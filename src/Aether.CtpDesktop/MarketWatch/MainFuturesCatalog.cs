using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Aether.CtpDesktop.MarketWatch;

/// <summary>主力合约目录的一条：CTP 规整代码 + 中文名 + 交易所简称。</summary>
public sealed record MainFuturesEntry(string Code, string Name, string Exchange);

/// <summary>
/// 期货主力合约目录——<b>客户端直接爬</b>东方财富「期货主力」板块（mk0830 + specificContract=true，
/// 一品种一条当前主力合约），不经过任何服务端。
/// <para>
/// 移植自 monorepo Web 服务端 <c>SecuritySyncService.SyncFromExternalAsync/ParseFutureData</c>——
/// 那套链路是「服务端爬 → 证券库 → 客户端增量同步」（Stockapp 桌面再从服务端拉）。本地桌面端没有
/// 服务端，把爬虫搬进客户端直接爬：连真实前置（SimNow）时订阅面 = 这份目录，而不是只有仿真柜台才有的
/// ax001。URL 与 Web 服务端同一写法（写死、不进配置）。
/// </para>
/// <para>
/// 实测口径（2026-10-25）：90 条 = 90 个品种各一条主力；其中 3 条是 DCE 月均合约
/// （pp2703F/v2701F/l2612F，非常规主力）——解析时过滤。<c>dm</c> 当前已是 CTP 规整形态
/// （CZCE 大写 3 位月份、CFFEX 大写、其余小写 4 位），规整步骤幂等、防源头大小写漂移。
/// </para>
/// </summary>
public sealed class MainFuturesCatalog
{
    /// <summary>东方财富期货主力板块（与 Web 服务端同 URL、同「写死」纪律）。</summary>
    public const string FuturesListUrl =
        "https://futsseapi.eastmoney.com/list/trans/block/risk/mk0830?orderBy=&sort=&pageSize=999&pageIndex=0&specificContract=true&platform=zbPC&field=name,p,zdf,vol,ccl,rz,tjd,cje,zde,o,h,l,zf,zjsj,zt,dt,dm,sc,tag,uid,zsjd";

    /// <summary>SimNow 行情前置（真实参考前置的 MD 腿）。只放公开地址——账号/密码按纪律只留工作区根 CLAUDE.md，绝不进仓。</summary>
    public const string SimNowMdFront = "tcp://182.254.243.31:40011";

    private static readonly Regex ContractPattern = new(@"^([A-Za-z]+)(\d+)$", RegexOptions.Compiled);
    private static readonly Regex MonthlyAveragePattern = new(@"^[A-Za-z]+\d+F$", RegexOptions.Compiled);

    /// <summary>东财 sc 交易所码 → 简称（服务端注释同表：113/114/115/142/220/225）。</summary>
    private static readonly IReadOnlyDictionary<int, string> ExchangeBySc = new Dictionary<int, string>
    {
        [113] = "SHFE", [114] = "DCE", [115] = "CZCE", [142] = "INE", [220] = "CFFEX", [225] = "GFEX",
    };

    private readonly HttpClient _httpClient;

    public MainFuturesCatalog(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);   // 与 Web 服务端同口径
    }

    /// <summary>爬取并解析主力合约目录。网络/解析失败直接抛——呈报口径归调用方（VM 的 StatusText）。</summary>
    public async Task<IReadOnlyList<MainFuturesEntry>> FetchAsync(CancellationToken ct = default)
    {
        var content = await _httpClient.GetStringAsync(FuturesListUrl, ct);
        return Parse(content);
    }

    /// <summary>纯解析（可单测）：dm/name/sc → 规整条目；过滤空代码与月均合约。</summary>
    public static IReadOnlyList<MainFuturesEntry> Parse(string content)
    {
        using var doc = JsonDocument.Parse(content);
        if (!doc.RootElement.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
            return Array.Empty<MainFuturesEntry>();

        var entries = new List<MainFuturesEntry>();
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var dm = item.TryGetProperty("dm", out var dmEl) ? dmEl.GetString() ?? "" : "";
            var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
            var sc = item.TryGetProperty("sc", out var scEl) && scEl.ValueKind == JsonValueKind.Number ? scEl.GetInt32() : 0;
            if (string.IsNullOrWhiteSpace(dm)) continue;
            if (MonthlyAveragePattern.IsMatch(dm)) continue;   // 月均合约（pp2703F 一族）：不是常规主力

            var code = NormalizeCode(dm);
            if (code.Length == 0) continue;

            var exchange = ExchangeBySc.TryGetValue(sc, out var ex) ? ex : "";
            if (exchange.Length == 0 && item.TryGetProperty("uid", out var uidEl)
                && uidEl.GetString() is { Length: > 0 } uid && uid.IndexOf('|') > 0)
            {
                exchange = uid[..uid.IndexOf('|')];   // uid 兜底（"CZCE|PX612" 前半段）——sc 表外的新交易所也能显示
            }
            entries.Add(new MainFuturesEntry(code, name, exchange));
        }
        return entries;
    }

    /// <summary>
    /// 合约代码 → CTP 订阅要求的形态：CZCE（3 位月份）与中金所前缀（IF/IC/IH/IM/T/TF/TS/TL）大写，
    /// 上期/大商/能源/广期小写。移植自 monorepo Stockapp <c>CtpViewModel.ToCtpInstrumentCode</c>
    /// （证券主数据存小写，CTP 对 CZCE/中金所区分大小写，不规整订阅会失败）；补 TL（三十债，原表漏）。
    /// </summary>
    public static string NormalizeCode(string code)
    {
        var m = ContractPattern.Match(code);
        if (!m.Success) return "";
        var prefix = m.Groups[1].Value;
        var digits = m.Groups[2].Value;
        var upper = digits.Length == 3 || IsCffexPrefix(prefix);
        prefix = upper ? prefix.ToUpperInvariant() : prefix.ToLowerInvariant();
        return prefix + digits;
    }

    private static bool IsCffexPrefix(string p) =>
        p.ToUpperInvariant() is "IF" or "IC" or "IH" or "IM" or "T" or "TF" or "TS" or "TL";
}

using System;
using System.Collections.Generic;
using System.Linq;
using Ctp.Net;

namespace Aether.Desktop.MarketWatch;

/// <summary>连接档案 chip 的 VM 包装（<see cref="CtpConnectionConfig"/> 非可观测——chip 清单在 MdFront 变化时整体重建）。</summary>
public sealed class ProfileChip
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public bool IsCurrent { get; init; }
}

/// <summary>
/// 连接档案的纯逻辑面（对齐 monorepo Stockapp <c>CtpConnectorViewModel</c> 的期货连接语义，
/// 2026-10-26 用户定调「管理是整个连接配置，参考 Aether 连接器的期货连接」）：
/// <b>档案 = 完整 <see cref="CtpConnectionConfig"/>，存 ⑦ 凭据仓（ctp.db，0600）</b>——含密码
/// （凭据仓是工作区钦定的密码存放处；preferences.json 仍绝不存密码）。
/// </summary>
public static class MarketProfiles
{
    /// <summary>内置兜底清单——首次启动（仓空）播种；「管理」里全删光后重启同样恢复（清单永不为空）。</summary>
    public static List<CtpConnectionConfig> BuiltIns() => new()
    {
        new() { Id = Guid.NewGuid(), Name = "本地", FrontAddress = "tcp://127.0.0.1:17801", MdFrontAddress = MarketWatchViewModel.LocalMdFront,
                BrokerId = "9999", UserId = "watching", Password = "",
                AppId = "simnow_client_test", AuthCode = "0000000000000000", AutoLogin = true },
        new() { Id = Guid.NewGuid(), Name = "远程", FrontAddress = "tcp://117.72.186.128:17801", MdFrontAddress = MarketWatchViewModel.RemoteMdFront,
                BrokerId = "9999", UserId = "watching", Password = "",
                AppId = "simnow_client_test", AuthCode = "0000000000000000", AutoLogin = true },
        new() { Id = Guid.NewGuid(), Name = "SimNow", FrontAddress = "tcp://182.254.243.31:40001", MdFrontAddress = MarketWatchViewModel.RealMdFront,
                BrokerId = "9999", UserId = "watching", Password = "",
                AppId = "simnow_client_test", AuthCode = "0000000000000000", AutoLogin = true },
    };

    /// <summary>
    /// 旧偏好档案的一次性迁移：仓空播种后，用旧 <c>market.mdFront</c>（/broker/user）挑出 last-used——
    /// 命中内置档案则标它；地址不在内置里则补一条「上次使用」档案。返回应标 last-used 的配置（可能为 null = 用默认）。
    /// </summary>
    public static CtpConnectionConfig? PickLastUsed(IEnumerable<CtpConnectionConfig> seeded,
        string? prefMdFront, string? prefBroker, string? prefUser)
    {
        var front = (prefMdFront ?? "").Trim();
        if (front.Length == 0) return null;

        var match = seeded.FirstOrDefault(p => string.Equals(p.MdFrontAddress.Trim(), front, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match;

        return new CtpConnectionConfig
        {
            Name = "上次使用",
            MdFrontAddress = front,
            BrokerId = string.IsNullOrWhiteSpace(prefBroker) ? "9999" : prefBroker.Trim(),
            UserId = string.IsNullOrWhiteSpace(prefUser) ? "watching" : prefUser.Trim(),
            AppId = "simnow_client_test",
            AuthCode = "0000000000000000",
            AutoLogin = true,
        };
    }

    /// <summary>
    /// 行情前置是否我们自己的仿真部署族（镜像 monorepo 连接器 <c>IsSimFront</c> 的纯文本判据，端口换成行情腿）：
    /// 任意 host 的 <c>:17802</c>（本地 127.0.0.1 与远程服务器部署统一按端口判）/ 旧 <c>127.0.0.1:17800</c> /
    /// <c>sim</c> 关键字；容忍 tcp:// 前缀与 localhost。<b>非仿真 = 真实柜台</b>（SimNow 等）——
    /// 自动爬主力合约的触发条件之一（另一个是品种还是 ax001 默认）。
    /// </summary>
    public static bool IsSimFrontAddress(string? mdFront)
    {
        var a = (mdFront ?? "").Trim();
        if (a.Length == 0) return false;
        if (string.Equals(a, "sim", StringComparison.OrdinalIgnoreCase)) return true;
        var bare = a.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase) ? a["tcp://".Length..] : a;
        bare = bare.Replace("localhost", "127.0.0.1", StringComparison.OrdinalIgnoreCase);
        return bare.EndsWith(":17802", StringComparison.OrdinalIgnoreCase)
            || string.Equals(bare, "127.0.0.1:17800", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>连接即建档的档案名：无同 UserId 的其它档案 → UserId；否则加前置区分后缀（如「botmm·sim」）。</summary>
    public static string BuildProfileName(IReadOnlyList<CtpConnectionConfig> profiles, Guid id, string userId, string mdFront)
    {
        var user = string.IsNullOrWhiteSpace(userId) ? "watching" : userId.Trim();
        var taken = profiles.Any(p => p.Id != id && string.Equals(p.UserId?.Trim(), user, StringComparison.OrdinalIgnoreCase));
        return taken ? $"{user}·{FrontTag(mdFront)}" : user;
    }

    /// <summary>前置区分标签：剥 tcp:// 取 host；解析不出用原文（纯文本判定——与 monorepo 连接器同款）。</summary>
    public static string FrontTag(string? front)
    {
        var value = (front ?? "").Trim();
        if (value.Length == 0) return "front";
        var host = value.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase) ? value["tcp://".Length..] : value;
        var colon = host.IndexOf(':');
        if (colon > 0) host = host[..colon];
        return host.Length == 0 ? value : host;
    }
}

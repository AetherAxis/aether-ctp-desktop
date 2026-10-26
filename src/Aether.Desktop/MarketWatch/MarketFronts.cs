using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aether.Workbench;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Desktop.MarketWatch;

/// <summary>一个行情前置条目（chip 的数据源）。<see cref="IsCurrent"/> 由 VM 按 <c>MdFront</c> 匹配维护。</summary>
public partial class MarketFront : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _mdFront = "";
    [ObservableProperty] private bool _isCurrent;

    public MarketFront() { }
    public MarketFront(string name, string mdFront)
    {
        Name = name;
        MdFront = mdFront;
    }
}

/// <summary>
/// 行情前置清单（2026-10-26 起可配置——用户「连接选择需要弄成配置的，比如弹出对话框，配置后保持最新」）：
/// 用户档案（偏好键 <c>market.fronts</c>，JSON 数组）优先；无档/坏档/空清单一律回退内置三个
/// （本地/远程/SimNow，同此前写死的 chips）。保存空清单 = 清键 = 下次加载回内置。
/// 档案格式（小写键，与旧 ConnectionPresets 档案风格一致）：[{"name":"本地","mdFront":"tcp://..."}]。
/// </summary>
public static class MarketFrontPresets
{
    private const string PrefKey = "market.fronts";

    private sealed record FrontDto(string Name, string MdFront);

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,   // name / mdFront
    };

    /// <summary>内置兜底清单——原三个写死 chip 的原值。</summary>
    public static IReadOnlyList<MarketFront> BuiltIn() => new List<MarketFront>
    {
        new("本地", MarketWatchViewModel.LocalMdFront),
        new("远程", MarketWatchViewModel.RemoteMdFront),
        new("SimNow", MarketWatchViewModel.RealMdFront),
    };

    /// <summary>加载：用户档优先（过滤空名/空址条目）；无档/坏档/过滤后为空 → 内置。清单永不为空。</summary>
    public static IReadOnlyList<MarketFront> Load(IPreferencesStore preferences)
    {
        var raw = preferences.GetAsync(PrefKey).GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(raw)) return BuiltIn();

        try
        {
            var dtos = JsonSerializer.Deserialize<List<FrontDto>>(raw, Json);
            var fronts = (dtos ?? new List<FrontDto>())
                .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.Name) && !string.IsNullOrWhiteSpace(d.MdFront))
                .Select(d => new MarketFront(d.Name.Trim(), d.MdFront.Trim()))
                .ToList();
            return fronts.Count > 0 ? fronts : BuiltIn();
        }
        catch (JsonException)
        {
            return BuiltIn();
        }
    }

    /// <summary>保存用户清单；空清单 = 清键（下次加载回内置——「全删光」即恢复默认，对话框里也这么提示）。</summary>
    public static void Save(IPreferencesStore preferences, IReadOnlyList<MarketFront> fronts)
    {
        if (fronts.Count == 0)
        {
            preferences.SetAsync(PrefKey, null).GetAwaiter().GetResult();
            return;
        }
        var raw = JsonSerializer.Serialize(
            fronts.Select(f => new FrontDto(f.Name.Trim(), f.MdFront.Trim())).ToList(), Json);
        preferences.SetAsync(PrefKey, raw).GetAwaiter().GetResult();
    }
}

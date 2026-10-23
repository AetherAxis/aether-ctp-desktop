using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Aether.Desktop.Connector;

/// <summary>
/// 连接预设清单的加载/序列化。预设原来硬编码在代码里（含一个公网地址）——换部署环境就得改代码重发。
/// 现在：<b>用户档案（偏好键 <c>connector.presets</c> 里的 JSON）优先，无档/坏档/空清单一律回退内置</b>，
/// 内置只作首次启动与兜底。
/// <para>档案格式（<c>instruments</c> 可省）：</para>
/// <code>[{"name":"本地仿真","front":"tcp://127.0.0.1:17801","mdFront":"tcp://127.0.0.1:17802","instruments":["ax001"]}]</code>
/// <para>容错纪律：**任何**字段问题都只丢那一条预设，绝不抛给启动路径——预设坏了不该让应用起不来。</para>
/// </summary>
public static class ConnectionPresets
{
    /// <summary>内置兜底预设（首次启动 / 档案缺失）。顺序即界面顺序，第 0 条同时是表单默认值。</summary>
    public static IReadOnlyList<ConnectionPreset> BuiltIn { get; } = new ConnectionPreset[]
    {
        new("本地仿真", "tcp://127.0.0.1:17801", "tcp://127.0.0.1:17802", new[] { "ax001" }),
        new("自有服务器", "tcp://117.72.186.128:17801", "tcp://117.72.186.128:17802", new[] { "ax001" }),
        new("SimNow", "tcp://180.168.146.187:10130", "tcp://180.168.146.187:10131", Array.Empty<string>()),
    };

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>从用户档案 JSON 读预设；无档/坏档/全部条目无效 → 内置兜底（保证非空）。</summary>
    public static IReadOnlyList<ConnectionPreset> Load(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return BuiltIn;
        try
        {
            var dtos = JsonSerializer.Deserialize<List<PresetDto>>(json, Json);
            if (dtos is null) return BuiltIn;
            var presets = dtos
                .Where(d => !string.IsNullOrWhiteSpace(d.Name) && !string.IsNullOrWhiteSpace(d.Front))
                .Select(d => new ConnectionPreset(
                    d.Name!.Trim(),
                    d.Front!.Trim(),
                    string.IsNullOrWhiteSpace(d.MdFront) ? d.Front!.Trim() : d.MdFront!.Trim(),
                    (d.Instruments ?? Array.Empty<string>())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim())
                        .ToArray()))
                .ToList();
            return presets.Count == 0 ? BuiltIn : presets;
        }
        catch (JsonException)
        {
            return BuiltIn;
        }
    }

    /// <summary>序列化用户档案（便于手工/运维生成）。</summary>
    public static string Serialize(IReadOnlyList<ConnectionPreset> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);
        var dtos = presets.Select(p => new PresetDto
        {
            Name = p.Name,
            Front = p.Front,
            MdFront = p.MdFront,
            Instruments = p.Instruments?.ToArray() ?? Array.Empty<string>(),
        }).ToList();
        return JsonSerializer.Serialize(dtos, new JsonSerializerOptions { WriteIndented = true });
    }

    private sealed class PresetDto
    {
        public string? Name { get; set; }
        public string? Front { get; set; }
        public string? MdFront { get; set; }
        public string[]? Instruments { get; set; }
    }
}

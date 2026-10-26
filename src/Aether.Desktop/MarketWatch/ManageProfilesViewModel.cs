using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Ctp.Net;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Desktop.MarketWatch;

/// <summary>
/// 「管理连接」对话框里的编辑副本（完整连接配置——对齐 ③ <see cref="CtpConnectionConfig"/> 字段面）。
/// <see cref="Id"/> 为空 = 新建（保存时分配）。
/// </summary>
public sealed class ProfileEntry
{
    public Guid Id { get; init; }
    public string Name { get; set; } = "";
    public string FrontAddress { get; set; } = "";
    public string MdFrontAddress { get; set; } = "";
    public string BrokerId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Password { get; set; } = "";
    public string AppId { get; set; } = "";
    public string AuthCode { get; set; } = "";
    public bool AutoLogin { get; set; } = true;

    public static ProfileEntry From(CtpConnectionConfig c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        FrontAddress = c.FrontAddress,
        MdFrontAddress = c.MdFrontAddress,
        BrokerId = c.BrokerId,
        UserId = c.UserId,
        Password = c.Password,
        AppId = c.AppId,
        AuthCode = c.AuthCode,
        AutoLogin = c.AutoLogin,
    };

    public CtpConnectionConfig ToConfig(bool isLastUsed) => new()
    {
        Id = Id == Guid.Empty ? Guid.NewGuid() : Id,
        Name = Name.Trim(),
        FrontAddress = FrontAddress.Trim(),
        MdFrontAddress = MdFrontAddress.Trim(),
        BrokerId = BrokerId.Trim(),
        UserId = UserId.Trim(),
        Password = Password,
        AppId = AppId.Trim(),
        AuthCode = AuthCode.Trim(),
        AutoLogin = AutoLogin,
        IsLastUsed = isLastUsed,
    };
}

/// <summary>
/// 「管理连接」对话框 VM：完整连接档案的增删改（chip 列表选条目 → 表单编辑全字段）。
/// 「保存」后 <see cref="Saved"/> 置真并请求关窗——落档（对 ⑦ 凭据仓做增/改/删的 diff）与 chip 刷新归
/// <see cref="MarketWatchViewModel"/>。取消/关窗 = 丢弃全部改动。
/// 表单字段两向绑定 <see cref="Selected"/> 的条目属性（编辑即时入副本）；条目属性本身无 INPC——
/// 程序化改动只在 <see cref="Add"/> 建档时发生（先建后选，无需回显）。
/// </summary>
public partial class ManageProfilesViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    /// <summary>档案编辑副本（含新建未保存的）。</summary>
    public ObservableCollection<ProfileEntry> Entries { get; } = new();

    /// <summary>当前编辑中的条目（表单绑定源；null = 未选——表单区隐藏）。</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private ProfileEntry? _selected;

    /// <summary>「保存」被点过。</summary>
    public bool Saved { get; private set; }

    /// <summary>VM 不持窗口引用——关窗经此事件，由窗口 code-behind 订阅。</summary>
    public event Action? CloseRequested;

    public ManageProfilesViewModel(IEnumerable<CtpConnectionConfig> current)
    {
        foreach (var c in current)
            Entries.Add(ProfileEntry.From(c));
        Selected = Entries.FirstOrDefault();
    }

    [RelayCommand]
    public void Select(ProfileEntry entry) => Selected = entry;

    [RelayCommand]
    public void Add()
    {
        var entry = new ProfileEntry
        {
            Name = "",
            FrontAddress = "",
            MdFrontAddress = "",
            BrokerId = "9999",
            UserId = "watching",
            Password = "",
            AppId = "simnow_client_test",
            AuthCode = "0000000000000000",
            AutoLogin = true,
        };
        Entries.Add(entry);
        Selected = entry;
    }

    [RelayCommand]
    public void RemoveSelected()
    {
        if (Selected is null) return;
        var index = Entries.IndexOf(Selected);
        Entries.Remove(Selected);
        Selected = Entries.Count > 0 ? Entries[Math.Min(index, Entries.Count - 1)] : null;
    }

    [RelayCommand]
    public void Save()
    {
        Saved = true;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    public void Cancel() => CloseRequested?.Invoke();

    /// <summary>有效条目（供保存）：行情前置非空（这是本 App 的连接必需项）；名称空则用 UserId。</summary>
    public IReadOnlyList<ProfileEntry> ToValidEntries() =>
        Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.MdFrontAddress))
            .Select(e =>
            {
                if (string.IsNullOrWhiteSpace(e.Name)) e.Name = e.UserId.Trim().Length > 0 ? e.UserId.Trim() : "未命名";
                return e;
            })
            .ToList();
}

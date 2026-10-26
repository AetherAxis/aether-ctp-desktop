using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Desktop.MarketWatch;

/// <summary>对话框里的一个可编辑条目（与 <see cref="MarketFront"/> 分开——编辑副本，确认才落档）。</summary>
public partial class FrontEntry : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _mdFront = "";

    public FrontEntry(string name, string mdFront)
    {
        Name = name;
        MdFront = mdFront;
    }
}

/// <summary>
/// 「管理行情前置」对话框 VM：编辑 <see cref="FrontEntry"/> 副本清单（增/删/改），「保存」后
/// <see cref="Saved"/> 置真并请求关窗——落档与 chip 刷新归 <see cref="MarketWatchViewModel"/>。
/// 保存时空名/空址条目会被过滤；全删光 = 恢复内置三个（见 <see cref="MarketFrontPresets.Save"/>）。
/// </summary>
public partial class ManageFrontsViewModel : ObservableObject
{
    public ObservableCollection<FrontEntry> Entries { get; } = new();

    /// <summary>「保存」被点过（取消/关窗 = false，改动丢弃）。</summary>
    public bool Saved { get; private set; }

    /// <summary>VM 不持窗口引用——关窗经此事件，由窗口 code-behind 订阅。</summary>
    public event Action? CloseRequested;

    public ManageFrontsViewModel(IEnumerable<MarketFront> current)
    {
        foreach (var f in current)
            Entries.Add(new FrontEntry(f.Name, f.MdFront));
    }

    [RelayCommand]
    private void Add() => Entries.Add(new FrontEntry("", ""));

    [RelayCommand]
    private void Remove(FrontEntry? entry)
    {
        if (entry is not null) Entries.Remove(entry);
    }

    [RelayCommand]
    private void Save()
    {
        Saved = true;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    /// <summary>有效条目（供保存）：非空名 + 非空址。</summary>
    public IReadOnlyList<MarketFront> ToFronts() =>
        Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Name) && !string.IsNullOrWhiteSpace(e.MdFront))
            .Select(e => new MarketFront(e.Name.Trim(), e.MdFront.Trim()))
            .ToList();
}

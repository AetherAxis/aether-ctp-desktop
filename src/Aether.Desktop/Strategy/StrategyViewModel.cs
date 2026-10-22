using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Desktop.Strategy;

/// <summary>
/// 策略页 ViewModel——回测 + 实盘运行面（M3）。
/// 策略列表从 labs 目录/已知算法列出；配置（合约/参数/分辨率）；回测和实盘的运行控制。
/// 实际策略执行经 ⑥ ILiveTradingRuntime / IBacktestRuntime（接线后续版本）。
/// </summary>
public partial class StrategyViewModel : ObservableObject
{
    // ── 策略列表 ──
    public ObservableCollection<StrategyInfo> Strategies { get; } = new()
    {
        new("market-maker", "做市商", "围绕最新价上下各5档阶梯限价单，给交易所供给连续深度"),
        new("noise-taker", "噪声交易者", "按节奏随机方向市价吃流动性，让盘口有成交、价格会动"),
    };

    [ObservableProperty] private StrategyInfo? _selectedStrategy;

    // ── 配置 ──
    [ObservableProperty] private string _instrument = "ax001";
    [ObservableProperty] private string _resolution = "Tick";
    [ObservableProperty] private int _maxVolume = 5;
    [ObservableProperty] private decimal _initialCash = 1_000_000;

    // ── 运行状态 ──
    [ObservableProperty] private string _runStatus = "未运行";
    [ObservableProperty] private string _statusText = "";

    [RelayCommand]
    private void SelectStrategy(StrategyInfo strategy) => SelectedStrategy = strategy;

    [RelayCommand]
    private void RunBacktest()
    {
        if (SelectedStrategy is null) { StatusText = "请先选择一个策略"; return; }
        RunStatus = "回测中（待 ⑥ IBacktestRuntime 接线）";
        StatusText = $"回测 {SelectedStrategy.Name} {Instrument} {Resolution}——⑥ LeanBacktestRunner 接线规划中";
    }

    [RelayCommand]
    private void StartLive()
    {
        if (SelectedStrategy is null) { StatusText = "请先选择一个策略"; return; }
        RunStatus = "实盘运行中（待 ⑥ ILiveTradingRuntime 接线）";
        StatusText = $"实盘 {SelectedStrategy.Name} {Instrument}——⑥ ILiveTradingRuntime 接线规划中。当前实盘经 ⑧ CtpConsole --run-strategy 运行";
    }

    [RelayCommand]
    private void Stop()
    {
        RunStatus = "已停止";
        StatusText = "";
    }
}

/// <summary>策略信息。</summary>
public record StrategyInfo(string Name, string Title, string Description);

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ctp.Net;

namespace Aether.Desktop.Futures;

/// <summary>
/// 期货页 ViewModel——行情板 + 账户/持仓/委托查询。
/// 连接管理全在 <see cref="Connector.ConnectorViewModel"/>（独立 tab）；本 VM 被动消费已连接的 <see cref="ICtpSession"/>：
/// 监听 ConnectionStateChanged → 连上启动轮询、断开清数据；TickReceived → 行情板 + 持仓现价/浮动盈亏。
/// </summary>
public partial class FuturesViewModel : ObservableObject
{
    private readonly ICtpSession _session;
    private readonly Dictionary<string, TickRow> _rowsByInstrument = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _statusText = "未连接";
    [ObservableProperty] private string _connectionState = "Closed";

    [ObservableProperty] private string _balanceText = "—";
    [ObservableProperty] private string _availableText = "—";
    [ObservableProperty] private string _accountIdText = "—";

    public ObservableCollection<TickRow> Quotes { get; } = new();
    public ObservableCollection<PositionRow> Positions { get; } = new();
    public ObservableCollection<OrderRow> Orders { get; } = new();

    public FuturesViewModel(ICtpSession session)
    {
        _session = session;

        // SDK 回调在后台线程 → 统一 marshal 到 UI 线程
        _session.ConnectionStateChanged += (_, s) => Dispatcher.UIThread.Post(() => OnConnectionState(s));
        _session.TickReceived += (_, t) => Dispatcher.UIThread.Post(() => OnTick(t));
        _session.OrderReceived += (_, o) => Dispatcher.UIThread.Post(() =>
            StatusText = $"[委托] {o.InstrumentId} {DirOffsetText(o.Direction, o.OffsetFlag)} {o.VolumeTotalOriginal}@{o.LimitPrice} 状态={(char)o.OrderStatusRaw} ref={o.OrderRef}");
        _session.TradeReceived += (_, t) => Dispatcher.UIThread.Post(() =>
            StatusText = $"[成交] {t.InstrumentId} {DirOffsetText(t.Direction, t.OffsetFlag)} {t.Volume}@{t.Price} ref={t.OrderRef}");

        // 被动同步当前状态
        IsConnected = _session.IsConnected;
        if (IsConnected) _ = RefreshAllAsync();
    }

    private void OnConnectionState(CtpConnectionStatus s)
    {
        ConnectionState = s.ToString();
        IsConnected = s == CtpConnectionStatus.Connected;
        if (IsConnected)
        {
            StatusText = "已连接";
            _ = RefreshAllAsync();
        }
        else
        {
            StatusText = s == CtpConnectionStatus.Closed ? "闭市" : "未连接";
            Positions.Clear();
            Orders.Clear();
            BalanceText = AvailableText = AccountIdText = "—";
        }
    }

    // ── 刷新命令（手动） ──

    [RelayCommand]
    private async Task RefreshAsync() => await RefreshAllAsync();

    [RelayCommand]
    private async Task RefreshOrdersAsync()
    {
        if (!IsConnected) return;
        try
        {
            var orders = await _session.QueryOrderAsync();
            Orders.Clear();
            foreach (var o in orders)
                Orders.Add(new OrderRow(
                    o.OrderRef, o.InstrumentId, DirOffsetText(o.Direction, o.OffsetFlag),
                    o.LimitPrice, o.VolumeTotalOriginal, o.VolumeTraded,
                    OrderStatusText((char)o.OrderStatusRaw), o.StatusMsg ?? ""));
        }
        catch (Exception ex)
        {
            StatusText = $"委托查询失败（空表 30s 超时为已知边界）：{ex.Message}";
        }
    }

    private async Task RefreshAllAsync()
    {
        await RefreshAccountAsync();
        await RefreshPositionsAsync();
    }

    private async Task RefreshAccountAsync()
    {
        if (!IsConnected) return;
        try
        {
            var account = await _session.QueryAccountAsync();
            AccountIdText = account.AccountId;
            BalanceText = account.Balance.ToString("N2");
            AvailableText = account.Available.ToString("N2");
        }
        catch (Exception ex) { StatusText = $"账户查询失败：{ex.Message}"; }
    }

    private async Task RefreshPositionsAsync()
    {
        if (!IsConnected) return;
        try
        {
            var positions = await _session.QueryPositionAsync();
            Positions.Clear();
            foreach (var p in positions.Where(p => p.TotalPosition != 0))
            {
                var tick = _rowsByInstrument.TryGetValue(p.InstrumentId, out var tr) ? tr.Last : 0;
                Positions.Add(new PositionRow
                {
                    Instrument = p.InstrumentId,
                    Direction = p.PosiDirection == CtpPosiDirection.Short ? "空" : "多",
                    Total = p.TotalPosition,
                    Today = p.TodayPosition,
                    Yesterday = p.YdPosition,
                    AvgPrice = tick,
                    CurrentPrice = tick,
                    Profit = 0,
                });
            }
        }
        catch (Exception ex) { StatusText = $"持仓查询失败：{ex.Message}"; }
    }

    private void OnTick(CtpTick t)
    {
        if (!_rowsByInstrument.TryGetValue(t.InstrumentId, out var row))
        {
            row = new TickRow { Instrument = t.InstrumentId };
            _rowsByInstrument[t.InstrumentId] = row;
            Quotes.Add(row);
        }
        var prevLast = row.Last;
        row.Last = t.LastPrice;
        row.Change = prevLast == 0 ? 0 : Math.Round(t.LastPrice - prevLast, 2);
        row.Bid = t.BidPrice;
        row.BidVolume = t.BidVolume;
        row.Ask = t.AskPrice;
        row.AskVolume = t.AskVolume;
        row.Volume = t.Volume;
        row.Time = $"{t.UpdateTime}.{t.UpdateMillisec:000}";

        // 持仓现价/浮动盈亏随 tick 更新
        for (int i = 0; i < Positions.Count; i++)
        {
            if (Positions[i].Instrument.Equals(t.InstrumentId, StringComparison.OrdinalIgnoreCase))
            {
                var p = Positions[i];
                var sign = p.Direction == "多" ? 1 : -1;
                p.CurrentPrice = t.LastPrice;
                p.Profit = Math.Round(sign * p.Total * (t.LastPrice - p.AvgPrice), 2);
            }
        }
    }

    // ── 显示格式化 ──

    internal static string DirOffsetText(CtpDirection d, CtpOffsetFlag o) => $"{DirText(d)}{OffsetText(o)}";
    internal static string DirText(CtpDirection d) => d switch
    {
        CtpDirection.Buy => "买", CtpDirection.Sell => "卖", _ => d.ToString(),
    };
    internal static string OffsetText(CtpOffsetFlag o) => o switch
    {
        CtpOffsetFlag.Open => "开", CtpOffsetFlag.Close => "平",
        CtpOffsetFlag.CloseToday => "平今", CtpOffsetFlag.CloseYesterday => "平昨",
        _ => o.ToString(),
    };
    internal static string OrderStatusText(char raw) => raw switch
    {
        '0' => "全成", '1' => "部成在队", '2' => "部成作废", '3' => "未成交在队",
        '4' => "已作废", '5' => "已撤销", 'a' => "在途", _ => $"状态{raw}",
    };
}

/// <summary>行情板行（按 instrument 原位更新——UI 线程）。</summary>
public partial class TickRow : ObservableObject
{
    public string Instrument { get; init; } = "";
    [ObservableProperty] private double _last;
    [ObservableProperty] private double _change;
    [ObservableProperty] private double _bid;
    [ObservableProperty] private int _bidVolume;
    [ObservableProperty] private double _ask;
    [ObservableProperty] private int _askVolume;
    [ObservableProperty] private long _volume;
    [ObservableProperty] private string _time = "";
}

/// <summary>持仓行。</summary>
public partial class PositionRow : ObservableObject
{
    public string Instrument { get; init; } = "";
    public string Direction { get; init; } = "";
    public int Total { get; init; }
    public int Today { get; init; }
    public int Yesterday { get; init; }
    public double AvgPrice { get; init; }
    [ObservableProperty] private double _currentPrice;
    [ObservableProperty] private double _profit;
}

/// <summary>委托行。</summary>
public sealed record OrderRow(string Ref, string Instrument, string DirOffset, double Price, int Volume, int Traded, string Status, string Message);

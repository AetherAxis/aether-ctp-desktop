using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Aether.Desktop.Connector;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ctp.Net;

namespace Aether.Desktop.Futures;

/// <summary>
/// 交易页 ViewModel——登录栏（<see cref="Connector"/>）+ 账户/持仓/委托查询。
/// 纯被动消费已连接的 <see cref="ICtpSession"/>：监听 ConnectionStateChanged → 连上启动轮询、断开清数据；
/// TickReceived → 只更新持仓现价（看行情归行情页——2026-10-25 布局收敛前去掉了本页自带的行情板，
/// 它与行情页完全重复；tick 仍进来，只是不再铺一块板）。
/// </summary>
public partial class FuturesViewModel : ObservableObject
{
    private readonly ICtpSession _session;
    /// <summary>最近成交价缓存（tick → 持仓「现价」列 + 持仓刷新时兜底）。</summary>
    private readonly Dictionary<string, double> _lastPrice = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>登录控制器（寄居本页顶栏——原独立「连接器」tab，2026-10-25 布局收敛）。</summary>
    public ConnectorViewModel Connector { get; }

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _statusText = "未连接";
    [ObservableProperty] private string _connectionState = "Closed";

    [ObservableProperty] private string _balanceText = "—";
    [ObservableProperty] private string _availableText = "—";
    [ObservableProperty] private string _accountIdText = "—";

    /// <summary>账户/持仓轮询间隔。类注释一直宣称「连上启动轮询」，但代码里只有建连与手动刷新才查——
    /// 这里补上真的轮询（3s，与 README 一致）。只查账户+持仓（委托表全量查询在空表时 30s 超时，
    /// 留给手动刷新，见 <see cref="RefreshOrdersCommand"/>）。</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private readonly DispatcherTimer _pollTimer = new() { Interval = PollInterval };
    private bool _pollBusy;

    public ObservableCollection<PositionRow> Positions { get; } = new();
    public ObservableCollection<OrderRow> Orders { get; } = new();

    public FuturesViewModel(ICtpSession session, ConnectorViewModel connector)
    {
        _session = session;
        Connector = connector;

        // SDK 回调在后台线程 → 统一 marshal 到 UI 线程
        _session.ConnectionStateChanged += (_, s) => Dispatcher.UIThread.Post(() => OnConnectionState(s));
        _session.TickReceived += (_, t) => Dispatcher.UIThread.Post(() => OnTick(t));
        _session.OrderReceived += (_, o) => Dispatcher.UIThread.Post(() =>
            StatusText = $"[委托] {o.InstrumentId} {DirOffsetText(o.Direction, o.OffsetFlag)} {o.VolumeTotalOriginal}@{o.LimitPrice} 状态={(char)o.OrderStatusRaw} ref={o.OrderRef}");
        _session.TradeReceived += (_, t) => Dispatcher.UIThread.Post(() =>
            StatusText = $"[成交] {t.InstrumentId} {DirOffsetText(t.Direction, t.OffsetFlag)} {t.Volume}@{t.Price} ref={t.OrderRef}");

        // 被动同步当前状态
        IsConnected = _session.IsConnected;
        if (IsConnected)
        {
            _ = RefreshAllAsync();
            _pollTimer.Start();
        }

        // 轮询拍（UI 线程 DispatcherTimer → 查询本身 await，不阻塞渲染）
        _pollTimer.Tick += async (_, _) => await PollAsync();
    }

    private void OnConnectionState(CtpConnectionStatus s)
    {
        ConnectionState = s.ToString();
        IsConnected = s == CtpConnectionStatus.Connected;
        if (IsConnected)
        {
            StatusText = "已连接";
            _ = RefreshAllAsync();
            _pollTimer.Start();
        }
        else
        {
            StatusText = s == CtpConnectionStatus.Closed ? "闭市" : "未连接";
            _pollTimer.Stop();
            _pollBusy = false;
            Positions.Clear();
            Orders.Clear();
            BalanceText = AvailableText = AccountIdText = "—";
        }
    }

    /// <summary>轮询一拍：重入保护（上一拍还没回来就跳过，避免断线窗口里堆积）。</summary>
    private async Task PollAsync()
    {
        if (!IsConnected || _pollBusy) return;
        _pollBusy = true;
        try
        {
            await RefreshAccountAsync();
            await RefreshPositionsAsync();
        }
        finally { _pollBusy = false; }
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
                var tick = _lastPrice.TryGetValue(p.InstrumentId, out var last) ? last : 0;
                Positions.Add(new PositionRow
                {
                    Instrument = p.InstrumentId,
                    Direction = p.PosiDirection == CtpPosiDirection.Short ? "空" : "多",
                    Total = p.TotalPosition,
                    Today = p.TodayPosition,
                    Yesterday = p.YdPosition,
                    // 持仓成本来自柜台（Σ 开仓价×手数×乘数）。旧代码拿最新价冒充「开仓均价」，
                    // 行情还没到时是 0，浮亏也就只能硬填 0——那是**假数据**，比空着更糟。
                    // 均价要除合约乘数（本契约不含合约信息），所以列名改成「持仓成本」按原值展示。
                    PositionCost = p.PositionCost,
                    CurrentPrice = tick,
                    // 浮盈用柜台口径（含昨仓结算价基准），与「最新价 − 开仓价 × 手数」不同，别自己算。
                    Profit = p.PositionProfit,
                });
            }
        }
        catch (Exception ex) { StatusText = $"持仓查询失败：{ex.Message}"; }
    }

    private void OnTick(CtpTick t)
    {
        // 行情板已随布局收敛移除（看行情归行情页）；tick 在交易页只剩一个消费点：持仓「现价」列。
        // 浮盈仍然只由 3s 轮询从柜台取（PositionProfit 含昨仓结算价基准）——不用价差自己算。
        _lastPrice[t.InstrumentId] = t.LastPrice;
        for (int i = 0; i < Positions.Count; i++)
        {
            if (Positions[i].Instrument.Equals(t.InstrumentId, StringComparison.OrdinalIgnoreCase))
                Positions[i].CurrentPrice = t.LastPrice;
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
    /// <summary>
    /// 委托状态 → 中文。**状态码解析交给契约 <see cref="CtpOrderStatus"/>**（<c>raw.ToStatus()</c>），
    /// 本方法只负责文案——原来这里自带一份 char switch，漏了 <c>'a'=未知</c>（显示成「在途」，等于把
    /// 「柜台没给状态」说成「在路上」）和 <c>'b'/'c'</c>（条件单未触发/已触发）。
    /// </summary>
    internal static string OrderStatusText(char raw)
    {
        var status = ((byte)raw).ToStatus();
        // 契约不认识的码归 Unknown；只有真的是 'a' 才显示「未知」，其它码保留原始码好排查
        if (status == CtpOrderStatus.Unknown && raw != 'a') return $"状态{raw}";
        return OrderStatusText(status);
    }

    internal static string OrderStatusText(CtpOrderStatus status) => status switch
    {
        CtpOrderStatus.AllTraded => "全成",
        CtpOrderStatus.PartTradedQueueing => "部成在队",
        CtpOrderStatus.PartTradedNotQueueing => "部成作废",
        CtpOrderStatus.NoTradeQueueing => "未成交在队",
        CtpOrderStatus.NoTradeNotQueueing => "已作废",
        CtpOrderStatus.Canceled => "已撤销",
        CtpOrderStatus.Unknown => "未知",
        CtpOrderStatus.NotTouched => "条件单未触发",
        CtpOrderStatus.Touched => "条件单已触发",
        _ => $"状态{(byte)status}",
    };
}

/// <summary>持仓行。</summary>
public partial class PositionRow : ObservableObject
{
    public string Instrument { get; init; } = "";
    public string Direction { get; init; } = "";
    public int Total { get; init; }
    public int Today { get; init; }
    public int Yesterday { get; init; }
    /// <summary>持仓成本（元，柜台 <c>PositionCost</c>）。均价 = 本值 ÷ (手数 × 合约乘数)——乘数不在契约里，故不折算。</summary>
    public double PositionCost { get; init; }
    [ObservableProperty] private double _currentPrice;
    [ObservableProperty] private double _profit;
}

/// <summary>委托行。</summary>
public sealed record OrderRow(string Ref, string Instrument, string DirOffset, double Price, int Volume, int Traded, string Status, string Message);

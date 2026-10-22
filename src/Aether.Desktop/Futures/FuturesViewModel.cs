using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aether.Workbench;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ctp.Net;

namespace Aether.Desktop.Futures;

/// <summary>
/// 期货页 ViewModel——M1：连前置（本地/服务器同形）→ 行情板 + 账户/持仓/委托查询。
/// 组件与 console 同款（⑦ ICtpSession 官方链路）；「bot 在跑」的证据就是持仓在动、挂单躺在委托页、权益在变。
/// <para>轮询：DispatcherTimer 3s 查账户+持仓（两条查询空表都正常回派——SDK 已钉）；
/// <b>委托查询不自动轮询</b>——已知边界（空表 SDK 不派发 17B 应答 ⇒ 30s 超时 + 管线停摆，会打断 tick 流），
/// 手动「刷新委托」按钮触发，超时捕获进状态栏不传染。</para>
/// </summary>
public partial class FuturesViewModel : ObservableObject
{
    private readonly ICtpSession _session;
    private readonly IPreferencesStore _preferences;
    private readonly DispatcherTimer _poll;
    private readonly Dictionary<string, TickRow> _rowsByInstrument = new(StringComparer.OrdinalIgnoreCase);

    private const string PrefFront = "futures.front";
    private const string PrefMdFront = "futures.mdfront";
    private const string PrefUser = "futures.user";
    private const string PrefPass = "futures.pass";
    private const string PrefInstruments = "futures.instruments";

    public FuturesViewModel(ICtpSession session, IPreferencesStore preferences)
    {
        _session = session;
        _preferences = preferences;

        // 启动同步恢复上次登录档案（JsonPreferencesStore.GetAsync 同步完成，UI 线程安全）
        Front = _preferences.GetAsync(PrefFront).GetAwaiter().GetResult() ?? "tcp://127.0.0.1:17801";
        MdFront = _preferences.GetAsync(PrefMdFront).GetAwaiter().GetResult() ?? "tcp://127.0.0.1:17802";        UserId = _preferences.GetAsync(PrefUser).GetAwaiter().GetResult() ?? "";
        Password = _preferences.GetAsync(PrefPass).GetAwaiter().GetResult() ?? "";
        Instruments = _preferences.GetAsync(PrefInstruments).GetAwaiter().GetResult() ?? "ax001";

        // SDK 回调在后台线程 → 统一 marshal 到 UI 线程再动集合
        _session.ConnectionStateChanged += (_, s) => Dispatcher.UIThread.Post(() => OnConnectionState(s));
        _session.TickReceived += (_, t) => Dispatcher.UIThread.Post(() => OnTick(t));
        _session.OrderReceived += (_, o) => Dispatcher.UIThread.Post(() => StatusText = $"[委托] {o.InstrumentId} {DirOffsetText(o.Direction, o.OffsetFlag)} {o.VolumeTotalOriginal}@{o.LimitPrice} 状态={(char)o.OrderStatusRaw} ref={o.OrderRef}");
        _session.TradeReceived += (_, t) => Dispatcher.UIThread.Post(() => StatusText = $"[成交] {t.InstrumentId} {DirOffsetText(t.Direction, t.OffsetFlag)} {t.Volume}@{t.Price} ref={t.OrderRef}");

        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _poll.Tick += (_, _) => _ = PollAsync();

        // ★ 自动订阅行情——MD 腿不需要交易登录（⑤ 注释："凭据不校验——行情订阅无账户语义"）
        // 打开 App 即看行情，登录按钮只管交易（账户/持仓/委托查询）
        _ = AutoSubscribeAsync();
    }

    /// <summary>自动订阅行情（无需登录——MD 独立腿，凭据不校验）。用配置里的行情前置 + 默认合约。</summary>
    private async Task AutoSubscribeAsync()
    {
        try
        {
            var symbols = Instruments.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (symbols.Length == 0) return;
            StatusText = "行情订阅中…";
            await _session.SubscribeMarketDataAsync(new CtpMarketDataConnect
            {
                MdFrontAddress = MdFront.Trim(),
                BrokerId = "9999",
                UserId = string.IsNullOrWhiteSpace(UserId) ? "guest" : UserId.Trim(),
                Password = Password ?? "",
            }, symbols);
            StatusText = $"已订阅 {string.Join(",", symbols)}（行情不需要登录；点「登录」查账户/持仓/委托）";
        }
        catch (Exception ex)
        {
            StatusText = $"行情订阅失败：{ex.Message}（检查行情前置地址）";
        }
    }

    // ── 连接预设（本地/服务器 = 自有仿真前置→ax001；SimNow = 真实前置→真实合约）──
    public IReadOnlyList<ConnectionPreset> Presets { get; } = new[]
    {
        new ConnectionPreset("本地", "tcp://127.0.0.1:17801", "tcp://127.0.0.1:17802", "ax001"),
        new ConnectionPreset("服务器", "tcp://117.72.186.128:17801", "tcp://117.72.186.128:17802", "ax001"),
        new ConnectionPreset("SimNow", "tcp://180.168.146.187:10130", "tcp://180.168.146.187:10131", "m2701"),
    };

    private ConnectionPreset? _selectedPreset;
    public ConnectionPreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetProperty(ref _selectedPreset, value) && value != null)
            {
                Front = value.Front;
                MdFront = value.MdFront;
                Instruments = value.DefaultInstrument;
                // 清旧行情、切订阅（MD 不需要登录）
                Quotes.Clear();
                _rowsByInstrument.Clear();
                StatusText = $"已切到「{value.Name}」（{value.Front}，合约 {value.DefaultInstrument}）";
                _ = AutoSubscribeAsync();
            }
        }
    }

    // ── 登录档案 ──

    [ObservableProperty] private string _front = "";
    [ObservableProperty] private string _mdFront = "";
    [ObservableProperty] private string _userId = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _broker = "9999";
    [ObservableProperty] private string _appId = "simnow_client_test";
    [ObservableProperty] private string _authCode = "0000000000000000";
    /// <summary>订阅合约（逗号分隔；默认 ax001——本地实验室/服务器板的合约）。</summary>
    [ObservableProperty] private string _instruments = "ax001";

    // ── 状态 ──

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _statusText = "未连接";
    [ObservableProperty] private string _connectionState = "Closed";

    // ── 行情板 ──

    public ObservableCollection<TickRow> Quotes { get; } = new();

    // ── 账户/持仓/委托 ──

    [ObservableProperty] private string _balanceText = "—";
    [ObservableProperty] private string _availableText = "—";
    [ObservableProperty] private string _accountIdText = "—";

    public ObservableCollection<PositionRow> Positions { get; } = new();
    public ObservableCollection<OrderRow> Orders { get; } = new();

    // ── 命令 ──

    /// <summary>登录/登出切换（登录栏按钮——按连接状态路由）。</summary>
    [RelayCommand]
    private Task ToggleConnectionAsync() => IsConnected ? LogoutAsync() : LoginAsync();

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsConnected) { StatusText = "已连接（先登出再换档案）"; return; }
        StatusText = "登录中…";
        try
        {
            var login = await _session.LoginAsync(new CtpLoginRequest
            {
                FrontAddress = Front.Trim(),
                MdFrontAddress = MdFront.Trim(),
                BrokerId = Broker.Trim(),
                UserId = UserId.Trim(),
                Password = Password,
                AppId = AppId.Trim(),
                AuthCode = AuthCode.Trim(),
            });
            // 登录档案持久化（密码明文入档——本地 sim 工具，同 console config.json 语义）
            await _preferences.SetAsync(PrefFront, Front.Trim());
            await _preferences.SetAsync(PrefMdFront, MdFront.Trim());
            await _preferences.SetAsync(PrefUser, UserId.Trim());
            await _preferences.SetAsync(PrefPass, Password);
            await _preferences.SetAsync(PrefInstruments, Instruments.Trim());
            StatusText = $"已登录：交易日 {login.TradingDay}（行情已在启动时自动订阅）";
            _ = RefreshAccountPositionsAsync();
            _ = RefreshOrdersAsync();
            _poll.Start();
        }
        catch (Exception ex)
        {
            StatusText = $"登录失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        _poll.Stop();
        try { _session.Logout(); }
        catch (Exception ex) { StatusText = $"登出异常：{ex.Message}"; }
        Quotes.Clear();
        _rowsByInstrument.Clear();
        Positions.Clear();
        Orders.Clear();
        BalanceText = AvailableText = AccountIdText = "—";
        StatusText = "已登出";
    }

    /// <summary>手动刷新（账户/持仓/委托一起查）。</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RefreshAccountPositionsAsync();
        await RefreshOrdersAsync();
    }

    /// <summary>手动刷新委托（不自动轮询——空表 30s 超时已知边界）。</summary>
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
            StatusText = $"委托查询失败（空表 30s 超时为已知边界，管线会自恢复）：{ex.Message}";
        }
    }

    // ── 轮询（账户+持仓——空表正常回派，可安全轮询） ──

    private async Task PollAsync()
    {
        if (!IsConnected) return;
        await RefreshAccountPositionsAsync();
    }

    private async Task RefreshAccountPositionsAsync()
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
                    AvgPrice = tick, // 近似开仓价——⑤ verbatim 已知边界，用当前行情价近似
                    CurrentPrice = tick,
                    Profit = 0,
                });
            }
        }
        catch (Exception ex) { StatusText = $"持仓查询失败：{ex.Message}"; }
    }

    // ── 事件（已在 UI 线程） ──

    private void OnConnectionState(CtpConnectionStatus s)
    {
        ConnectionState = s.ToString();
        IsConnected = s == CtpConnectionStatus.Connected;
        if (!IsConnected) _poll.Stop();
        StatusText = IsConnected ? "已连接" : $"连接状态：{s}";
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

        // 持仓现价/浮动盈亏随 tick 更新——⑤ 持仓应答金额字段是 verbatim（已知边界），客户端计算
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
        CtpDirection.Buy => "买",
        CtpDirection.Sell => "卖",
        _ => d.ToString(),
    };

    internal static string OffsetText(CtpOffsetFlag o) => o switch
    {
        CtpOffsetFlag.Open => "开",
        CtpOffsetFlag.Close => "平",
        CtpOffsetFlag.CloseToday => "平今",
        CtpOffsetFlag.CloseYesterday => "平昨",
        _ => o.ToString(),
    };

    /// <summary>报单状态码 → 短文本（THOST_FTDC_OST_* char 码）。</summary>
    internal static string OrderStatusText(char raw) => raw switch
    {
        '0' => "全成",
        '1' => "部成在队",
        '2' => "部成作废",
        '3' => "未成交在队",
        '4' => "已作废",
        '5' => "已撤销",
        'a' => "在途",
        _ => $"状态{raw}",
    };
}

/// <summary>连接预设（快速切换本地/远程/真实前置）。</summary>
public record ConnectionPreset(string Name, string Front, string MdFront, string DefaultInstrument);

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

/// <summary>持仓行。开仓均价是近似值（⑤ 持仓应答金额字段是 verbatim——无真实入场价，首次 tick 取当前价）。
/// 现价/浮动盈亏随 tick 实时更新。</summary>
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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Aether.Workbench;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ctp.Net;

namespace Aether.Desktop.MarketWatch;

/// <summary>
/// 行情页 ViewModel —— 一个「打开就能看」的行情终端：只连行情前置（MD 腿，不需要交易登录），
/// 订阅品种后把 tick 铺成看板。存在的理由：两个 bot 在跑的时候，需要一个能**肉眼看到**价格在动的面。
/// <para>
/// 与 <see cref="Connector.ConnectorViewModel"/> 的分工：连接器管交易登录（那才有账户/持仓/委托）；
/// 本页只消费行情腿，且**不依赖交易登录**——冷启动即自动订阅，省掉「先登录才能看行情」这一步。
/// 凭据口径：本前置/⑤ 与 SimNow 的 MD 腿都不校验（SimNow 2026-10-25 实证空密码可用）；
/// 其它真实前置经 MD 密码框现填（会话内存，不落盘）。
/// </para>
/// <para>
/// 共用同一条 MD 连接（<see cref="ICtpSession"/> 单例）：若有人在连接器里用别的账号登录并订阅，
/// MD 连接会按那个账号重建，本页订阅随之失效——所以这里显示「静默 N 秒」并留一个「重新订阅」按钮，
/// 不做后台偷偷重建（两页互抢订阅比失效更难查）。
/// </para>
/// <para>
/// 订阅面（2026-10-25 起）：仿真前置（本地/远程）= 手输品种（默认 ax001）；真实前置（SimNow）=
/// <see cref="MainFuturesCatalog"/> 客户端直爬的主力合约目录（ax001 在真实柜台不存在——自动替换，
/// 用户手输过品种则尊重输入）。「主力合约」按钮任何时候可手动现爬现订。
/// </para>
/// </summary>
public partial class MarketWatchViewModel : ObservableObject
{
    private const string PrefMdFront = "market.mdFront";
    private const string PrefSymbols = "market.symbols";
    private const string PrefBroker = "market.broker";
    private const string PrefUser = "market.user";

    /// <summary>
    /// 远程前置（服务器上部署的交易所 + 前置）。默认<b>本地</b>前置——见 <see cref="LocalMdFront"/> 的注释。
    /// </summary>
    public const string RemoteMdFront = "tcp://117.72.186.128:17802";
    /// <summary>
    /// 本地前置（本机 17802，转连远程交易所 117.72.186.128:17800）——**默认值**。
    /// <para>
    /// 默认连本地的理由是「标准开发拓扑」（两个 bot → 本地前置 → 远程交易所，见根 CLAUDE.md）——
    /// 行情随 bot 链走。**远程前置同样会推流**（2026-10-26 活跃行情下复测：远程 17802 在 30 秒
    /// 活跃窗口推 72 笔 tick；2026-10-25 记的「远程只回快照 2 帧、不推流」是市场静默期的误判，
    /// 已推翻）——bots 直连远程前置、或本机不起本地前置时，点「远程」chip 即可。
    /// </para>
    /// </summary>
    public const string LocalMdFront = "tcp://127.0.0.1:17802";
    /// <summary>
    /// 真实前置（SimNow 行情腿）。ax001 只存在于我们的仿真柜台——连真实前置时默认品种无意义，
    /// 订阅面自动切换为爬取的主力合约目录（见 <see cref="MainFuturesCatalog"/>）。只放公开地址，
    /// SimNow 账号/密码按纪律留工作区根 CLAUDE.md，不进仓也不进偏好。
    /// </summary>
    public const string RealMdFront = MainFuturesCatalog.SimNowMdFront;
    public const string DefaultSymbols = "ax001";

    /// <summary>tick 速率的统计窗口（秒）。行情前置每秒推一次快照，5s 窗口既能看出「断了」也不会跳得太厉害。</summary>
    private const double RateWindowSeconds = 5;
    /// <summary>静默告警阈值：超过这么久没有 tick 就在状态栏提示（前置断开 / 被别的账号重建了 MD）。</summary>
    private const double SilentSeconds = 10;

    private readonly ICtpSession _session;
    private readonly IPreferencesStore _preferences;
    private readonly MainFuturesCatalog _mainFutures;
    private readonly Dictionary<string, QuoteRow> _rowsByInstrument = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>爬取到的主力合约中文名（代码 → 名称）——看板「名称」列的数据源；手输品种查不到就留空。</summary>
    private Dictionary<string, string> _namesByInstrument = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<DateTime> _recentTicks = new();
    private readonly DispatcherTimer _pulse = new() { Interval = TimeSpan.FromSeconds(1) };

    private DateTime _lastTickAt = DateTime.MinValue;

    public MarketWatchViewModel(ICtpSession session, MainFuturesCatalog mainFutures, IPreferencesStore preferences)
    {
        _session = session;
        _mainFutures = mainFutures;
        _preferences = preferences;

        // 启动同步恢复上次的订阅档案（与连接器同款：GetAsync 同步完成）
        MdFront = _preferences.GetAsync(PrefMdFront).GetAwaiter().GetResult() ?? LocalMdFront;
        Broker = _preferences.GetAsync(PrefBroker).GetAwaiter().GetResult() ?? "9999";
        User = _preferences.GetAsync(PrefUser).GetAwaiter().GetResult() ?? "watching";
        SymbolInput = _preferences.GetAsync(PrefSymbols).GetAwaiter().GetResult() ?? DefaultSymbols;

        LoadFronts();   // 前置清单：用户档（market.fronts）优先，回退内置本地/远程/SimNow

        SyncRows(SplitSymbols(SymbolInput));

        _session.TickReceived += (_, t) => Dispatcher.UIThread.Post(() => OnTick(t));
        _session.ConnectionStateChanged += (_, s) => Dispatcher.UIThread.Post(() => OnConnectionState(s));

        _pulse.Tick += (_, _) => UpdatePulse();
        _pulse.Start();

        // 冷启动即订阅：行情腿不需要账号密码，打开就该看到 tick（连不上时状态栏会说话）
        _ = SubscribeAsync();
    }

    // ── 订阅档案（界面绑定） ──

    [ObservableProperty] private string _mdFront = LocalMdFront;
    [ObservableProperty] private string _broker = "9999";
    [ObservableProperty] private string _user = "watching";
    /// <summary>
    /// MD 密码（可选）：本前置与 SimNow 的 MD 腿都不校验（SimNow 2026-10-25 实证：watching+空密码
    /// 登录/订阅/87 合约 tick 流全通）——**SimNow 留空即可**。其它真实前置（期货公司柜台）通常校验，
    /// 框留着给那类前置。**会话内存而已，绝不落盘**（与连接器「密码不落盘」同政策）。
    /// </summary>
    [ObservableProperty] private string _mdPassword = "";
    /// <summary>订阅品种（逗号分隔）。点「订阅」时整表替换看板行。</summary>
    [ObservableProperty] private string _symbolInput = DefaultSymbols;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isSubscribed;

    /// <summary>动作/错误文案（订阅中、订阅失败…）——与每秒刷新的 <see cref="FeedText"/> 分开，免得淹没错误。</summary>
    [ObservableProperty] private string _statusText = "准备订阅…";
    /// <summary>每秒刷新的连接/流量口径（前置、tick 速率、最近 tick、静默提示）。</summary>
    [ObservableProperty] private string _feedText = "—";
    [ObservableProperty] private double _tickRate;
    [ObservableProperty] private long _tickCount;
    [ObservableProperty] private string _lastTickText = "—";

    public ObservableCollection<QuoteRow> Quotes { get; } = new();

    public string RateWindowHint => $"{RateWindowSeconds:0}s 窗口";

    // ── 命令 ──

    /// <summary>
    /// 订阅（整表替换品种）。增量订阅新合约；品种被删掉时只从看板移除、不主动退订（契约无逐合约退订）。
    /// 连真实前置且品种还是仿真默认（ax001）时，先爬主力合约目录替换订阅面（见 <see cref="MainFuturesCatalog"/>）。
    /// </summary>
    [RelayCommand]
    private async Task SubscribeAsync()
    {
        if (IsBusy) return;

        var symbols = SplitSymbols(SymbolInput);
        if (symbols.Count == 0)
        {
            StatusText = "请先填品种（逗号分隔，例如 ax001）";
            return;
        }
        if (string.IsNullOrWhiteSpace(MdFront))
        {
            StatusText = "请先填行情前置地址（tcp://host:port）";
            return;
        }

        IsBusy = true;
        try
        {
            // 真实前置 + 仿真默认品种 ⇒ 直接爬主力合约（ax001 在真实柜台不存在，订阅了也永远静默）。
            // 用户手输过品种（≠ ax001 默认）则尊重输入——那是明确的订阅意图。
            var fromCrawl = false;
            if (IsRealFrontAddress(MdFront) && IsSimOnlyBoard(symbols))
            {
                try
                {
                    StatusText = "真实前置：爬取主力合约…";
                    var entries = await _mainFutures.FetchAsync();
                    if (entries.Count > 0)
                    {
                        RememberNames(entries);
                        symbols = entries.Select(e => e.Code).ToList();
                        fromCrawl = true;
                    }
                }
                catch (Exception ex)
                {
                    StatusText = $"主力合约爬取失败（{ex.Message}）——按输入品种订阅";
                }
            }

            await SubscribeSymbolsAsync(symbols, fromCrawl);
        }
        finally
        {
            IsBusy = false;
            UpdatePulse();
        }
    }

    /// <summary>
    /// 手动爬主力合约并订阅（当前前置无关——真实前置的标准看板面；冷启动自动爬取失败后也可用它重试）。
    /// 爬取结果不写进 <see cref="SymbolInput"/> / 偏好：主力按月换约，存档会过期；每次现爬现订阅。
    /// </summary>
    [RelayCommand]
    private async Task MainFuturesAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(MdFront))
        {
            StatusText = "请先填行情前置地址（tcp://host:port）";
            return;
        }

        IsBusy = true;
        try
        {
            StatusText = "爬取主力合约…";
            var entries = await _mainFutures.FetchAsync();
            if (entries.Count == 0)
            {
                StatusText = "主力合约目录为空（数据源无返回）";
                return;
            }
            RememberNames(entries);
            await SubscribeSymbolsAsync(entries.Select(e => e.Code).ToList(), fromCrawl: true);
        }
        catch (Exception ex)
        {
            StatusText = $"主力合约爬取失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
            UpdatePulse();
        }
    }

    /// <summary>订阅核心（命令共用，<b>不管理 <see cref="IsBusy"/></b>——由各命令包持）：铺看板行 → 落偏好（爬取来源不落品种档）→ 连 MD 订阅 → 回放会话缓存。</summary>
    private async Task SubscribeSymbolsAsync(IReadOnlyList<string> symbols, bool fromCrawl)
    {
        StatusText = fromCrawl ? $"订阅中：主力合约 {symbols.Count} 个…" : $"订阅中：{string.Join(",", symbols)}…";
        try
        {
            SyncRows(symbols);
            await _preferences.SetAsync(PrefMdFront, MdFront.Trim());
            // 爬取来的品种不落档：主力按月换约，存档跨月即过期；下次连真实前置重新现爬
            if (!fromCrawl)
                await _preferences.SetAsync(PrefSymbols, string.Join(",", symbols));
            await _preferences.SetAsync(PrefBroker, Broker.Trim());
            await _preferences.SetAsync(PrefUser, User.Trim());

            await _session.SubscribeMarketDataAsync(new CtpMarketDataConnect
            {
                MdFrontAddress = MdFront.Trim(),
                BrokerId = string.IsNullOrWhiteSpace(Broker) ? "9999" : Broker.Trim(),
                UserId = string.IsNullOrWhiteSpace(User) ? "watching" : User.Trim(),
                Password = MdPassword,   // 本前置与 SimNow MD 均不校验（SimNow 实证可空）；其它真实前置需要——不落盘，现填
            }, symbols);

            // 会话缓存里可能已有这些合约的 tick（重订阅/切页后立刻有数，不用等下一拍）
            foreach (var (instrument, tick) in _session.GetAllLatestTicks())
                if (_rowsByInstrument.TryGetValue(instrument, out var row) && !row.HasQuote)
                    row.Apply(tick);

            IsSubscribed = _session.IsMdSubscribed;
            StatusText = IsSubscribed
                ? (fromCrawl ? $"已订阅主力合约 {symbols.Count} 个（{MdFront}）" : $"已订阅 {string.Join(",", symbols)}（{MdFront}）")
                : "订阅请求已发出，但 MD 连接未建立";
        }
        catch (Exception ex)
        {
            IsSubscribed = _session.IsMdSubscribed;
            StatusText = $"行情订阅失败：{ex.Message}";
        }
    }

    /// <summary>断开行情（释放 MD 连接并退订）。注意：这条连接是与交易页共用的。</summary>
    [RelayCommand]
    private void Disconnect()
    {
        try
        {
            _session.UnsubscribeMarketData();
            StatusText = "已断开行情";
        }
        catch (Exception ex)
        {
            StatusText = $"断开行情异常：{ex.Message}";
        }

        IsSubscribed = _session.IsMdSubscribed;
        foreach (var row in Quotes) row.Reset();
        _recentTicks.Clear();
        _lastTickAt = DateTime.MinValue;
        UpdatePulse();
    }

    // ── 前置清单（可配置：用户档案优先，内置兜底——见 MarketFrontPresets） ──

    /// <summary>前置 chip 数据源（名称 → 地址）。「管理」对话框保存后即时重载并持久化。</summary>
    public ObservableCollection<MarketFront> Fronts { get; } = new();

    private void LoadFronts()
    {
        Fronts.Clear();
        foreach (var f in MarketFrontPresets.Load(_preferences))
            Fronts.Add(f);
        UpdateCurrentChip();
    }

    /// <summary>当前 <see cref="MdFront"/> 变化 → 重算各 chip 的 IsCurrent 高亮（按地址匹配）。</summary>
    partial void OnMdFrontChanged(string value) => UpdateCurrentChip();

    private void UpdateCurrentChip()
    {
        var current = (MdFront ?? "").Trim();
        foreach (var f in Fronts)
            f.IsCurrent = current.Length > 0 && string.Equals(current, f.MdFront.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>点前置 chip = 填入地址（订阅动作归「订阅」按钮——与手输地址同一条路）。</summary>
    [RelayCommand]
    private void UseFront(MarketFront? front)
    {
        if (front is not null && !string.IsNullOrWhiteSpace(front.MdFront))
            MdFront = front.MdFront;
    }

    /// <summary>打开「管理行情前置」对话框：编辑副本，保存后落档 + 重载 chip（保持最新配置）。</summary>
    [RelayCommand]
    private async Task ManageFrontsAsync()
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) return;

        var vm = new ManageFrontsViewModel(Fronts);
        var dialog = new ManageFrontsDialog { DataContext = vm };
        vm.CloseRequested += dialog.Close;
        await dialog.ShowDialog(owner);
        vm.CloseRequested -= dialog.Close;

        if (!vm.Saved) return;   // 取消/关窗：改动丢弃

        var fronts = vm.ToFronts();
        MarketFrontPresets.Save(_preferences, fronts);
        LoadFronts();   // 空清单 → Save 清键 → Load 回内置三个
        StatusText = fronts.Count > 0
            ? $"前置清单已保存（{fronts.Count} 个）"
            : "前置清单已清空——恢复内置（本地/远程/SimNow）";
    }

    // ── 前置/品种判定 ──

    /// <summary>是否真实前置（SimNow 行情腿）。地址完全匹配才判真——我们自己的仿真前置（本地/远程）各有专属地址。</summary>
    internal static bool IsRealFrontAddress(string? mdFront) =>
        string.Equals((mdFront ?? "").Trim(), RealMdFront, StringComparison.OrdinalIgnoreCase);

    /// <summary>看板品种是否还是仿真默认（ax001 一族）——真实前置上这些代码不存在，才轮到自动爬主力替换。</summary>
    internal static bool IsSimOnlyBoard(IReadOnlyList<string> symbols) =>
        symbols.Count > 0 && symbols.All(s => string.Equals(s, DefaultSymbols, StringComparison.OrdinalIgnoreCase));

    private void RememberNames(IReadOnlyList<MainFuturesEntry> entries) =>
        _namesByInstrument = entries.ToDictionary(e => e.Code, e => e.Name, StringComparer.OrdinalIgnoreCase);

    // ── 数据流 ──

    private void OnTick(CtpTick tick)
    {
        // 只喂看板上的合约：同一条 MD 连接上可能还挂着交易页/策略订阅的别的合约（IF2612 之类），
        // 它们进来会让「品种数」和速率对不上看板，直接忽略。
        if (!_rowsByInstrument.TryGetValue(tick.InstrumentId, out var row)) return;

        row.Apply(tick);
        _lastTickAt = DateTime.UtcNow;
        _recentTicks.Enqueue(_lastTickAt);
        TickCount++;
    }

    private void OnConnectionState(CtpConnectionStatus status)
    {
        var wasSubscribed = IsSubscribed;
        IsSubscribed = _session.IsMdSubscribed;
        if (wasSubscribed && !IsSubscribed)
            StatusText = status == CtpConnectionStatus.Closed
                ? "交易连接已关闭，行情连接随之释放——点「订阅」重新连行情"
                : "交易连接状态变化，行情连接已释放——点「订阅」重新连行情";
    }

    /// <summary>每秒一拍：算速率、判断静默、刷新口径行。</summary>
    private void UpdatePulse()
    {
        var now = DateTime.UtcNow;
        while (_recentTicks.Count > 0 && (now - _recentTicks.Peek()).TotalSeconds > RateWindowSeconds)
            _recentTicks.Dequeue();

        TickRate = MarketMath.TickRate(_recentTicks.Count, RateWindowSeconds);
        IsSubscribed = _session.IsMdSubscribed;

        if (_lastTickAt == DateTime.MinValue)
        {
            LastTickText = "—";
            FeedText = IsSubscribed
                ? $"MD 已连接 · {Quotes.Count} 品种 · 等待首笔 tick（{RateWindowHint}）"
                : "MD 未连接";
            return;
        }

        LastTickText = _lastTickAt.ToLocalTime().ToString("HH:mm:ss.fff");
        var silentSeconds = (now - _lastTickAt).TotalSeconds;
        var silent = silentSeconds > SilentSeconds;
        var feed = $"MD {(IsSubscribed ? "已连接" : "未连接")} · {Quotes.Count} 品种 · {TickRate:F1} tick/s（{RateWindowHint}） · 最近 tick {LastTickText}";
        if (silent)
            feed += $" · 已静默 {silentSeconds:F0}s（前置断开，或 MD 被别的账号重建——点「订阅」重连）";
        FeedText = feed;
    }

    // ── 看板行维护 ──

    /// <summary>把看板行对齐到品种清单：保留已有行的数据，删掉移除的，新合约追加到末尾。名称列取爬取目录的中文名（查不到留空）。</summary>
    private void SyncRows(IReadOnlyList<string> symbols)
    {
        var wanted = new HashSet<string>(symbols, StringComparer.OrdinalIgnoreCase);

        for (int i = Quotes.Count - 1; i >= 0; i--)
        {
            if (wanted.Contains(Quotes[i].Instrument)) continue;
            _rowsByInstrument.Remove(Quotes[i].Instrument);
            Quotes.RemoveAt(i);
        }

        foreach (var symbol in symbols)
        {
            if (_rowsByInstrument.ContainsKey(symbol)) continue;
            var row = new QuoteRow { Instrument = symbol, Name = _namesByInstrument.GetValueOrDefault(symbol) ?? "" };
            _rowsByInstrument[symbol] = row;
            Quotes.Add(row);
        }
    }

    internal static IReadOnlyList<string> SplitSymbols(string? raw) => (raw ?? "")
        .Split(new[] { ',', '，', ' ', ';', '；' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}

/// <summary>
/// 行情看板的一行。**按合约原位更新**（不重建集合——重建会让滚动位置和选中态每 tick 抖一次）。
/// 涨跌基准逐行记录：柜台给了昨结/昨收就用它，没给就用本会话首笔，并在行上标出口径。
/// </summary>
public partial class QuoteRow : ObservableObject
{
    public string Instrument { get; init; } = "";

    /// <summary>中文名（主力合约目录爬取时带出；手输/仿真品种无来源则空）。</summary>
    public string Name { get; init; } = "";

    /// <summary>涨跌基准价（0 = 尚未确定）。</summary>
    public double Reference { get; private set; }
    /// <summary>基准口径标签（昨结/昨收/首笔）。</summary>
    [ObservableProperty] private string _basis = MarketMath.BasisFirst;
    public double LastValue { get; private set; }
    public bool HasQuote { get; private set; }

    [ObservableProperty] private string _lastText = "—";
    [ObservableProperty] private string _changeText = "—";
    [ObservableProperty] private string _percentText = "—";
    [ObservableProperty] private string _bidText = "—";
    [ObservableProperty] private string _askText = "—";
    [ObservableProperty] private string _bidVolumeText = "—";
    [ObservableProperty] private string _askVolumeText = "—";
    [ObservableProperty] private string _volumeText = "—";
    [ObservableProperty] private string _openInterestText = "—";
    [ObservableProperty] private string _timeText = "—";
    [ObservableProperty] private string _arrow = "·";

    [ObservableProperty] private bool _isUp;
    [ObservableProperty] private bool _isDown;
    /// <summary>最新价变动次数（同一价位被反复推送不算）——「行情在动」的直接证据。</summary>
    [ObservableProperty] private int _moves;

    /// <summary>吃进一笔 tick。返回 true 表示最新价相对上一笔变了。</summary>
    public bool Apply(CtpTick tick)
    {
        var previous = LastValue;
        var hadQuote = HasQuote;
        // LastPrice<=0 = 柜台的无值哨兵（DBL_MAX 已在映射层归零）：保持上一笔，不能把价格"归零"。
        var last = tick.LastPrice > 0 ? tick.LastPrice : previous;

        UpdateBasis(tick, last);

        LastValue = last;
        HasQuote = true;

        var change = MarketMath.Change(last, Reference);
        var percent = MarketMath.ChangePercent(last, Reference);
        var direction = MarketMath.Direction(change);

        LastText = MarketMath.PriceText(last);
        ChangeText = MarketMath.ChangeText(change);
        PercentText = MarketMath.PercentText(percent);
        Arrow = direction > 0 ? "▲" : direction < 0 ? "▼" : "·";
        IsUp = direction > 0;
        IsDown = direction < 0;

        BidText = tick.BidPrice > 0 ? MarketMath.PriceText(tick.BidPrice) : "—";
        AskText = tick.AskPrice > 0 ? MarketMath.PriceText(tick.AskPrice) : "—";
        BidVolumeText = tick.BidPrice > 0 ? tick.BidVolume.ToString() : "—";
        AskVolumeText = tick.AskPrice > 0 ? tick.AskVolume.ToString() : "—";
        VolumeText = tick.Volume.ToString("N0");
        OpenInterestText = tick.OpenInterest > 0 ? tick.OpenInterest.ToString("N0") : "—";
        TimeText = MarketMath.TimeText(tick.UpdateTime, tick.UpdateMillisec);

        if (hadQuote && last > 0 && previous > 0 && Math.Abs(last - previous) > 1e-9)
        {
            Moves++;
            return true;
        }
        return false;
    }

    /// <summary>基准：先取柜台昨结/昨收；都没有就锁本会话首笔。后续 tick 若补上了昨结，改用昨结。</summary>
    private void UpdateBasis(CtpTick tick, double last)
    {
        var (reference, basis) = MarketMath.PickBasis(tick.PreSettlement, tick.PreClose, Reference > 0 ? Reference : last);
        // 已锁首笔且本笔仍无柜台基准 → 保持原基准（sessionFirst 传的是 Reference，值不变）
        if (Reference <= 0 || basis == MarketMath.BasisSettlement)
        {
            Reference = reference;
            Basis = basis;
        }
    }

    /// <summary>清空数值（断开行情后保留行，避免看板"跳空"）。</summary>
    public void Reset()
    {
        Reference = 0;
        LastValue = 0;
        HasQuote = false;
        LastText = ChangeText = PercentText = "—";
        BidText = AskText = BidVolumeText = AskVolumeText = VolumeText = OpenInterestText = "—";
        TimeText = "—";
        Arrow = "·";
        IsUp = IsDown = false;
        Moves = 0;
        Basis = MarketMath.BasisFirst;
    }
}

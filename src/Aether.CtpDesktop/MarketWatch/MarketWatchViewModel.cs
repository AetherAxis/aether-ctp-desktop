using System;
using System.Collections.Concurrent;
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

namespace Aether.CtpDesktop.MarketWatch;

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
    private readonly ICtpConfigStore _configStore;
    private readonly Dictionary<string, QuoteRow> _rowsByInstrument = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>爬取到的主力合约中文名（代码 → 名称）——看板「名称」列的数据源；手输品种查不到就留空。</summary>
    private Dictionary<string, string> _namesByInstrument = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// 速率采样：**每拍一条**（拍时间, 该拍内的 tick 数），不再逐笔入队。
    /// 逐笔入队在 2026-10-29 事故里暴露成问题——上游同一条行情被重复投递 28 万次/秒时，
    /// 队列每秒涨几十万项，光进出队就够 UI 卡住。
    /// </summary>
    private readonly Queue<(DateTime At, long Count)> _rateSamples = new();
    /// <summary>看板在册的合约。行情回调线程要读（过滤），故用并发字典；UI 线程在 <see cref="SyncRows"/> 维护。</summary>
    private readonly ConcurrentDictionary<string, byte> _boardInstruments = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>回调线程写入的「每合约最新一笔」，由每秒一拍在 UI 线程取走。</summary>
    private readonly ConcurrentDictionary<string, CtpTick> _pendingTicks = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>自上一拍以来收到的看板 tick 数（回调线程累加，UI 线程取走并清零）。</summary>
    private long _ticksSincePulse;
    /// <summary>最近一笔看板 tick 的 UTC ticks（回调线程写，避免跨线程碰 <see cref="_lastTickAt"/>）。</summary>
    private long _lastTickAtTicks = DateTime.MinValue.Ticks;

    private readonly DispatcherTimer _pulse = new() { Interval = TimeSpan.FromSeconds(1) };

    private DateTime _lastTickAt = DateTime.MinValue;

    public MarketWatchViewModel(ICtpSession session, MainFuturesCatalog mainFutures,
        ICtpConfigStore configStore, IPreferencesStore preferences)
    {
        _session = session;
        _mainFutures = mainFutures;
        _configStore = configStore;
        _preferences = preferences;

        // 订阅品种仍走偏好（连接配置已迁 ⑦ 凭据仓——见 InitializeConnection）
        SymbolInput = _preferences.GetAsync(PrefSymbols).GetAwaiter().GetResult() ?? DefaultSymbols;

        // 连接配置恢复：last-used 档案（仓空时播种内置 + 旧偏好一次性迁移）；AutoLogin 旗标决定冷启动是否自动订阅
        var autoConnect = InitializeConnection();

        SyncRows(SplitSymbols(SymbolInput));

        // 行情回调线程只做「记下每合约最新一笔」（O(1)，不碰 UI），由每秒一拍在 UI 线程合并刷新。
        // 逐笔 Dispatcher.Post 的代价在 2026-10-29 事故里现过原形：上游同一条行情被重复投递
        // 28 万次/秒时，主线程被 Post 淹掉，界面表现成假死。
        _session.TickReceived += (_, t) => EnqueueTick(t);
        _session.ConnectionStateChanged += (_, s) => Dispatcher.UIThread.Post(() => OnConnectionState(s));

        _pulse.Tick += (_, _) => UpdatePulse();
        _pulse.Start();

        // 冷启动即订阅：行情腿不需要账号密码，打开就该看到 tick（连不上时状态栏会说话）；
        // last-used 档案关了「启动自动连接」则等用户点「订阅」
        if (autoConnect)
            _ = SubscribeAsync();
    }

    /// <summary>
    /// 连接配置恢复（构造期同步完成）：仓空 → 播种内置档案（本地/远程/SimNow）+ 旧 market.* 偏好一次性迁移
    /// （命中内置标 last-used，地址不在内置里补「上次使用」档）；再按 last-used 恢复表单（行情前置/经纪商/
    /// 用户号/密码）。返回是否启动自动连接（= last-used 档案的 AutoLogin；无档案默认开——保持旧行为）。
    /// </summary>
    private bool InitializeConnection()
    {
        var profiles = _configStore.GetAllAsync().GetAwaiter().GetResult();
        if (profiles.Count == 0)
        {
            var seeded = MarketProfiles.BuiltIns();
            var lastUsed = MarketProfiles.PickLastUsed(seeded,
                _preferences.GetAsync(PrefMdFront).GetAwaiter().GetResult(),
                _preferences.GetAsync(PrefBroker).GetAwaiter().GetResult(),
                _preferences.GetAsync(PrefUser).GetAwaiter().GetResult());
            foreach (var p in seeded)
                _configStore.SaveAsync(p).GetAwaiter().GetResult();
            if (lastUsed is not null)
            {
                lastUsed.IsLastUsed = true;   // SaveAsync 事务内清其它行旗标（契约唯一性不变量）
                _configStore.SaveAsync(lastUsed).GetAwaiter().GetResult();
            }
            profiles = _configStore.GetAllAsync().GetAwaiter().GetResult();
        }

        var cfg = _configStore.GetAsync().GetAwaiter().GetResult();
        if (cfg is not null)
        {
            MdFront = cfg.MdFrontAddress.Length > 0 ? cfg.MdFrontAddress : LocalMdFront;
            Broker = cfg.BrokerId.Length > 0 ? cfg.BrokerId : "9999";
            User = cfg.UserId.Length > 0 ? cfg.UserId : "watching";
            MdPassword = cfg.Password;
        }
        else
        {
            MdFront = LocalMdFront;
            Broker = "9999";
            User = "watching";
        }
        RefreshChips();
        return cfg?.AutoLogin ?? true;
    }

    // ── 订阅档案（界面绑定） ──

    [ObservableProperty] private string _mdFront = LocalMdFront;
    [ObservableProperty] private string _broker = "9999";
    [ObservableProperty] private string _user = "watching";
    /// <summary>
    /// MD 密码：本前置与 SimNow 的 MD 腿都不校验（SimNow 2026-10-25 实证：watching+空密码
    /// 登录/订阅/87 合约 tick 流全通）——**SimNow 留空即可**；其它真实前置（期货公司柜台）通常校验。
    /// **随连接档案存 ⑦ 凭据仓（ctp.db，0600）**——monorepo 连接器同款（「连接即建档」一并存）；
    /// preferences.json 仍绝不存密码。
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
            // 真实柜台（非我们的仿真部署族——见 MarketProfiles.IsSimFrontAddress 的端口判据）+ 仿真默认品种
            // ⇒ 直接爬主力合约（ax001 在真实柜台不存在，订阅了也永远静默）。
            // 用户手输过品种（≠ ax001 默认）则尊重输入——那是明确的订阅意图。
            var fromCrawl = false;
            if (!MarketProfiles.IsSimFrontAddress(MdFront) && IsSimOnlyBoard(symbols))
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

    /// <summary>订阅核心（命令共用，<b>不管理 <see cref="IsBusy"/></b>——由各命令包持）：铺看板行 → 落品种偏好（爬取来源不落）→ 连 MD 订阅 → 连接即建档 → 回放会话缓存。</summary>
    private async Task SubscribeSymbolsAsync(IReadOnlyList<string> symbols, bool fromCrawl)
    {
        StatusText = fromCrawl ? $"订阅中：主力合约 {symbols.Count} 个…" : $"订阅中：{string.Join(",", symbols)}…";
        try
        {
            SyncRows(symbols);
            // 爬取来的品种不落档：主力按月换约，存档跨月即过期；下次连真实柜台重新现爬。
            // 连接配置不落偏好——归 ⑦ 凭据仓（连接即建档，见下方 SaveProfileOnConnect）。
            if (!fromCrawl)
                await _preferences.SetAsync(PrefSymbols, string.Join(",", symbols));

            await _session.SubscribeMarketDataAsync(new CtpMarketDataConnect
            {
                MdFrontAddress = MdFront.Trim(),
                BrokerId = string.IsNullOrWhiteSpace(Broker) ? "9999" : Broker.Trim(),
                UserId = string.IsNullOrWhiteSpace(User) ? "watching" : User.Trim(),
                Password = MdPassword,   // 本前置与 SimNow MD 均不校验（SimNow 实证可空）；其它真实前置需要
            }, symbols);

            // 连接即自动建档（monorepo 连接器语义：键 = 行情前置 + 用户号，失败尝试也留档——误档可在「管理」里删）
            await SaveProfileOnConnectAsync();

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

    /// <summary>连接即建档：当前表单值 upsert 成 last-used 档案（命中「行情前置 + 用户号」复用 Id 与交易侧字段）。</summary>
    private async Task SaveProfileOnConnectAsync()
    {
        try
        {
            var mdFront = MdFront.Trim();
            var user = (string.IsNullOrWhiteSpace(User) ? "watching" : User).Trim();
            var profiles = await _configStore.GetAllAsync();
            var existing = profiles.FirstOrDefault(p =>
                string.Equals(p.MdFrontAddress.Trim(), mdFront, StringComparison.OrdinalIgnoreCase)
                && string.Equals(p.UserId.Trim(), user, StringComparison.OrdinalIgnoreCase));

            var name = existing?.Name ?? MarketProfiles.BuildProfileName(profiles, Guid.Empty, user, mdFront);
            await _configStore.SaveAsync(new CtpConnectionConfig
            {
                Id = existing?.Id ?? Guid.NewGuid(),
                Name = name,
                FrontAddress = existing?.FrontAddress ?? "",
                MdFrontAddress = mdFront,
                BrokerId = (string.IsNullOrWhiteSpace(Broker) ? "9999" : Broker).Trim(),
                UserId = user,
                Password = MdPassword,
                AppId = existing?.AppId ?? "simnow_client_test",
                AuthCode = existing?.AuthCode ?? "0000000000000000",
                AutoLogin = existing?.AutoLogin ?? true,
                IsLastUsed = true,   // SaveAsync 事务内清其它行旗标（契约唯一性不变量）
            });
            RefreshChips();
        }
        catch
        {
            /* 建档失败不阻断行情——chip 少一条而已，下次连接重试 */
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
        _rateSamples.Clear();
        _pendingTicks.Clear();
        Interlocked.Exchange(ref _ticksSincePulse, 0);
        Interlocked.Exchange(ref _lastTickAtTicks, DateTime.MinValue.Ticks);
        _lastTickAt = DateTime.MinValue;
        UpdatePulse();
    }

    // ── 连接档案（完整配置：⑦ 凭据仓 CtpConnectionConfig，chips = 档案） ──

    /// <summary>连接档案 chips（名称 → 完整配置）。点 chip = 套用全部连接字段并直接订阅（monorepo 连接器语义）。</summary>
    public ObservableCollection<ProfileChip> Profiles { get; } = new();

    /// <summary>当前 <see cref="MdFront"/> 变化 → 重建 chips（高亮 = 地址匹配的档案）。</summary>
    partial void OnMdFrontChanged(string value) => RefreshChips();

    private void RefreshChips()
    {
        var current = (MdFront ?? "").Trim();
        Profiles.Clear();
        foreach (var p in _configStore.GetAllAsync().GetAwaiter().GetResult())
            Profiles.Add(new ProfileChip
            {
                Id = p.Id,
                Name = string.IsNullOrWhiteSpace(p.Name) ? p.UserId : p.Name,
                IsCurrent = current.Length > 0 && string.Equals(current, p.MdFrontAddress.Trim(), StringComparison.OrdinalIgnoreCase),
            });
    }

    /// <summary>点档案 chip = 套用完整连接配置（行情前置/经纪商/用户号/密码）+ 标 last-used + 直接订阅。</summary>
    [RelayCommand]
    private async Task UseProfileAsync(ProfileChip? chip)
    {
        if (chip is null || IsBusy) return;
        var cfg = _configStore.GetAllAsync().GetAwaiter().GetResult()
            .FirstOrDefault(p => p.Id == chip.Id);
        if (cfg is null) return;   // chips 刚重建的极端时序——静默跳过

        MdFront = cfg.MdFrontAddress;
        Broker = cfg.BrokerId;
        User = cfg.UserId;
        MdPassword = cfg.Password;

        cfg.IsLastUsed = true;
        await _configStore.SaveAsync(cfg);
        RefreshChips();

        await SubscribeAsync();
    }

    /// <summary>打开「管理连接档案」对话框：完整配置（前置×2/经纪商/用户/密码/AppId/授权码/AutoLogin）增删改，保存后持久化并保持最新。</summary>
    [RelayCommand]
    private async Task ManageProfilesAsync()
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) return;

        var before = _configStore.GetAllAsync().GetAwaiter().GetResult();
        var vm = new ManageProfilesViewModel(before);
        var dialog = new ManageProfilesDialog { DataContext = vm };
        vm.CloseRequested += dialog.Close;
        await dialog.ShowDialog(owner);
        vm.CloseRequested -= dialog.Close;

        if (!vm.Saved) return;   // 取消/关窗：改动丢弃

        // diff 落档：删除的删掉，其余 upsert；last-used 尽量保住（被删则标第一条，仓空留空 = 下次启动播种内置）
        var after = vm.ToValidEntries();
        var afterIds = after.Select(e => e.Id).ToHashSet();
        foreach (var removed in before.Where(p => !afterIds.Contains(p.Id)))
            await _configStore.DeleteAsync(removed.Id);

        Guid? lastUsedId = before.FirstOrDefault(p => p.IsLastUsed)?.Id;
        var keepLastUsed = lastUsedId.HasValue && after.Any(e => e.Id == lastUsedId);
        foreach (var entry in after)
            await _configStore.SaveAsync(entry.ToConfig(isLastUsed: keepLastUsed && lastUsedId == entry.Id));
        if (!keepLastUsed && after.Count > 0)
            await _configStore.SaveAsync(after[0].ToConfig(isLastUsed: true));

        RefreshChips();
        StatusText = after.Count > 0
            ? $"连接档案已保存（{after.Count} 个）"
            : "连接档案已清空——重启后恢复内置（本地/远程/SimNow）";
    }

    // ── 前置/品种判定 ──

    /// <summary>看板品种是否还是仿真默认（ax001 一族）——真实前置上这些代码不存在，才轮到自动爬主力替换。</summary>
    internal static bool IsSimOnlyBoard(IReadOnlyList<string> symbols) =>
        symbols.Count > 0 && symbols.All(s => string.Equals(s, DefaultSymbols, StringComparison.OrdinalIgnoreCase));

    private void RememberNames(IReadOnlyList<MainFuturesEntry> entries) =>
        _namesByInstrument = entries.ToDictionary(e => e.Code, e => e.Name, StringComparer.OrdinalIgnoreCase);

    // ── 数据流 ──

    /// <summary>
    /// 行情回调线程入口：只登记「该合约最新一笔」，**不碰 UI、不做逐笔累积**。
    /// 同合约的中间态直接覆盖——看板每秒只展示一次，被覆盖掉的中间价没有任何人看得到。
    /// </summary>
    private void EnqueueTick(CtpTick tick)
    {
        // 只喂看板上的合约：同一条 MD 连接上可能还挂着交易页/策略订阅的别的合约（IF2612 之类），
        // 它们进来会让「品种数」和速率对不上看板，直接忽略。
        if (!_boardInstruments.ContainsKey(tick.InstrumentId)) return;

        _pendingTicks[tick.InstrumentId] = tick;
        Interlocked.Increment(ref _ticksSincePulse);
        Interlocked.Exchange(ref _lastTickAtTicks, DateTime.UtcNow.Ticks);
    }

    /// <summary>UI 线程（每秒一拍）：把回调线程攒下的「每合约最新一笔」落到行上，并把速率样本记一条。</summary>
    private void DrainTicks()
    {
        var ticks = Interlocked.Exchange(ref _ticksSincePulse, 0);
        if (ticks > 0)
        {
            _rateSamples.Enqueue((DateTime.UtcNow, ticks));
            TickCount += ticks;
        }

        var at = Interlocked.Read(ref _lastTickAtTicks);
        if (at > DateTime.MinValue.Ticks) _lastTickAt = new DateTime(at, DateTimeKind.Utc);

        foreach (var instrument in _pendingTicks.Keys)
            if (_pendingTicks.TryRemove(instrument, out var tick))
                OnTick(tick);
    }

    private void OnTick(CtpTick tick)
    {
        if (!_rowsByInstrument.TryGetValue(tick.InstrumentId, out var row)) return;
        row.Apply(tick);
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

    /// <summary>每秒一拍：落 tick、算速率、判断静默、刷新口径行。</summary>
    private void UpdatePulse()
    {
        DrainTicks();

        var now = DateTime.UtcNow;
        while (_rateSamples.Count > 0 && (now - _rateSamples.Peek().At).TotalSeconds > RateWindowSeconds)
            _rateSamples.Dequeue();

        long ticksInWindow = 0;
        foreach (var sample in _rateSamples) ticksInWindow += sample.Count;

        TickRate = MarketMath.TickRate((int)Math.Min(int.MaxValue, ticksInWindow), RateWindowSeconds);
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

    /// <summary>
    /// 仿真自有品种的中文名兜底（名称列）：与 ④ InstrumentCatalog 同源同值——ax001 = Aether币价期货
    /// （服务端已是权威源，2026-10-26 起 queryInstruments/RPC 携带；CTP wire「查合约」应答帧的名称槽
    /// 未逆向——⑤ FEATURE_MATRIX 记 follow-up——wire 补齐前桌面端用内置兜底，爬取目录的真实品种名不受影响）。
    /// </summary>
    private static readonly Dictionary<string, string> SimInstrumentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ax001"] = "Aether币价期货",
    };

    /// <summary>把看板行对齐到品种清单：保留已有行的数据，删掉移除的，新合约追加到末尾。名称列取爬取目录的中文名，查不到退仿真品种兜底。</summary>
    private void SyncRows(IReadOnlyList<string> symbols)
    {
        var wanted = new HashSet<string>(symbols, StringComparer.OrdinalIgnoreCase);

        for (int i = Quotes.Count - 1; i >= 0; i--)
        {
            if (wanted.Contains(Quotes[i].Instrument)) continue;
            _rowsByInstrument.Remove(Quotes[i].Instrument);
            _boardInstruments.TryRemove(Quotes[i].Instrument, out _);
            Quotes.RemoveAt(i);
        }

        foreach (var symbol in symbols)
        {
            _boardInstruments[symbol] = 0;   // 回调线程按它过滤，必须在行建好之前就登记
            if (_rowsByInstrument.ContainsKey(symbol)) continue;
            var name = _namesByInstrument.GetValueOrDefault(symbol)
                ?? SimInstrumentNames.GetValueOrDefault(symbol)
                ?? "";
            var row = new QuoteRow { Instrument = symbol, Name = name };
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

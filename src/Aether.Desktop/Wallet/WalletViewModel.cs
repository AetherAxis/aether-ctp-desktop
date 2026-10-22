using System;
using System.IO;
using System.Threading.Tasks;
using Aether.Ledger;
using Aether.Workbench;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ctp.Net;

namespace Aether.Desktop.Wallet;

/// <summary>
/// 钱包页 ViewModel（M2）——AQ 充提操作面。
/// 三层结构：连接设置（链节点/custody/交易所）→ 钱包管理（导入种子→派生地址→查余额）→ 操作（充值/提币）。
/// 充值 = 链上向 custody 转 AQ（memo = 交易所账户名），桥 1 秒对账入账场内现金（1 AQ = 1 现金）。
/// 提币 = 经交易所 JSON-RPC withdraw op（登录鉴权 + 静态可用现金闸门）。
/// 设置持久化到 IPreferencesStore（同旧桌面端 wallet.node / wallet.custody / wallet.exchange 键）。
/// </summary>
public partial class WalletViewModel : ObservableObject
{
    private readonly ICtpSession _session;
    private readonly IPreferencesStore _preferences;
    private LedgerWallet? _wallet;

    // ── 连接设置（持久化）──
    [ObservableProperty] private string _nodeAddress = "127.0.0.1:17805";
    [ObservableProperty] private string _custodyAddress = "";
    [ObservableProperty] private string _exchangeAddress = "127.0.0.1:17800";

    // ── 钱包 ──
    [ObservableProperty] private string _seedText = "";
    [ObservableProperty] private string _walletAddress = "—";
    [ObservableProperty] private string _chainAqBalance = "—";
    [ObservableProperty] private string _chainCoinBalance = "—";

    // ── 链状态 ──
    [ObservableProperty] private string _chainStatus = "未连接";
    [ObservableProperty] private string _chainHeight = "—";
    [ObservableProperty] private string _chainId = "—";

    // ── 场内现金（交易所，需交易页登录）──
    [ObservableProperty] private string _exchangeAccountId = "—";
    [ObservableProperty] private string _exchangeBalance = "—";
    [ObservableProperty] private string _exchangeAvailable = "—";

    // ── 充值操作 ──
    [ObservableProperty] private string _depositAccount = "";
    [ObservableProperty] private string _depositAmount = "1000";

    // ── 提币操作 ──
    [ObservableProperty] private string _withdrawUser = "";
    [ObservableProperty] private string _withdrawPassword = "";
    [ObservableProperty] private string _withdrawAmount = "100";

    [ObservableProperty] private string _statusText = "";

    public WalletViewModel(ICtpSession session, IPreferencesStore preferences)
    {
        _session = session;
        _preferences = preferences;
        _ = RestoreSettingsAsync();
    }

    // ── 设置持久化 ──

    private async Task RestoreSettingsAsync()
    {
        NodeAddress = await _preferences.GetAsync("wallet.node") ?? NodeAddress;
        CustodyAddress = await _preferences.GetAsync("wallet.custody") ?? "";
        ExchangeAddress = await _preferences.GetAsync("wallet.exchange") ?? ExchangeAddress;
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        await _preferences.SetAsync("wallet.node", NodeAddress.Trim());
        await _preferences.SetAsync("wallet.custody", CustodyAddress.Trim());
        await _preferences.SetAsync("wallet.exchange", ExchangeAddress.Trim());
        StatusText = "设置已保存";
    }

    // ── 钱包状态 ──
    [ObservableProperty] private bool _isCreating;
    [ObservableProperty] private bool _seedBackedUp;
    [ObservableProperty] private string _newSeedHex = "";

    // ── 交易所登录（钱包页独立的连接配置）──
    [ObservableProperty] private string _loginUser = "";
    [ObservableProperty] private string _loginPassword = "";
    [ObservableProperty] private string _loginBroker = "9999";
    [ObservableProperty] private string _loginFront = "tcp://127.0.0.1:17801";
    [ObservableProperty] private string _loginMdFront = "tcp://127.0.0.1:17802";
    [ObservableProperty] private string _loginStatus = "未登录";

    /// <summary>生成新钱包——显示种子（只此一次，确认备份后保存，不再展示）。</summary>
    [RelayCommand]
    private void GenerateNewWallet()
    {
        IsCreating = true;
        SeedBackedUp = false;
        NewSeedHex = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    }

    /// <summary>确认备份 → 保存种子为活跃钱包（保存后种子不再显示——只此一次）。</summary>
    [RelayCommand]
    private async Task ConfirmCreateAsync()
    {
        if (!SeedBackedUp || NewSeedHex is not { Length: 64 }) return;
        SeedText = NewSeedHex;
        await ImportWalletAsync();
        // 清除显示——只此一次
        NewSeedHex = "";
        IsCreating = false;
        SeedBackedUp = false;
        StatusText = "钱包已创建。⚠ 种子只此一次展示——离线备份是找回钱包的唯一途径。";
    }

    /// <summary>取消创建。</summary>
    [RelayCommand]
    private void CancelCreate()
    {
        IsCreating = false;
        NewSeedHex = "";
        SeedBackedUp = false;
    }

    /// <summary>钱包页独立登录交易所（不依赖交易页先登录）。</summary>
    [RelayCommand]
    private async Task LoginExchangeAsync()
    {
        if (string.IsNullOrWhiteSpace(LoginUser) || string.IsNullOrWhiteSpace(LoginPassword))
        {
            LoginStatus = "请填写账户和密码";
            return;
        }
        LoginStatus = "登录中…";
        try
        {
            if (_session.IsConnected) { _session.Logout(); }
            var login = await _session.LoginAsync(new CtpLoginRequest
            {
                FrontAddress = LoginFront.Trim(),
                MdFrontAddress = LoginMdFront.Trim(),
                BrokerId = LoginBroker.Trim(),
                UserId = LoginUser.Trim(),
                Password = LoginPassword,
                AppId = "simnow_client_test",
                AuthCode = "0000000000000000",
            });
            LoginStatus = $"已登录：交易日 {login.TradingDay}";
            await RefreshAccountAsync();
        }
        catch (Exception ex)
        {
            LoginStatus = $"登录失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportWalletAsync()
    {
        var seed = SeedText.Trim();
        if (seed.Length != 64)
        {
            StatusText = "种子须为 64 位 hex（32 字节）";
            return;
        }
        try
        {
            _wallet = new LedgerWallet(Convert.FromHexString(seed));
            WalletAddress = _wallet.Address.Hex;
            await _preferences.SetAsync("wallet.seed", seed);
            StatusText = $"钱包已导入：{_wallet.Address.Hex[..12]}…";
            await RefreshChainBalanceAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"导入失败：{ex.Message}";
        }
    }

    // ── 链查询 ──

    [RelayCommand]
    public async Task RefreshChainAsync()
    {
        try
        {
            var (host, port) = ParseEndpoint(NodeAddress);
            var client = new LedgerClient(host, port);
            var info = await client.GetInfoAsync();
            ChainStatus = "已连接";
            ChainHeight = info.Height.ToString();
            ChainId = info.ChainId.ToString();
            if (_wallet is not null) await RefreshChainBalanceAsync();
        }
        catch (Exception ex)
        {
            ChainStatus = "未连接";
            ChainHeight = ChainId = "—";
            StatusText = $"链节点不可达：{ex.Message}";
        }
    }

    private async Task RefreshChainBalanceAsync()
    {
        if (_wallet is null) return;
        try
        {
            var (host, port) = ParseEndpoint(NodeAddress);
            var client = new LedgerClient(host, port);
            var balance = await client.GetBalanceAsync(_wallet.Address.Hex);
            ChainAqBalance = LedgerSupply.Format(balance.QuoteBalanceAtoms);
            ChainCoinBalance = LedgerSupply.Format(balance.BalanceAtoms);
        }
        catch { ChainAqBalance = ChainCoinBalance = "—"; }
    }

    // ── 场内现金查询（需交易页登录）──

    [RelayCommand]
    public async Task RefreshAccountAsync()
    {
        if (!_session.IsConnected) { StatusText = "请先在「交易」页登录"; return; }
        try
        {
            var account = await _session.QueryAccountAsync();
            ExchangeAccountId = account.AccountId;
            ExchangeBalance = account.Balance.ToString("N2");
            ExchangeAvailable = account.Available.ToString("N2");
        }
        catch (Exception ex) { StatusText = $"账户查询失败：{ex.Message}"; }
    }

    // ── 充值（AQ → 场内）──

    [RelayCommand]
    public async Task DepositAsync()
    {
        if (_wallet is null) { StatusText = "请先导入钱包种子"; return; }
        if (string.IsNullOrWhiteSpace(CustodyAddress)) { StatusText = "请先设置 custody 地址"; return; }
        if (string.IsNullOrWhiteSpace(DepositAccount)) { StatusText = "请填写交易所账户名（memo 归账键）"; return; }
        if (!decimal.TryParse(DepositAmount, out var amount) || amount <= 0) { StatusText = "数量无效"; return; }
        try
        {
            var (host, port) = ParseEndpoint(NodeAddress);
            var client = new LedgerClient(host, port);
            var atoms = (ulong)Math.Round(amount * (decimal)LedgerSupply.AtomsPerCoin);
            await LedgerBridge.DepositAsync(client, _wallet, LedgerAddress.Parse(CustodyAddress.Trim()),
                atoms, DepositAccount.Trim());
            StatusText = $"已提交充值 {amount} AQ → custody（memo={DepositAccount}），桥 1 秒内对账入账场内现金";
        }
        catch (Exception ex) { StatusText = $"充值失败：{ex.Message}"; }
    }

    // ── 提币（场内 → AQ）──

    [RelayCommand]
    public async Task WithdrawAsync()
    {
        if (string.IsNullOrWhiteSpace(WithdrawUser) || string.IsNullOrWhiteSpace(WithdrawPassword))
        { StatusText = "请填写交易所账户和密码"; return; }
        if (!decimal.TryParse(WithdrawAmount, out var amount) || amount <= 0) { StatusText = "数量无效"; return; }
        try
        {
            var atoms = (ulong)Math.Round(amount * (decimal)LedgerSupply.AtomsPerCoin);
            var (host, port) = ParseEndpoint(ExchangeAddress);
            using var rpc = new Aether.Exchange.Rpc.SimRpcClient(host, port);
            await rpc.ConnectAsync();
            await rpc.LoginAsync(new Aether.Exchange.Rpc.LoginArg("9999", WithdrawUser.Trim(), WithdrawPassword));
            var toAddress = _wallet?.Address.Hex ?? "";
            var txHash = await rpc.WithdrawAsync(toAddress, atoms);
            StatusText = $"提币已提交：{amount} AQ → {toAddress[..Math.Min(12, toAddress.Length)]}…（tx {txHash[..Math.Min(12, txHash.Length)]}…）";
        }
        catch (Exception ex) { StatusText = $"提币失败：{ex.Message}"; }
    }

    // ── 工具 ──

    private static (string Host, int Port) ParseEndpoint(string addr)
    {
        var colon = addr.LastIndexOf(':');
        if (colon > 0 && int.TryParse(addr[(colon + 1)..], out var port))
            return (addr[..colon], port);
        return (addr, 17805);
    }
}

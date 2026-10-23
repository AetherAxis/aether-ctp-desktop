using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aether.Workbench;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ctp.Net;

namespace Aether.Desktop.Connector;

/// <summary>
/// 连接器 tab —— CTP 登录/登出/预设管理的独立页面。
/// 与交易/策略/钱包 tab 隔离：后者被动消费 <see cref="ICtpSession"/>（单例），登录/订阅/重连全在本页完成。
/// </summary>
public partial class ConnectorViewModel : ObservableObject
{
    private readonly ICtpSession _session;
    private readonly IPreferencesStore _preferences;

    private const string PrefFront = "connector.front";
    private const string PrefMdFront = "connector.mdfront";
    private const string PrefUser = "connector.user";
    private const string PrefPass = "connector.pass";
    private const string PrefBroker = "connector.broker";
    private const string PrefAppId = "connector.appid";
    private const string PrefAuth = "connector.auth";
    private const string PrefAutoLogin = "connector.autologin";
    private const string PrefPreset = "connector.preset";
    private const string PrefPresets = "connector.presets";   // 预设清单档案（JSON 数组；见 ConnectionPresets）

    public ConnectorViewModel(ICtpSession session, IPreferencesStore preferences)
    {
        _session = session;
        _preferences = preferences;

        // 预设清单：用户档案优先（无档/坏档回退内置，保证非空）
        Presets = ConnectionPresets.Load(_preferences.GetAsync(PrefPresets).GetAwaiter().GetResult());

        // 启动同步恢复上次登录档案
        Front = _preferences.GetAsync(PrefFront).GetAwaiter().GetResult() ?? Presets[0].Front;
        MdFront = _preferences.GetAsync(PrefMdFront).GetAwaiter().GetResult() ?? Presets[0].MdFront;
        Broker = _preferences.GetAsync(PrefBroker).GetAwaiter().GetResult() ?? "9999";
        UserId = _preferences.GetAsync(PrefUser).GetAwaiter().GetResult() ?? "";
        // 密码**不落盘**（2026-10-24）：旧版把密码明文写进 preferences.json（同机任何进程可读）。
        // 现在启动不恢复密码，并顺手清掉历史版本留下的明文键。要恢复「记住密码/自动登录」，
        // 得接系统钥匙串（macOS Keychain / Windows DPAPI / libsecret）——偏好文件只存引用。
        if (_preferences.GetAsync(PrefPass).GetAwaiter().GetResult() is not null)
            _preferences.SetAsync(PrefPass, null).GetAwaiter().GetResult();
        Password = "";
        AppId = _preferences.GetAsync(PrefAppId).GetAwaiter().GetResult() ?? "simnow_client_test";
        AuthCode = _preferences.GetAsync(PrefAuth).GetAwaiter().GetResult() ?? "0000000000000000";
        AutoLogin = bool.TryParse(_preferences.GetAsync(PrefAutoLogin).GetAwaiter().GetResult(), out var autoLogin) && autoLogin;

        // 恢复上次选中的预设（如果表单值匹配某个预设则高亮）
        var lastPresetName = _preferences.GetAsync(PrefPreset).GetAwaiter().GetResult();
        if (lastPresetName != null)
        {
            _selectedPreset = Presets.FirstOrDefault(p => p.Name == lastPresetName);
            if (_selectedPreset != null) MarkCurrentPreset(_selectedPreset);
        }

        // 连接状态变化 → 被动同步状态文本
        _session.ConnectionStateChanged += (_, s) =>
        {
            IsConnected = s == CtpConnectionStatus.Connected;
            StatusText = s switch
            {
                CtpConnectionStatus.Connected => "已连接",
                CtpConnectionStatus.Reconnecting => "重连中…",
                CtpConnectionStatus.Closed => "闭市",
                _ => "未连接",
            };
        };
        IsConnected = _session.IsConnected;
        StatusText = IsConnected ? "已连接" : "未连接";

        // 自动登录：有档案 + 未连接 + AutoLogin 开启 + 有密码。
        // 密码不再持久化 ⇒ 冷启动时密码必为空，自动登录无从谈起——明确告知，而不是拿空密码去撞柜台。
        if (AutoLogin && !IsConnected && !string.IsNullOrWhiteSpace(Front) && !string.IsNullOrWhiteSpace(UserId))
        {
            if (string.IsNullOrEmpty(Password))
                StatusText = "自动登录已停用：密码不再写入本地偏好（安全），请输入密码后点「连接」";
            else
                _ = ConnectAsync();
        }
    }

    // ── 预设（固定三个 + 字典驱动 Instruments）──

    /// <summary>预设清单（界面绑定）：名字 → { 交易前置, 行情前置, 默认订阅品种 }。
    /// <b>用户档案优先，无档/坏档回退内置</b>——见 <see cref="ConnectionPresets"/>；内置只作首次启动与兜底
    /// （原来硬编码在代码里，换部署环境要改代码重发）。</summary>
    public IReadOnlyList<ConnectionPreset> Presets { get; }

    /// <summary>内置兜底清单（兼容旧引用点）。</summary>
    public static IReadOnlyList<ConnectionPreset> DefaultPresets => ConnectionPresets.BuiltIn;

    [ObservableProperty] private ConnectionPreset? _selectedPreset;

    /// <summary>点预设 chip → 填入地址 + 自动订阅对应品种（MD 腿不需要登录）。</summary>
    [RelayCommand]
    private async Task ApplyPresetAsync(ConnectionPreset? preset)
    {
        if (preset == null || IsConnecting) return;

        Front = preset.Front;
        MdFront = preset.MdFront;
        Instruments = string.Join(", ", preset.Instruments);
        MarkCurrentPreset(preset);
        await _preferences.SetAsync(PrefPreset, preset.Name);

        // MD 腿独立于交易登录 —— 预设切换立即重新订阅行情
        if (preset.Instruments.Count > 0)
        {
            await SubscribeMdOnlyAsync(preset.MdFront, preset.Instruments);
        }
    }

    private void MarkCurrentPreset(ConnectionPreset preset)
    {
        SelectedPreset = preset;
    }

    // ── 表单字段 ──

    [ObservableProperty] private string _front = "";
    [ObservableProperty] private string _mdFront = "";
    [ObservableProperty] private string _broker = "";
    [ObservableProperty] private string _userId = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _appId = "";
    [ObservableProperty] private string _authCode = "";
    /// <summary>订阅品种（逗号分隔）。预设切换自动填；SimNow 等真实柜台需手动填主连代码。</summary>
    [ObservableProperty] private string _instruments = "";
    [ObservableProperty] private bool _autoLogin;

    // ── 状态 ──

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private string _statusText = "未连接";

    // ── 连接命令 ──

    /// <summary>登录/登出切换。</summary>
    [RelayCommand]
    private Task ToggleConnectionAsync() => IsConnected ? LogoutAsync() : ConnectAsync();

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (IsConnecting) return;
        if (string.IsNullOrWhiteSpace(Front) || string.IsNullOrWhiteSpace(UserId))
        {
            StatusText = "请填写交易前置地址与用户号";
            return;
        }

        IsConnecting = true;
        StatusText = "连接中…";
        try
        {
            // 持久化登录档案
            await SaveFormPrefsAsync();

            var result = await _session.LoginAsync(new CtpLoginRequest
            {
                FrontAddress = Front.Trim(),
                MdFrontAddress = MdFront.Trim(),
                BrokerId = Broker.Trim(),
                UserId = UserId.Trim(),
                Password = Password,
                AppId = AppId.Trim(),
                AuthCode = AuthCode.Trim(),
            });

            // 登录后立即订阅行情（用表单里的品种列表，空则跳过）
            var symbols = ParseInstruments();
            if (symbols.Length > 0)
            {
                await _session.SubscribeMarketDataAsync(new CtpMarketDataConnect
                {
                    MdFrontAddress = MdFront.Trim(),
                    BrokerId = Broker.Trim(),
                    UserId = UserId.Trim(),
                    Password = Password ?? "",
                }, symbols);
            }

            StatusText = $"已连接（交易日 {result.TradingDay}）";
        }
        catch (Exception ex)
        {
            StatusText = $"连接失败：{ex.Message}";
        }
        finally
        {
            IsConnecting = false;
            IsConnected = _session.IsConnected;
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        try
        {
            _session.Logout();
            StatusText = "已断开";
        }
        catch (Exception ex)
        {
            StatusText = $"断开异常：{ex.Message}";
        }
        IsConnected = _session.IsConnected;
    }

    /// <summary>仅订阅行情（MD 腿独立于交易登录，预设切换时调用）。</summary>
    private async Task SubscribeMdOnlyAsync(string mdFront, IReadOnlyList<string> symbols)
    {
        try
        {
            StatusText = $"行情订阅中：{string.Join(",", symbols)}";
            await _session.SubscribeMarketDataAsync(new CtpMarketDataConnect
            {
                MdFrontAddress = mdFront.Trim(),
                BrokerId = Broker.Trim(),
                UserId = string.IsNullOrWhiteSpace(UserId) ? "guest" : UserId.Trim(),
                Password = Password ?? "",
            }, symbols);
            StatusText = $"已订阅 {string.Join(",", symbols)}";
        }
        catch (Exception ex)
        {
            StatusText = $"行情订阅失败：{ex.Message}";
        }
    }

    private string[] ParseInstruments() => Instruments
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private async Task SaveFormPrefsAsync()
    {
        await _preferences.SetAsync(PrefFront, Front.Trim());
        await _preferences.SetAsync(PrefMdFront, MdFront.Trim());
        await _preferences.SetAsync(PrefBroker, Broker.Trim());
        await _preferences.SetAsync(PrefUser, UserId.Trim());
        await _preferences.SetAsync(PrefPass, null);   // 密码绝不落盘（见 ctor 注释：恢复记住密码须走系统钥匙串）
        await _preferences.SetAsync(PrefAppId, AppId.Trim());
        await _preferences.SetAsync(PrefAuth, AuthCode.Trim());
        await _preferences.SetAsync(PrefAutoLogin, AutoLogin ? "true" : "false");
    }
}

/// <summary>连接预设（快速切换柜台）。</summary>
public record ConnectionPreset(string Name, string Front, string MdFront, IReadOnlyList<string> Instruments);

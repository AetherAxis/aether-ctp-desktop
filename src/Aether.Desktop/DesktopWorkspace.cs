using Aether.Workbench.Abstractions;

namespace Aether.Desktop;

/// <summary>App 的 panel key 目录（App 自备——⑩ 引擎不携带任何业务键）。</summary>
public static class PanelKeys
{
    public const string Market = "market";
    public const string Trading = "trading";
    public const string Wallet = "wallet";
}

/// <summary>
/// App 的默认工作台：行情 / 交易 / 钱包 三 tab（2026-10-25 布局收敛，原五 tab）。
/// 行情 = 只连行情腿的看板（MD 唯一持有者——冷启动即订阅、真实前置自动爬主力，不需要交易账号）；
/// 交易 = 登录栏（原「连接器」tab 收编为顶栏）+ 账户/持仓/委托（行情板已移除——看行情归行情页）；
/// 钱包 = AQ 充提。原「策略」tab 为假按钮桩，撤掉——接 ⑥ 运行时时再以真功能回归。
/// </summary>
public static class DesktopWorkspace
{
    public static WorkspaceDefinition Definition { get; } = new(
        Id: "main",
        Title: "Aether Desktop",
        Panels: new List<PanelDescriptor>
        {
            new(PanelKeys.Market, PanelKeys.Market, "行情", "futures"),
            new(PanelKeys.Trading, PanelKeys.Trading, "交易", "activity"),
            new(PanelKeys.Wallet, PanelKeys.Wallet, "钱包", "wallet"),
        },
        Layout: new LayoutTabbed(new[] { PanelKeys.Market, PanelKeys.Trading, PanelKeys.Wallet }));
}

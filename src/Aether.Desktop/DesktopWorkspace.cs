using Aether.Workbench.Abstractions;

namespace Aether.Desktop;

/// <summary>App 的 panel key 目录（App 自备——⑩ 引擎不携带任何业务键）。</summary>
public static class PanelKeys
{
    public const string Trading = "trading";
    public const string Strategy = "strategy";
    public const string Wallet = "wallet";
}

/// <summary>App 的默认工作台：交易 / 策略 / 钱包三 tab。
/// 交易 = 行情 + 持仓 + 委托（已实现 M1）；策略 = 回测 + 实盘（M3）；钱包 = AQ 充提（M2）。</summary>
public static class DesktopWorkspace
{
    public static WorkspaceDefinition Definition { get; } = new(
        Id: "main",
        Title: "Aether Desktop",
        Panels: new List<PanelDescriptor>
        {
            new(PanelKeys.Trading, PanelKeys.Trading, "交易", "trading"),
            new(PanelKeys.Strategy, PanelKeys.Strategy, "策略", "strategy"),
            new(PanelKeys.Wallet, PanelKeys.Wallet, "钱包", "wallet"),
        },
        Layout: new LayoutTabbed(new[] { PanelKeys.Trading, PanelKeys.Strategy, PanelKeys.Wallet }));
}

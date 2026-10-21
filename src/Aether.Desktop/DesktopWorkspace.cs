using Aether.Workbench.Abstractions;

namespace Aether.Desktop;

/// <summary>App 的 panel key 目录（App 自备——⑩ 引擎不携带任何业务键；三处必须同串：
/// workspace 定义 / view registry / 贡献者注册）。</summary>
public static class PanelKeys
{
    public const string Futures = "futures";
    public const string Backtest = "backtest";
    public const string Wallet = "wallet";
}

/// <summary>App 的默认工作台：期货 / 回测 / 钱包三 tab（nav rail 依此派生；图标键命中 ⑩ IconLoader 的 codicon 目录）。</summary>
public static class DesktopWorkspace
{
    public static WorkspaceDefinition Definition { get; } = new(
        Id: "main",
        Title: "Aether Desktop",
        Panels: new List<PanelDescriptor>
        {
            new(PanelKeys.Futures, PanelKeys.Futures, "期货", "futures"),
            new(PanelKeys.Backtest, PanelKeys.Backtest, "回测", "backtest"),
            new(PanelKeys.Wallet, PanelKeys.Wallet, "钱包", "wallet"),
        },
        Layout: new LayoutTabbed(new[] { PanelKeys.Futures, PanelKeys.Backtest, PanelKeys.Wallet }));
}

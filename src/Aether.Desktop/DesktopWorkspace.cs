using Aether.Workbench.Abstractions;

namespace Aether.Desktop;

/// <summary>App 的 panel key 目录（App 自备——⑩ 引擎不携带任何业务键）。</summary>
public static class PanelKeys
{
    public const string Market = "market";
}

/// <summary>
/// App 的默认工作台：**单 tab 行情**（2026-10-26 收敛——用户「交易和钱包先去掉，后面按需求补」）。
/// 桌面端当前就是一台纯行情终端：只连 MD 腿的看板 + 主力合约直爬，无交易登录、无钱包。
/// 交易（登录栏+账户/持仓/委托）与钱包（AQ 充提）按 git 历史随时可回归——回归时把
/// ProjectReference（① Ledger / ④ Exchange.Rpc）与 CI checkout 一并加回。
/// </summary>
public static class DesktopWorkspace
{
    public static WorkspaceDefinition Definition { get; } = new(
        Id: "main",
        Title: "Aether Desktop",
        Panels: new List<PanelDescriptor>
        {
            new(PanelKeys.Market, PanelKeys.Market, "行情", "futures"),
        },
        Layout: new LayoutTabbed(new[] { PanelKeys.Market }));
}

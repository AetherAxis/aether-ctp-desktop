using Aether.Desktop.Backtest;
using Aether.Desktop.Futures;
using Aether.Desktop.Wallet;
using Aether.Workbench.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Aether.Desktop;

/// <summary>App 视图贡献者：把三个 panel 的视图/VM 工厂注册进 ⑩ 引擎的 IViewRegistry（经 AddWorkbench 的贡献者模式）。</summary>
public sealed class AppViewContributor : IViewRegistryContributor
{
    public void Contribute(IViewRegistry registry, IServiceProvider services)
    {
        registry.Register(PanelKeys.Futures,
            () => new FuturesView(),
            () => services.GetRequiredService<FuturesViewModel>());   // 单例 VM——panel 常驻不重建

        registry.Register(PanelKeys.Backtest, () => new BacktestView());
        registry.Register(PanelKeys.Wallet, () => new WalletView());
    }
}

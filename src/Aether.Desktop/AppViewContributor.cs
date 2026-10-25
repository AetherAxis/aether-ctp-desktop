using Aether.Desktop.Futures;
using Aether.Desktop.MarketWatch;
using Aether.Desktop.Wallet;
using Aether.Workbench.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Aether.Desktop;

/// <summary>App 视图贡献者：把三个 panel（行情/交易/钱包）的视图/VM 工厂注册进 ⑩ 引擎的 IViewRegistry。</summary>
public sealed class AppViewContributor : IViewRegistryContributor
{
    public void Contribute(IViewRegistry registry, IServiceProvider services)
    {
        registry.Register(PanelKeys.Market,
            () => new MarketWatchView(),
            () => services.GetRequiredService<MarketWatchViewModel>());

        registry.Register(PanelKeys.Trading,
            () => new FuturesView(),
            () => services.GetRequiredService<FuturesViewModel>());

        registry.Register(PanelKeys.Wallet,
            () => new WalletView(),
            () => services.GetRequiredService<WalletViewModel>());
    }
}

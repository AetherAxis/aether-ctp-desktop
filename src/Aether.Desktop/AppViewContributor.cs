using Aether.Desktop.MarketWatch;
using Aether.Workbench.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Aether.Desktop;

/// <summary>App 视图贡献者：把行情 panel 的视图/VM 工厂注册进 ⑩ 引擎的 IViewRegistry。</summary>
public sealed class AppViewContributor : IViewRegistryContributor
{
    public void Contribute(IViewRegistry registry, IServiceProvider services)
    {
        registry.Register(PanelKeys.Market,
            () => new MarketWatchView(),
            () => services.GetRequiredService<MarketWatchViewModel>());
    }
}

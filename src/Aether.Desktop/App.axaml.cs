using System.IO;
using Aether.Desktop.Futures;
using Aether.Desktop.Strategy;
using Aether.Desktop.Wallet;
using Aether.Workbench;
using Aether.Workbench.Abstractions;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Ctp.Net.Client.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Aether.Desktop;

/// <summary>
/// ⑪ 组合根：⑩ 布局引擎 + ⑦ CTP 客户端组件 → 轻量桌面 App（期货/回测/钱包）。
/// 依赖闭包：⑩ aether-workbench + ⑦ ctp-net-client（③②传递）——与 console 同款 ICtpSession 组件，
/// 桌面端本就是这些组件的原始宿主（console 即按"复用桌面端同款组件"建的）。
/// </summary>
public partial class App : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        // 持久化：~/.aether/aether-desktop/preferences.json（布局/主题/登录档案）
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether", "aether-desktop");
        Directory.CreateDirectory(dataDir);

        var services = new ServiceCollection();
        services.AddSingleton<IPreferencesStore>(new JsonPreferencesStore(Path.Combine(dataDir, "preferences.json")));
        services.AddCtpInfrastructure(sqliteDbPath: Path.Combine(dataDir, "ctp.db"));   // ⑦ 同款组件（多档案凭据仓）
        services.AddWorkbench(DesktopWorkspace.Definition);                            // ⑩ 引擎 + 三 tab 布局
        services.AddSingleton<IViewRegistryContributor, AppViewContributor>();
        services.AddSingleton<FuturesViewModel>();
        services.AddSingleton<StrategyViewModel>();
        services.AddSingleton<WalletViewModel>();
        var provider = services.BuildServiceProvider();
        WorkbenchServices.Attach(provider);   // ⑩ view 层 code-behind 经此解析服务

        var shell = provider.GetRequiredService<WorkbenchShellViewModel>();
        shell.RestoreThemeSync();             // 窗口 Show 前同步恢复主题（免首帧 flash）
        var window = new WorkbenchWindow { DataContext = shell };
        window.Show();
        _ = shell.InitializeAsync();          // 恢复布局 + 激活工作台（不阻塞首帧）

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = window;

        base.OnFrameworkInitializationCompleted();
    }
}

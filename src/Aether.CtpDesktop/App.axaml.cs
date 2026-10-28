using System.IO;
using Aether.CtpDesktop.MarketWatch;
using Aether.Workbench;
using Aether.Workbench.Abstractions;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Ctp.Net.Client.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Aether.CtpDesktop;

/// <summary>
/// ⑪ 组合根：⑩ 布局引擎 + ⑦ CTP 客户端组件 → **纯行情终端**（单 tab，2026-10-26 收敛）。
/// 依赖闭包：⑩ aether-workbench + ⑦ ctp-net-client（③②传递）——与 console 同款 ICtpSession 组件，
/// 桌面端本就是这些组件的原始宿主（console 即按"复用桌面端同款组件"建的）。
/// <para>交易/钱包页已撤（用户「后面再按需求补」）——① Ledger / ④ Exchange.Rpc 的
/// ProjectReference 与 CI checkout 一并移除，回归时再加回（见 DesktopWorkspace 注释）。</para>
/// </summary>
public partial class App : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        // 持久化：~/.aether/aether-desktop/preferences.json（布局/主题/行情页订阅档案）
        // ⚠ 目录名是**故意保留的历史字面量**（2026-10-28 仓名改为 aether-ctp-desktop 时未跟随）：
        //   里面是用户的 ctp.db（连接档案含密码，0600）与布局偏好——改目录 = 让已有档案变孤儿，
        //   收益为零。它不是产品名，是用户数据的位置。
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether", "aether-desktop");
        Directory.CreateDirectory(dataDir);

        var services = new ServiceCollection();
        services.AddSingleton<IPreferencesStore>(new JsonPreferencesStore(Path.Combine(dataDir, "preferences.json")));
        services.AddCtpInfrastructure(sqliteDbPath: Path.Combine(dataDir, "ctp.db"));   // ⑦ 同款组件（凭据仓——行情页暂不用，会话注册需要）
        services.AddSingleton<MainFuturesCatalog>();   // 主力合约目录——客户端直爬东方财富（真实前置的订阅面）
        services.AddWorkbench(DesktopWorkspace.Definition);                            // ⑩ 引擎 + 单 tab 布局
        services.AddSingleton<IViewRegistryContributor, AppViewContributor>();
        services.AddSingleton<MarketWatchViewModel>();
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

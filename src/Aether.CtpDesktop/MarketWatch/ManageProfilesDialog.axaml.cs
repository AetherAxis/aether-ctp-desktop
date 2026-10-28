using Avalonia.Controls;

namespace Aether.CtpDesktop.MarketWatch;

/// <summary>「管理连接档案」对话框——内容即 <see cref="ManageProfilesViewModel"/>；关窗经 VM 的 CloseRequested 事件（VM 不持窗口引用）。</summary>
public partial class ManageProfilesDialog : Window
{
    public ManageProfilesDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WireClose();
    }

    private void WireClose()
    {
        if (DataContext is ManageProfilesViewModel vm)
            vm.CloseRequested += Close;
    }
}

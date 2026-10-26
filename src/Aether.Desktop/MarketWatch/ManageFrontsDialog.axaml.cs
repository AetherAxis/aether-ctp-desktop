using Avalonia.Controls;

namespace Aether.Desktop.MarketWatch;

/// <summary>「管理行情前置」对话框——内容即 <see cref="ManageFrontsViewModel"/>；关窗经 VM 的 CloseRequested 事件（VM 不持窗口引用）。</summary>
public partial class ManageFrontsDialog : Window
{
    public ManageFrontsDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WireClose();
    }

    private void WireClose()
    {
        if (DataContext is ManageFrontsViewModel vm)
            vm.CloseRequested += Close;
    }
}

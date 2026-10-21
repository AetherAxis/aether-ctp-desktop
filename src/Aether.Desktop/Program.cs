using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace Aether.Desktop;

/// <summary>程序入口。</summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}

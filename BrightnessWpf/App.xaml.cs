using System.Windows;
using BrightnessWpf.Models;
using BrightnessWpf.Services;

namespace BrightnessWpf;

/// <summary>
/// 应用入口：单实例互斥锁 + 应用主题 + 手动创建主窗口。
/// </summary>
public partial class App : System.Windows.Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // WinForms 托盘图标右键菜单需要启用视觉样式才能正常渲染
        System.Windows.Forms.Application.EnableVisualStyles();

        // 单实例保护，防止多开（多实例会抢占全局热键）
        _mutex = new Mutex(true, @"Global\HMSimpleBrightness_Wpf", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        // 应用主题（浅色/深色/跟随系统）
        var config = AppConfig.Load();
        ThemeManager.Apply(config.ThemeMode);

        var win = new MainWindow();
        MainWindow = win;
        win.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}

using System.Windows;

namespace BrightnessWpf;

/// <summary>
/// 应用入口。
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // WinForms 托盘图标右键菜单需要启用视觉样式才能正常渲染
        System.Windows.Forms.Application.EnableVisualStyles();
    }
}

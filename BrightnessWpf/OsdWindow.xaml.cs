using System.Windows;
using System.Windows.Threading;
using BrightnessWpf.Models;

namespace BrightnessWpf;

/// <summary>
/// OSD 亮度弹层。迁移自原版 NativeOsdForm：无边框置顶、显示在鼠标所在屏幕底部中央、约 2 秒后自动隐藏。
/// </summary>
public partial class OsdWindow : Window
{
    private readonly DispatcherTimer _hideTimer;

    public OsdWindow()
    {
        InitializeComponent();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _hideTimer.Tick += (s, e) => { _hideTimer.Stop(); Hide(); };
    }

    public void ShowOsd(IEnumerable<OsdItem> items)
    {
        OsdList.ItemsSource = items;
        if (!IsVisible) Show();
        UpdateLayout();

        // 定位到鼠标所在屏幕的底部中央（同 Windows 原生 OSD）
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Control.MousePosition);
        var wa = screen.WorkingArea;
        Left = wa.Left + (wa.Width - ActualWidth) / 2;
        Top = wa.Bottom - ActualHeight - 100;

        _hideTimer.Stop();
        _hideTimer.Start();
    }
}

using System.Windows;
using System.Windows.Threading;

namespace BrightnessWpf;

/// <summary>
/// OSD 亮度弹层：Win11 原生音量条样式的紧凑药丸条，显示在鼠标所在屏幕底部中央，约 2 秒后自动隐藏。
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

    public void ShowOsd(int brightness)
    {
        TrackBar.Value = brightness;
        ValueText.Text = brightness.ToString();
        if (!IsVisible) Show();
        UpdateLayout();

        // 定位到鼠标所在屏幕的底部中央（同 Windows 原生 OSD）
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Control.MousePosition);
        var wa = screen.WorkingArea;

        // Screen.WorkingArea 是物理像素，WPF Left/Top 是 DIP，需按 DPI 缩放换算
        double scale = 1.0;
        var source = System.Windows.PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null && source.CompositionTarget.TransformToDevice.M11 > 0)
            scale = source.CompositionTarget.TransformToDevice.M11;

        double waLeftDips = wa.Left / scale;
        double waWidthDips = wa.Width / scale;
        double waBottomDips = wa.Bottom / scale;
        Left = waLeftDips + (waWidthDips - ActualWidth) / 2;
        Top = waBottomDips - ActualHeight - 90;

        _hideTimer.Stop();
        _hideTimer.Start();
    }
}

using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using BrightnessWpf.ViewModels;

namespace BrightnessWpf;

/// <summary>
/// 主窗口。迁移第 2 步：接入显示器检测与亮度控制。
/// 托盘用 WinForms NotifyIcon（H.NotifyIcon 在单文件发布下无法创建托盘图标）。
/// </summary>
public partial class MainWindow : Window
{
    private NotifyIcon? _trayIcon;
    private MainViewModel? _viewModel;
    private bool _isShuttingDown;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // 定位到屏幕右下角
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 10;
        Top = workArea.Bottom - Height - 10;

        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _ = _viewModel.InitializeAsync();

        SetupTrayIcon();
    }

    // 显示器列表加载后窗口会变高，保持窗口不超出屏幕（下边缘锁定在任务栏上方）
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wa = SystemParameters.WorkArea;
        if (Top + ActualHeight > wa.Bottom)
            Top = Math.Max(wa.Top, wa.Bottom - ActualHeight - 10);
        if (Left + ActualWidth > wa.Right)
            Left = Math.Max(wa.Left, wa.Right - ActualWidth - 10);
    }

    private void SetupTrayIcon()
    {
        Icon trayIcon;
        using (var stream = System.Reflection.Assembly.GetExecutingAssembly()
                   .GetManifestResourceStream("BrightnessWpf.Resources.Icon_Tray_Hybrid.ico"))
        {
            trayIcon = stream != null ? new Icon(stream) : SystemIcons.Application;
        }

        _trayIcon = new NotifyIcon
        {
            Icon = trayIcon,
            Text = "HM's Simple Brightness Tool",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (s, e) => ShowAndActivate());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("同步多屏亮度", null, (s, e) => _ = _viewModel?.SyncAllCommand.ExecuteAsync(null));
        menu.Items.Add("重新扫描", null, (s, e) => _ = _viewModel?.RefreshCommand.ExecuteAsync(null));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (s, e) => ShutdownApp());
        _trayIcon.ContextMenuStrip = menu;

        // 左键切换主窗口，中键同步亮度
        _trayIcon.MouseUp += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                if (IsVisible) Hide();
                else ShowAndActivate();
            }
            else if (e.Button == MouseButtons.Middle)
            {
                _ = _viewModel?.SyncAllCommand.ExecuteAsync(null);
            }
        };
    }

    // 滑块变化 → 立即同步 VM 并调节亮度
    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider && slider.Tag is MonitorViewModel vm)
        {
            // 用滑块当前值立即写入 VM，避免绑定 Delay 导致读到上一拍的值（否则调节总是延后一拍）
            vm.Brightness = (int)e.NewValue;
            _ = _viewModel?.SetBrightnessCommand.ExecuteAsync(vm);
        }
    }

    // 右上角菜单按钮 → 弹出菜单
    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void SyncAll_Click(object sender, RoutedEventArgs e) =>
        _ = _viewModel?.SyncAllCommand.ExecuteAsync(null);

    private void Refresh_Click(object sender, RoutedEventArgs e) =>
        _ = _viewModel?.RefreshCommand.ExecuteAsync(null);

    private void Exit_Click(object sender, RoutedEventArgs e) => ShutdownApp();

    private void ShutdownApp()
    {
        _isShuttingDown = true;
        _viewModel?.SaveSettings();
        _trayIcon?.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void ShowAndActivate()
    {
        Show();
        Activate();
        WindowState = WindowState.Normal;
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // 关闭按钮最小化到托盘，不退出
        if (_isShuttingDown) return;
        e.Cancel = true;
        Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel?.SaveSettings();
        _trayIcon?.Dispose();
        base.OnClosed(e);
    }
}

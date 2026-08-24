using System.Windows;
using System.Windows.Controls;
using BrightnessWpf.ViewModels;
using H.NotifyIcon;

namespace BrightnessWpf;

/// <summary>
/// 主窗口。迁移第 2 步：接入显示器检测与亮度控制。
/// </summary>
public partial class MainWindow : Window
{
    private TaskbarIcon? _trayIcon;
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

        SetupTrayIcon();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _ = _viewModel.InitializeAsync();
    }

    private void SetupTrayIcon()
    {
        // 从内嵌资源加载托盘图标（单文件发布下 Assembly.Location/ExtractAssociatedIcon 不可靠）
        System.Drawing.Icon trayIcon;
        using (var stream = System.Reflection.Assembly.GetExecutingAssembly()
                   .GetManifestResourceStream("BrightnessWpf.Resources.Icon_Tray_Hybrid.ico"))
        {
            trayIcon = stream != null ? new System.Drawing.Icon(stream) : System.Drawing.SystemIcons.Application;
        }

        _trayIcon = new TaskbarIcon
        {
            Icon = trayIcon,
            ToolTipText = "HM's Simple Brightness Tool",
            Visibility = Visibility.Visible
        };

        _trayIcon.TrayLeftMouseDown += (s, e) =>
        {
            if (IsVisible)
                Hide();
            else
                ShowAndActivate();
        };

        var contextMenu = new ContextMenu();
        contextMenu.Style = (Style)FindResource(typeof(ContextMenu));

        var showItem = new MenuItem { Header = "显示主窗口" };
        showItem.Click += (s, e) => ShowAndActivate();
        contextMenu.Items.Add(showItem);

        contextMenu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "退出" };
        exitItem.Click += (s, e) => ShutdownApp();
        contextMenu.Items.Add(exitItem);

        _trayIcon.ContextMenu = contextMenu;
    }

    // 滑块变化 → 调节亮度
    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider && slider.Tag is MonitorViewModel vm)
        {
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
        Application.Current.Shutdown();
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

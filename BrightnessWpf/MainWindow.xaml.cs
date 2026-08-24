using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;

namespace BrightnessWpf;

/// <summary>
/// 主窗口。迁移第 1 步：托盘 + 最小 UI 壳。
/// </summary>
public partial class MainWindow : Window
{
    private TaskbarIcon? _trayIcon;

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
    }

    private void SetupTrayIcon()
    {
        // 单文件发布下 Assembly.Location 为空，改用 ProcessPath 从运行中的 exe 自提取图标
        string exePath = Environment.ProcessPath ?? "BrightnessWpf.exe";
        _trayIcon = new TaskbarIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath),
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
        exitItem.Click += (s, e) =>
        {
            _trayIcon?.Dispose();
            Application.Current.Shutdown();
        };
        contextMenu.Items.Add(exitItem);

        _trayIcon.ContextMenu = contextMenu;
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
        e.Cancel = true;
        Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnClosed(e);
    }
}
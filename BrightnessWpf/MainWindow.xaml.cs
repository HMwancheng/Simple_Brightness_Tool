using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;
using BrightnessWpf.Models;
using BrightnessWpf.Services;
using BrightnessWpf.ViewModels;
using Microsoft.Win32;

namespace BrightnessWpf;

/// <summary>
/// 主窗口。迁移第 2 步：接入显示器检测与亮度控制。
/// 托盘用 WinForms NotifyIcon（H.NotifyIcon 在单文件发布下无法创建托盘图标）。
/// </summary>
public partial class MainWindow : Window
{
    // 窗口距屏幕右/下边缘的距离（基于 WorkArea，自动适配任务栏位置）
    private const double ScreenMargin = 5;

    private Forms.NotifyIcon? _trayIcon;
    private MainViewModel? _viewModel;
    private MouseHook? _mouseHook;
    private HotkeyService? _hotkeyService;
    private OsdWindow? _osdWindow;
    private bool _isShuttingDown;
    private bool _menuOpen;
    private bool _reloading;
    private IntPtr _trayIconHandle = IntPtr.Zero;
    private uint _trayIconId;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // 定位到屏幕右下角，右/下边缘距离一致
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - ScreenMargin;
        Top = workArea.Bottom - Height - ScreenMargin;

        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _ = _viewModel.InitializeAsync();

        SetupTrayIcon();
        SetupMouseWheelHook();
        SetupHotkeys();
        RegisterSystemEvents();
    }

    // 显示器列表加载后窗口会变高，保持窗口不超出屏幕（下边缘锁定在任务栏上方）
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wa = SystemParameters.WorkArea;
        if (Top + ActualHeight > wa.Bottom)
            Top = Math.Max(wa.Top, wa.Bottom - ActualHeight - ScreenMargin);
        if (Left + ActualWidth > wa.Right)
            Left = Math.Max(wa.Left, wa.Right - ActualWidth - ScreenMargin);
    }

    // 点击窗口外部时窗口自动隐藏（弹出面板式交互）
    private void Window_Deactivated(object sender, EventArgs e)
    {
        // 有子窗口（设置/曲线编辑器等）或菜单打开时不隐藏
        if (_menuOpen) return;
        foreach (Window w in OwnedWindows)
            if (w.IsVisible) return;

        if (IsCursorInsideWindow())
            Activate();
        else
            Hide();
    }

    private bool IsCursorInsideWindow()
    {
        try
        {
            var topLeft = PointToScreen(new System.Windows.Point(0, 0));
            var bottomRight = PointToScreen(new System.Windows.Point(ActualWidth, ActualHeight));
            var pos = Forms.Control.MousePosition;
            return pos.X >= topLeft.X && pos.X <= bottomRight.X &&
                   pos.Y >= topLeft.Y && pos.Y <= bottomRight.Y;
        }
        catch { return true; }
    }

    private void SetupTrayIcon()
    {
        Icon trayIcon;
        using (var stream = System.Reflection.Assembly.GetExecutingAssembly()
                   .GetManifestResourceStream("BrightnessWpf.Resources.Icon_Tray_Hybrid.ico"))
        {
            trayIcon = stream != null ? new Icon(stream) : SystemIcons.Application;
        }

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = trayIcon,
            Text = "HM's Simple Brightness Tool",
            Visible = true
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (s, e) => ShowAndActivate());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("同步多屏亮度", null, (s, e) => _ = _viewModel?.SyncAllCommand.ExecuteAsync(null));
        menu.Items.Add("重新扫描", null, (s, e) => _ = _viewModel?.RefreshCommand.ExecuteAsync(null));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (s, e) => ShutdownApp());
        _trayIcon.ContextMenuStrip = menu;

        // 左键切换主窗口，中键同步亮度
        _trayIcon.MouseUp += (s, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                if (IsVisible) Hide();
                else ShowAndActivate();
            }
            else if (e.Button == Forms.MouseButtons.Middle)
            {
                _ = _viewModel?.SyncAllCommand.ExecuteAsync(null);
            }
        };
    }

    // 托盘图标上的滚轮滚动 → 调整亮度
    private void SetupMouseWheelHook()
    {
        _mouseHook = new MouseHook();
        _mouseHook.MouseWheel += (s, delta) =>
        {
            if (IsMouseOverTrayIcon())
            {
                _viewModel?.AdjustAllByStep(delta);
                if (!IsVisible) ShowOsd();
            }
        };
        _mouseHook.Install();
    }

    // 全局热键：Ctrl+F5 提升亮度，Ctrl+F6 降低亮度
    private void SetupHotkeys()
    {
        if (_viewModel == null) return;
        _hotkeyService = new HotkeyService();
        _hotkeyService.HotkeyPressed += id =>
        {
            _viewModel.AdjustAllByStep(id == 1 ? 1 : -1);
            if (!IsVisible) ShowOsd();
        };
        _hotkeyService.Register(_viewModel.HotkeyIncrease, 1);
        _hotkeyService.Register(_viewModel.HotkeyDecrease, 2);
    }

    // 系统事件：唤醒/解锁/显示变更 → 重新扫描显示器（合并并发 + 延迟，避免卡顿）
    private void RegisterSystemEvents()
    {
        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
    }

    private void UnregisterSystemEvents()
    {
        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
        SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
    }

    private void SystemEvents_PowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            Dispatcher.InvokeAsync(() => ReloadMonitorsDelayed(2000));
    }

    private void SystemEvents_SessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect)
            Dispatcher.InvokeAsync(() => ReloadMonitorsDelayed(2000));
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() => ReloadMonitorsDelayed(0));
    }

    private async void ReloadMonitorsDelayed(int delayMs)
    {
        if (_reloading) return;
        _reloading = true;
        try
        {
            if (delayMs > 0) await Task.Delay(delayMs);
            await (_viewModel?.RefreshCommand.ExecuteAsync(null) ?? Task.CompletedTask);
        }
        finally
        {
            _reloading = false;
        }
    }

    // OSD 亮度弹层（热键/托盘滚轮调节时显示）
    private void ShowOsd()
    {
        var items = _viewModel?.Monitors
            .Where(vm => vm.IsAdjustable)
            .Select(vm => new OsdItem { Brightness = vm.Brightness })
            .ToList();
        if (items == null || items.Count == 0) return;
        _osdWindow ??= new OsdWindow();
        _osdWindow.ShowOsd(items);
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
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu != null)
        {
            var menu = btn.ContextMenu;
            menu.Closed += (s, args) => _menuOpen = false;
            _menuOpen = true;
            menu.PlacementTarget = btn;
            menu.IsOpen = true;
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
        _mouseHook?.Dispose();
        _hotkeyService?.Dispose();
        _osdWindow?.Close();
        _trayIcon?.Dispose();
        UnregisterSystemEvents();
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
        _mouseHook?.Dispose();
        _hotkeyService?.Dispose();
        _osdWindow?.Close();
        _trayIcon?.Dispose();
        UnregisterSystemEvents();
        base.OnClosed(e);
    }

    // ================== 托盘图标矩形检测（托盘滚轮用） ==================

    private bool IsMouseOverTrayIcon()
    {
        try
        {
            if (_trayIconHandle == IntPtr.Zero || _trayIconId == 0)
                CacheTrayIconInfo();
            if (_trayIconHandle == IntPtr.Zero || _trayIconId == 0) return false;

            var nid = new Native.NativeMethods.NOTIFYICONIDENTIFIER
            {
                cbSize = Marshal.SizeOf<Native.NativeMethods.NOTIFYICONIDENTIFIER>(),
                hWnd = _trayIconHandle,
                uID = _trayIconId,
                guidItem = Guid.Empty
            };
            int result = Native.NativeMethods.Shell_NotifyIconGetRect(ref nid, out Native.NativeMethods.Rect rect);
            if (result == 0)
            {
                var pt = Forms.Control.MousePosition;
                return pt.X >= rect.left && pt.X <= rect.right &&
                       pt.Y >= rect.top && pt.Y <= rect.bottom;
            }
        }
        catch { }
        return false;
    }

    // 通过反射获取 NotifyIcon 内部隐藏窗口句柄和 ID（用于 Shell_NotifyIconGetRect）
    private void CacheTrayIconInfo()
    {
        try
        {
            if (_trayIcon == null) return;
            var type = typeof(Forms.NotifyIcon);
            foreach (var field in type.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
            {
                if (_trayIconId == 0 && (field.FieldType == typeof(uint) || field.FieldType == typeof(int)))
                {
                    if (field.Name.ToLowerInvariant().Contains("id"))
                        _trayIconId = Convert.ToUInt32(field.GetValue(_trayIcon) ?? 0u);
                }
                else if (_trayIconHandle == IntPtr.Zero && field.FieldType.Name.Contains("Window"))
                {
                    var window = field.GetValue(_trayIcon);
                    if (window != null)
                    {
                        var handleProp = window.GetType().GetProperty("Handle", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (handleProp != null)
                        {
                            var h = handleProp.GetValue(window);
                            if (h != null) _trayIconHandle = (IntPtr)h;
                        }
                    }
                }
                else if (_trayIconHandle == IntPtr.Zero && field.FieldType == typeof(IntPtr))
                {
                    if (field.Name.ToLowerInvariant().Contains("hwnd") || field.Name.ToLowerInvariant().Contains("handle"))
                    {
                        var v = field.GetValue(_trayIcon);
                        if (v != null) _trayIconHandle = (IntPtr)v;
                    }
                }
            }
        }
        catch { }
    }
}

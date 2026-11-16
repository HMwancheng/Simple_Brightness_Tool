using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;

namespace SimpleBrightness
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Auto-detect system theme
            ThemeManager.AutoDetectTheme();

            using (Mutex mutex = new Mutex(false, "Global\\" + "HMSimpleBrightness_v40_Split_Fixed"))
            {
                if (!mutex.WaitOne(0, false)) return;
                ApplicationConfiguration.Initialize();
                Application.Run(new MyCustomApplicationContext());
            }
        }
    }

    public class MyCustomApplicationContext : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip contextMenu;
        private List<MonitorInfo> monitors = new List<MonitorInfo>();
        private AppConfig config;
        private MouseHook mouseHook;
        private NativeOsdForm? _osdForm; 
        private bool _isDebugMode = false;
        /// <summary>显示器重扫单次标记：合并唤醒时同时触发的多个系统事件，防止重复全量扫描（WMI/DDC 很耗时）</summary>
        private int _reloading = 0;
        private IntPtr _trayIconHandle = IntPtr.Zero;
        private uint _trayIconId = 0;
        private HotkeyMessageWindow? _hotkeyWindow;

        // 显示器列表可能被后台扫描线程重建，统一用此锁保护
        private readonly object _monitorsLock = new object();
        // 托盘图标矩形缓存：钩子回调里只读缓存，绝不在这里做 Shell IPC
        private readonly object _trayRectLock = new object();
        private NativeMethods.Rect _trayIconRect;
        private bool _hasTrayIconRect;
        private Thread? _trayRectThread;
        // UI 线程调度器（钩子/后台线程把重活投递回 UI 线程）
        private readonly Control _uiInvoker = new Control();
        // 滚轮增量合并，避免每个滚轮事件都触发一次重绘
        private int _pendingWheelDelta;
        private int _wheelFlushQueued;

        public MyCustomApplicationContext()
        {
            // 强制创建句柄：钩子/后台线程通过它 BeginInvoke 回到 UI 线程
            _ = _uiInvoker.Handle;

            config = AppConfig.Load();
            config.Save();

            // 注册显示器句柄刷新回调（用于电源控制）
            BrightnessController.RefreshMonitorHandleCallback = GetLatestMonitorHandle;

            RefreshMonitors();
            RestoreOrReadBrightness();
            
            Application.ApplicationExit += (s, e) => { UnregisterSystemEvents(); SaveAllSettings(); CleanupDispatcher(); };
            RegisterSystemEvents();

            contextMenu = new ContextMenuStrip();
            
            // Apply dark mode styling to context menu
            ApplyContextMenuDarkMode(contextMenu);
            
            contextMenu.Items.Add("打开主界面", null, (s, e) => ShowMainWindow());
            contextMenu.Items.Add(new ToolStripSeparator());
            
            // 同步多屏亮度菜单项，带提示说明
            var syncItem = new ToolStripMenuItem("同步多屏亮度", null, (s, e) => SyncAllMonitorsBrightness());
            syncItem.ToolTipText = "一键同步所有显示器的亮度 (快捷：中键单击托盘图标)";
            contextMenu.Items.Add(syncItem);
            
            contextMenu.Items.Add(new ToolStripSeparator());
            
            // 检查更新菜单项
            var updateItem = new ToolStripMenuItem("检查更新", null, (s, e) => OpenUrl("https://github.com/HMwancheng/Simple_Brightness_Tool/releases/latest"));
            updateItem.ToolTipText = "查看最新版本发布页";
            contextMenu.Items.Add(updateItem);
            
            contextMenu.Items.Add("查看项目主页", null, (s, e) => OpenUrl("https://github.com/HMwancheng/Simple_Brightness_Tool"));
            contextMenu.Items.Add("查看作者主页", null, (s, e) => OpenUrl("https://github.com/HMwancheng"));
            
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("设置", null, (s, e) => ShowSettings());
            
            var debugItem = new ToolStripMenuItem("🛠 调试模式 (忽略曲线)", null, (s, e) => {
                _isDebugMode = !_isDebugMode;
                if (s is ToolStripMenuItem menuItem)
                {
                    menuItem.Checked = _isDebugMode;
                }
                // 切换调试模式后刷新一下，确保数值逻辑一致
                ReloadMonitorsSafe();
            });
            contextMenu.Items.Add(debugItem);
            
            contextMenu.Items.Add("重新扫描", null, (s, e) => { ReloadMonitorsSafe(); });
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("退出", null, (s, e) => {
                SaveAllSettings();
                mouseHook?.Uninstall();
                UnregisterHotkeys();
                trayIcon!.Visible = false;
                Application.Exit(); 
            });

            trayIcon = new NotifyIcon()
            {
                Icon = IconDrawer.LoadTrayIcon(config.TrayIconStyle), 
                ContextMenuStrip = contextMenu,
                Visible = true,
                Text = "HM's Simple Brightness Tool"
            };
            
            CacheTrayIconInfo();
            StartTrayRectWorker();

            trayIcon.MouseClick += (s, e) => { 
                if (e.Button == MouseButtons.Left) ShowBrightnessWindow(); 
                else if (e.Button == MouseButtons.Middle) SyncAllBrightness();
            };

            mouseHook = new MouseHook();
            mouseHook.MouseWheel += OnGlobalMouseWheel;
            mouseHook.Install();
            
            // Register global hotkeys
            RegisterHotkeys();
        }
        
        private void RegisterHotkeys()
        {
            // Create a native message window for hotkey messages
            _hotkeyWindow = new HotkeyMessageWindow();
            _hotkeyWindow.HotkeyPressed += (hotkeyId) => {
                // hotkeyId 1 = increase, hotkeyId 2 = decrease
                if (hotkeyId == 1) AdjustBrightnessByHotkey(true);
                else if (hotkeyId == 2) AdjustBrightnessByHotkey(false);
            };
            
            // Register increase brightness hotkey with ID 1
            if (!_hotkeyWindow.RegisterHotkey(config.HotkeyIncrease, 1))
            {
                Console.WriteLine($"Failed to register increase hotkey: {config.HotkeyIncrease}");
            }
            
            // Register decrease brightness hotkey with ID 2
            if (!_hotkeyWindow.RegisterHotkey(config.HotkeyDecrease, 2))
            {
                Console.WriteLine($"Failed to register decrease hotkey: {config.HotkeyDecrease}");
            }
        }
        
        private void UnregisterHotkeys()
        {
            _hotkeyWindow?.Dispose();
            _hotkeyWindow = null;
        }
        
        public void ReloadHotkeys()
        {
            UnregisterHotkeys();
            RegisterHotkeys();
        }
        
        private void AdjustBrightnessByHotkey(bool increase)
        {
            int step = config.ScrollStep;
            if (!increase) step = -step;
            
            foreach (var monitor in SnapshotMonitors())
            {
                int newValue = monitor.LastBrightness + step;
                newValue = Math.Max(0, Math.Min(100, newValue));
                
                // Use ApplyBrightness which handles curve; OSD 统一在最后显示一次
                ApplyBrightness(monitor, newValue, false, false);
            }
            
            // Show OSD after all adjustments
            ShowUnifiedOsd();
            
            // Save settings
            SaveAllSettings();
        }

        private void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("无法打开链接: " + ex.Message); }
        }

        private void SyncAllBrightness()
        {
            var snapshot = SnapshotMonitors();
            if (snapshot.Count == 0) return;
            int targetVal = snapshot[0].LastBrightness;
            foreach (var m in snapshot)
            {
                if (config.HiddenMonitors.Contains(m.UniqueId)) continue;
                m.LastBrightness = targetVal;
                ApplyBrightness(m, targetVal, true, false);
            }
            ShowUnifiedOsd();
        }

        private void RegisterSystemEvents() {
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        }
        private void UnregisterSystemEvents() {
            SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
            SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        }
        private async void SystemEvents_PowerModeChanged(object? sender, PowerModeChangedEventArgs e) {
            if (e.Mode == PowerModes.Resume) {
                await Task.Delay(2000);
                // 唤醒后低层鼠标钩子可能被系统移除，重新安装
                RunOnUi(ReinstallMouseHook);
                ReloadMonitorsSafe();
            }
        }
        private async void SystemEvents_SessionSwitch(object? sender, SessionSwitchEventArgs e) {
            if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect) {
                await Task.Delay(2000);
                RunOnUi(ReinstallMouseHook);
                ReloadMonitorsSafe();
            }
        }

        private void ReinstallMouseHook() {
            try {
                mouseHook?.Uninstall();
                mouseHook?.Install();
            } catch { }
        }
        private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e) {
            // 分辨率/缩放变化后托盘图标尺寸与位置都可能变
            RunOnUi(RefreshTrayIcon);
            ReloadMonitorsSafe();
        }
        private void SystemEvents_UserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Desktop) {
                RunOnUi(RefreshTrayIcon);
            }
        }
        private void RefreshTrayIcon() {
            try { if (trayIcon != null) trayIcon.Icon = IconDrawer.LoadTrayIcon(config.TrayIconStyle); } catch { }
        }

        // 获取显示器的亮度配置值，支持新旧ID格式兼容
        private int? GetSavedBrightnessForMonitor(MonitorInfo m, int index)
        {
            // 先尝试新ID
            if (config.SavedBrightness.TryGetValue(m.UniqueId, out int val)) return val;

            // 尝试旧ID格式 - 尝试多个可能的索引
            if (m.UniqueId.StartsWith("DDC_") && m.UniqueId.Contains("_H"))
            {
                string[] parts = m.UniqueId.Split('_');
                if (parts.Length >= 4)
                {
                    string nameHash = parts[1];
                    
                    // 尝试旧版ID格式 (DDC_{hash}_IDX_{i})
                    for (int i = 0; i < 4; i++)
                    {
                        string oldId = $"DDC_{nameHash}_IDX_{i}";
                        if (config.SavedBrightness.TryGetValue(oldId, out int oldVal))
                        {
                            // 迁移：复制到新ID
                            config.SavedBrightness[m.UniqueId] = oldVal;
                            return oldVal;
                        }
                    }
                    
                    // 尝试句柄变化后的ID (DDC_{hash}_H{oldHandle}_IDX_{i})
                    // 遍历所有保存的亮度配置，查找相同nameHash的项
                    var savedKeys = config.SavedBrightness.Keys.ToList();
                    foreach (var savedId in savedKeys)
                    {
                        if (savedId.StartsWith($"DDC_{nameHash}_H") && savedId != m.UniqueId)
                        {
                            // 找到相同nameHash但不同句柄的配置，迁移到新ID
                            int savedVal = config.SavedBrightness[savedId];
                            config.SavedBrightness[m.UniqueId] = savedVal;
                            return savedVal;
                        }
                    }
                }
            }
            return null;
        }

        // 检查显示器是否被隐藏，支持新旧ID格式和句柄变化后的ID迁移
        private bool IsMonitorHidden(MonitorInfo m, int index)
        {
            // 先检查当前ID
            if (config.HiddenMonitors.Contains(m.UniqueId)) return true;

            // 检查DDC显示器（可能因句柄变化导致ID改变）
            if (m.UniqueId.StartsWith("DDC_") && m.UniqueId.Contains("_H"))
            {
                string[] parts = m.UniqueId.Split('_');
                if (parts.Length >= 4)
                {
                    string nameHash = parts[1];
                    
                    // 检查旧ID格式 (DDC_{hash}_IDX_{i})
                    for (int i = 0; i < 4; i++)
                    {
                        string oldId = $"DDC_{nameHash}_IDX_{i}";
                        if (config.HiddenMonitors.Contains(oldId))
                        {
                            // 迁移到新ID
                            config.HiddenMonitors.Remove(oldId);
                            config.HiddenMonitors.Add(m.UniqueId);
                            return true;
                        }
                    }
                    
                    // 检查句柄变化后的ID (DDC_{hash}_H{oldHandle}_IDX_{i})
                    // 遍历所有隐藏配置，查找相同nameHash的项
                    var hiddenList = config.HiddenMonitors.ToList();
                    foreach (var hiddenId in hiddenList)
                    {
                        if (hiddenId.StartsWith($"DDC_{nameHash}_H") && hiddenId != m.UniqueId)
                        {
                            // 找到相同nameHash但不同句柄的隐藏配置，迁移到新ID
                            config.HiddenMonitors.Remove(hiddenId);
                            config.HiddenMonitors.Add(m.UniqueId);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        // 获取显示器的曲线配置，支持新旧ID格式和句柄变化后的ID迁移
        private Dictionary<int, int>? GetCurveForMonitor(MonitorInfo m, int index)
        {
            // 先尝试新ID
            if (config.Curves.TryGetValue(m.UniqueId, out var curve)) return curve;

            // 尝试旧ID格式 - 尝试多个可能的索引
            if (m.UniqueId.StartsWith("DDC_") && m.UniqueId.Contains("_H"))
            {
                string[] parts = m.UniqueId.Split('_');
                if (parts.Length >= 4)
                {
                    string nameHash = parts[1];

                    // 旧版按物理句柄生成 ID，唤醒一次就多一个别名。若其中恰好只有一条曲线
                    // 与其它都不同，说明它是最近一次编辑（旧版保存只写了一个 ID），优先采用它。
                    var editedCurve = config.FindOutlierCurve(nameHash);
                    if (editedCurve != null)
                    {
                        config.Curves[m.UniqueId] = editedCurve;
                        return editedCurve;
                    }
                    
                    // 尝试旧版ID格式 (DDC_{hash}_IDX_{i})
                    for (int i = 0; i < 4; i++)
                    {
                        string oldId = $"DDC_{nameHash}_IDX_{i}";
                        if (config.Curves.TryGetValue(oldId, out var oldCurve))
                        {
                            config.Curves[m.UniqueId] = oldCurve;
                            return oldCurve;
                        }
                    }
                    
                    // 尝试句柄变化后的ID (DDC_{hash}_H{oldHandle}_IDX_{i})
                    // 遍历所有曲线配置，查找相同nameHash的项
                    var curveKeys = config.Curves.Keys.ToList();
                    foreach (var curveId in curveKeys)
                    {
                        if (curveId.StartsWith($"DDC_{nameHash}_H") && curveId != m.UniqueId)
                        {
                            // 找到相同nameHash但不同句柄的曲线配置，迁移到新ID
                            var savedCurve = config.Curves[curveId];
                            config.Curves[m.UniqueId] = savedCurve;
                            return savedCurve;
                        }
                    }
                }
            }
            return null;
        }

        // 获取曲线配置的便捷方法（使用显示器在列表中的索引）
        private Dictionary<int, int> GetCurveForMonitorWithFallback(MonitorInfo m)
        {
            var snapshot = SnapshotMonitors();
            int index = snapshot.IndexOf(m);
            if (index < 0) index = 0;
            
            var curve = GetCurveForMonitor(m, index);
            if (curve != null && curve.Count >= 2) return curve;
            
            // 返回默认曲线
            return new Dictionary<int, int> { { 0, 0 }, { 100, 100 } };
        }

        /// <summary>
        /// 把 Action 投递到 UI 线程执行。鼠标钩子与后台扫描线程都必须通过它回到 UI 线程，
        /// 避免在钩子回调里做 WMI/DDC/窗口绘制导致全系统鼠标卡顿。
        /// </summary>
        private void RunOnUi(Action action)
        {
            try {
                if (_uiInvoker.IsHandleCreated) {
                    _uiInvoker.BeginInvoke(action);
                    return;
                }
            } catch { }
            action();
        }

        private List<MonitorInfo> SnapshotMonitors()
        {
            lock (_monitorsLock) return new List<MonitorInfo>(monitors);
        }

        public void ReloadMonitorsSafe() {
            // 合并唤醒时同时触发的多个系统事件，避免重复全量扫描
            if (Interlocked.CompareExchange(ref _reloading, 1, 0) != 0) return;

            // 系统事件可能来自非 UI 线程，先切回 UI 线程再操作窗口
            RunOnUi(() => {
                // 关闭主窗口（异步，不阻塞）
                try {
                    var form = Application.OpenForms.OfType<BrightnessForm>().FirstOrDefault();
                    if (form != null && !form.IsDisposed && form.Visible)
                        form.BeginInvoke(new Action(() => form.Close()));
                } catch { }

                // 旧 OSD 弹出层延迟销毁（异步，不阻塞）
                var oldOsd = _osdForm;
                if (oldOsd != null && !oldOsd.IsDisposed) {
                    try { oldOsd.BeginInvoke(new Action(() => oldOsd.Dispose())); } catch { }
                }
                _osdForm = null;

                // 先保存当前亮度（字典操作，很快）
                foreach (var m in SnapshotMonitors()) config.SavedBrightness[m.UniqueId] = m.LastBrightness;

                // 重量级枚举放到后台线程，且只做纯数据检测；结果再回到 UI 线程应用
                Task.Run(() => {
                    List<MonitorInfo> detected;
                    try {
                        detected = DetectMonitors();
                    } catch {
                        Interlocked.Exchange(ref _reloading, 0);
                        return;
                    }

                    RunOnUi(() => {
                        try {
                            ApplyDetectedMonitors(detected);
                            RestoreBrightnessAfterReload();
                            ReadRealBrightness();
                        } finally {
                            Interlocked.Exchange(ref _reloading, 0);
                        }
                    });
                });
            });
        }

        // 在显示器列表重建后，恢复每个显示器保存的亮度值（UI 线程）
        private void RestoreBrightnessAfterReload() {
            int idx = 0;
            foreach (var m in SnapshotMonitors()) {
                var savedVal = GetSavedBrightnessForMonitor(m, idx);
                if (savedVal.HasValue) m.LastBrightness = savedVal.Value;
                idx++;
            }
        }

        private void RestoreOrReadBrightness()
        {
            bool needReadHardware = false;
            int idx = 0;
            foreach (var m in SnapshotMonitors()) {
                var savedVal = GetSavedBrightnessForMonitor(m, idx);
                if (savedVal.HasValue) m.LastBrightness = savedVal.Value;
                else needReadHardware = true;
                idx++;
            }
            if (needReadHardware) ReadRealBrightness();
        }

        private void SaveAllSettings()
        {
            foreach(var m in SnapshotMonitors()) config.SavedBrightness[m.UniqueId] = m.LastBrightness;
            config.Save();
        }

        /// <summary>
        /// 后台读取硬件亮度，再回到 UI 线程反推软件值（后台线程不碰配置与控件）
        /// </summary>
        private void ReadRealBrightness()
        {
            var snapshot = SnapshotMonitors();
            Task.Run(() => {
                var readings = new List<(MonitorInfo Monitor, int Value)>();
                int wmiValue = snapshot.Any(m => m.Type == MonitorType.WMI) ? BrightnessController.GetWmiBrightness() : -1;
                foreach (var m in snapshot) {
                    int value = m.Type == MonitorType.WMI ? wmiValue
                              : (m.Type == MonitorType.DDC ? BrightnessController.GetVCPBrightness(m.Handle) : -1);
                    if (value != -1) readings.Add((m, value));
                }
                RunOnUi(() => ApplyRealBrightness(readings));
            });
        }

        // 应用硬件读数（UI 线程）
        private void ApplyRealBrightness(List<(MonitorInfo Monitor, int Value)> readings)
        {
            if (readings.Count == 0) return;
            var snapshot = SnapshotMonitors();

            // 先获取主屏（WMI）的保存亮度值，用于副屏同步
            int? primarySavedBrightness = null;
            foreach (var m in snapshot)
            {
                if (m.Type == MonitorType.WMI)
                {
                    int idx = snapshot.IndexOf(m);
                    if (idx < 0) idx = 0;
                    primarySavedBrightness = GetSavedBrightnessForMonitor(m, idx);
                    break;
                }
            }
            if (primarySavedBrightness == null && snapshot.Count > 0)
                primarySavedBrightness = GetSavedBrightnessForMonitor(snapshot[0], 0);

            foreach (var (m, realHardwareVal) in readings) {
                int finalSoftwareVal = realHardwareVal;

                // 非调试模式下按曲线反推软件值，避免下次调节亮度骤降
                if (!_isDebugMode && m.Type == MonitorType.DDC) {
                    var curve = GetCurveForMonitorWithFallback(m);
                    finalSoftwareVal = ReverseInterpolate(realHardwareVal, curve);
                }

                int idx = snapshot.IndexOf(m);
                if (idx < 0) idx = 0;
                var savedBrightness = GetSavedBrightnessForMonitor(m, idx);

                if (m.Type == MonitorType.DDC && primarySavedBrightness.HasValue)
                {
                    int primaryDiff = Math.Abs(finalSoftwareVal - primarySavedBrightness.Value);
                    if (primaryDiff <= 5)
                    {
                        finalSoftwareVal = primarySavedBrightness.Value;
                    }
                    else if (savedBrightness.HasValue)
                    {
                        int diff = Math.Abs(finalSoftwareVal - savedBrightness.Value);
                        if (diff <= 2 && diff > 0) finalSoftwareVal = savedBrightness.Value;
                    }
                }
                else if (m.Type == MonitorType.WMI && savedBrightness.HasValue) {
                    int diff = Math.Abs(finalSoftwareVal - savedBrightness.Value);
                    if (diff <= 2 && diff > 0) finalSoftwareVal = savedBrightness.Value;
                }

                m.LastBrightness = finalSoftwareVal;

                var form = Application.OpenForms.OfType<BrightnessForm>().FirstOrDefault();
                if (form != null && !form.IsDisposed && form.Visible)
                    form.BeginInvoke(new Action(() => form.UpdateSlider(m.UniqueId, finalSoftwareVal)));
            }
        }

        private void OnGlobalMouseWheel(object? sender, MouseEventArgs e)
        {
            // 注意：这里运行在低层鼠标钩子（WH_MOUSE_LL）回调中。
            // 回调超时会导致全系统鼠标发涩，所以只做缓存判断 + 累加，然后立即返回。
            if (!IsPointOverTrayIcon(e.X, e.Y)) return;

            int step = config.ScrollStep;
            Interlocked.Add(ref _pendingWheelDelta, e.Delta > 0 ? step : -step);

            // 同一时间只排队一次刷新，连续滚轮会在一次刷新里合并
            if (Interlocked.Exchange(ref _wheelFlushQueued, 1) == 0)
            {
                RunOnUi(FlushWheelDelta);
            }
        }

        // 在 UI 线程应用合并后的滚轮增量
        private void FlushWheelDelta()
        {
            Interlocked.Exchange(ref _wheelFlushQueued, 0);
            int change = Interlocked.Exchange(ref _pendingWheelDelta, 0);
            if (change == 0) return;

            var snapshot = SnapshotMonitors();
            for (int idx = 0; idx < snapshot.Count; idx++)
            {
                var m = snapshot[idx];
                if (IsMonitorHidden(m, idx)) continue;
                int newVal = Math.Clamp(m.LastBrightness + change, 0, 100);
                ApplyBrightness(m, newVal, true, false);
            }

            // 主窗口可见时已经在更新滑块，不再重复弹 OSD
            bool mainVisible = Application.OpenForms.OfType<BrightnessForm>().Any(f => f != null && f.Visible && !f.IsDisposed);
            if (!mainVisible) ShowUnifiedOsd();
        }

        // 只读缓存，不做任何 Shell IPC（真正的查询在后台线程里做）
        private bool IsPointOverTrayIcon(int x, int y)
        {
            NativeMethods.Rect rect;
            lock (_trayRectLock)
            {
                if (!_hasTrayIconRect) return false;
                rect = _trayIconRect;
            }
            return x >= rect.left && x <= rect.right && y >= rect.top && y <= rect.bottom;
        }

        /// <summary>
        /// 托盘图标矩形后台刷新线程：Shell_NotifyIconGetRect 是跨进程调用，
        /// 放在后台线程可以避免它拖慢消息泵和低层鼠标钩子。
        /// </summary>
        private void StartTrayRectWorker()
        {
            if (_trayRectThread != null) return;

            _trayRectThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        if (_trayIconHandle == IntPtr.Zero || _trayIconId == 0) CacheTrayIconInfo();

                        if (_trayIconHandle != IntPtr.Zero && _trayIconId != 0)
                        {
                            var nid = new NativeMethods.NOTIFYICONIDENTIFIER
                            {
                                cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONIDENTIFIER>(),
                                hWnd = _trayIconHandle,
                                uID = _trayIconId,
                                guidItem = Guid.Empty
                            };

                            if (NativeMethods.Shell_NotifyIconGetRect(ref nid, out NativeMethods.Rect rect) == 0)
                            {
                                lock (_trayRectLock)
                                {
                                    _trayIconRect = rect;
                                    _hasTrayIconRect = true;
                                }
                            }
                        }
                    }
                    catch { }
                    Thread.Sleep(1000);
                }
            })
            { IsBackground = true, Name = "TrayIconRectWorker" };

            _trayRectThread.Start();
        }

        private void CleanupDispatcher()
        {
            try { _uiInvoker.Dispose(); } catch { }
        }

        private void CacheTrayIconInfo()
        {
            try
            {
                var notifyIconType = typeof(NotifyIcon);
                
                var allFields = notifyIconType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var field in allFields)
                {
                    if (_trayIconId == 0 && (field.FieldType == typeof(uint) || field.FieldType == typeof(int)))
                    {
                        if (field.Name.ToLowerInvariant().Contains("id"))
                        {
                            _trayIconId = Convert.ToUInt32(field.GetValue(trayIcon));
                        }
                    }
                    else if (_trayIconHandle == IntPtr.Zero && 
                             (field.FieldType.Name == "NativeWindow" || field.FieldType.Name.Contains("Window")))
                    {
                        var window = field.GetValue(trayIcon);
                        if (window != null)
                        {
                            var handleProp = window.GetType().GetProperty("Handle", BindingFlags.Public | BindingFlags.Instance);
                            if (handleProp != null)
                            {
                                var handleValue = handleProp.GetValue(window);
                                if (handleValue != null)
                                {
                                    _trayIconHandle = (IntPtr)handleValue;
                                }
                            }
                        }
                    }
                    else if (_trayIconHandle == IntPtr.Zero && field.FieldType == typeof(IntPtr))
                    {
                        if (field.Name.ToLowerInvariant().Contains("hwnd") || 
                            field.Name.ToLowerInvariant().Contains("handle"))
                        {
                            var fieldValue = field.GetValue(trayIcon);
                            if (fieldValue != null)
                            {
                                _trayIconHandle = (IntPtr)fieldValue;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public void ApplyBrightness(MonitorInfo m, int val, bool updateUi = true, bool showOsd = true)
        {
            m.LastBrightness = val;
            int finalVal = val;
            if (!_isDebugMode && m.Type == MonitorType.DDC) {
                // 获取曲线配置，支持新旧ID格式兼容
                var curve = GetCurveForMonitorWithFallback(m);
                finalVal = Interpolate(val, curve);
            }
            
            if (m.Type == MonitorType.WMI) {
                BrightnessController.SetBrightnessImmediate(m, finalVal);
            } else {
                BrightnessController.SetBrightnessDebounced(m, finalVal, config.DebounceTime);
            }
            
            var form = Application.OpenForms.OfType<BrightnessForm>().FirstOrDefault();
            bool isMainWinVisible = (form != null && !form.IsDisposed && form.Visible);

            if (isMainWinVisible) {
                if (updateUi) form!.BeginInvoke(new Action(() => form.UpdateSlider(m.UniqueId, val)));
            } else if (showOsd) {
                ShowUnifiedOsd();
            }
        }

        private void ShowUnifiedOsd()
        {
            // 始终重新计算可见显示器列表，以确保隐藏状态变化被正确反映
            var visibleMonitors = new List<MonitorInfo>();
            int idx = 0;
            foreach (var m in SnapshotMonitors()) {
                if (!IsMonitorHidden(m, idx)) visibleMonitors.Add(m);
                idx++;
            }
            if (visibleMonitors.Count == 0) return;

            if (_osdForm == null || _osdForm.IsDisposed) {
                _osdForm = new NativeOsdForm(visibleMonitors);
            } else {
                _osdForm.UpdateMonitors(visibleMonitors);
            }

            // 显示第一个可见显示器的OSD
            if (_osdForm.InvokeRequired)
                _osdForm.BeginInvoke(new Action(() => _osdForm.ShowForMonitor(0)));
            else
                _osdForm.ShowForMonitor(0);
        }

        // 正向插值：软件 -> 硬件 (X -> Y)
        private int Interpolate(int input, Dictionary<int, int> points) {
            var sorted = points.OrderBy(k => k.Key).ToList();
            if (input <= sorted.First().Key) return sorted.First().Value;
            if (input >= sorted.Last().Key) return sorted.Last().Value;
            for (int i = 0; i < sorted.Count - 1; i++) {
                if (input >= sorted[i].Key && input <= sorted[i+1].Key) {
                    return sorted[i].Value + (input - sorted[i].Key) * (sorted[i+1].Value - sorted[i].Value) / (sorted[i+1].Key - sorted[i].Key);
                }
            }
            return input;
        }

        // [新增] 反向插值：硬件 -> 软件 (Y -> X)
        private int ReverseInterpolate(int hardwareVal, Dictionary<int, int> points) {
            var sorted = points.OrderBy(k => k.Key).ToList();
            // 边界检查
            if (hardwareVal <= sorted.First().Value) return sorted.First().Key;
            if (hardwareVal >= sorted.Last().Value) return sorted.Last().Key;

            for (int i = 0; i < sorted.Count - 1; i++) {
                int y1 = sorted[i].Value;
                int y2 = sorted[i+1].Value;
                
                // 判断是否在Y轴区间内 (处理可能的曲线波动，虽然亮度曲线通常是单调递增的)
                // 注意：这里假设曲线是分段线性的
                if ((hardwareVal >= y1 && hardwareVal <= y2) || (hardwareVal <= y1 && hardwareVal >= y2)) {
                    int x1 = sorted[i].Key;
                    int x2 = sorted[i+1].Key;
                    
                    if (y1 == y2) return x1; // 避免除以零 (水平线段)
                    
                    // 线性反推公式: x = x1 + (y - y1) * (x2 - x1) / (y2 - y1)
                    return x1 + (hardwareVal - y1) * (x2 - x1) / (y2 - y1);
                }
            }
            return hardwareVal; // 兜底
        }

        private void ShowBrightnessWindow() {
            var snapshot = SnapshotMonitors();
            var form = Application.OpenForms.OfType<BrightnessForm>().FirstOrDefault();
            if (form == null || form.IsDisposed)
                form = new BrightnessForm(snapshot, config, this, contextMenu);
            form.Show(); form.Activate();
            foreach(var m in snapshot) form.UpdateSlider(m.UniqueId, m.LastBrightness);
        }

        private void ShowSettings()
        {
            var form = new SettingsForm(config);
            form.FormClosed += (s, e) =>
            {
                if (form.HotkeysChanged)
                {
                    // Reload hotkeys if changed
                    ReloadHotkeys();
                }
                // Update tray icon if changed
                if (form.TrayIconChanged && trayIcon != null)
                {
                    trayIcon.Icon = IconDrawer.LoadTrayIcon(config.TrayIconStyle);
                }
            };
            form.Show();
        }

        private void ShowMainWindow()
        {
            // 显示主界面（亮度调节窗口）
            ShowBrightnessWindow();
        }

        private void SyncAllMonitorsBrightness()
        {
            // 同步所有显示器的亮度（与中键点击托盘图标效果相同）
            try
            {
                var snapshot = SnapshotMonitors();
                if (snapshot.Count == 0) return;
                
                // 获取第一个显示器的亮度作为参考
                int referenceBrightness = snapshot[0].LastBrightness;
                
                // 应用到所有其他显示器
                foreach (var monitor in snapshot.Skip(1))
                {
                    ApplyBrightness(monitor, referenceBrightness, false, false);
                }
                
                // 显示OSD提示
                ShowUnifiedOsd();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"同步亮度失败: {ex.Message}");
            }
        }

        private void ApplyContextMenuDarkMode(ContextMenuStrip menu)
        {
            // Set menu render mode for custom styling - pass a delegate to check theme dynamically
            menu.Renderer = new ThemedToolStripRenderer(() => ThemeManager.IsDarkMode);

            // Handle menu opening to style items - check theme dynamically
            menu.Opening += (s, e) =>
            {
                bool isDarkMode = ThemeManager.IsDarkMode;
                
                // Update menu background and text colors
                menu.BackColor = isDarkMode ? Color.FromArgb(45, 45, 45) : SystemColors.Control;
                menu.ForeColor = isDarkMode ? Color.White : SystemColors.ControlText;
                
                foreach (ToolStripItem item in menu.Items)
                {
                    ApplyMenuItemStyle(item, isDarkMode);
                }
            };
        }

        private void ApplyMenuItemStyle(ToolStripItem item, bool isDarkMode)
        {
            if (item is ToolStripMenuItem menuItem)
            {
                menuItem.BackColor = isDarkMode ? Color.FromArgb(45, 45, 45) : SystemColors.Control;
                menuItem.ForeColor = isDarkMode ? Color.White : SystemColors.ControlText;

                // Handle hover/selection colors
                menuItem.MouseEnter += (s, e) =>
                {
                    menuItem.BackColor = isDarkMode ? Color.FromArgb(60, 60, 60) : SystemColors.Highlight;
                    menuItem.ForeColor = isDarkMode ? Color.White : SystemColors.HighlightText;
                };

                menuItem.MouseLeave += (s, e) =>
                {
                    menuItem.BackColor = isDarkMode ? Color.FromArgb(45, 45, 45) : SystemColors.Control;
                    menuItem.ForeColor = isDarkMode ? Color.White : SystemColors.ControlText;
                };
            }
            else if (item is ToolStripSeparator separator)
            {
                separator.BackColor = isDarkMode ? Color.FromArgb(45, 45, 45) : SystemColors.Control;
            }
        }

        // Custom ToolStripRenderer for themed menu
        private class ThemedToolStripRenderer : ToolStripProfessionalRenderer
        {
            private Func<bool> _isDarkModeFunc;

            public ThemedToolStripRenderer(Func<bool> isDarkModeFunc) : base(new ThemedColorTable(isDarkModeFunc))
            {
                _isDarkModeFunc = isDarkModeFunc;
            }

            private bool IsDarkMode => _isDarkModeFunc();

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                // Draw custom check mark background
                Rectangle rect = e.ImageRectangle;
                Color bgColor = IsDarkMode ? Color.FromArgb(45, 45, 45) : SystemColors.Control;
                using (Brush brush = new SolidBrush(bgColor))
                {
                    e.Graphics.FillRectangle(brush, rect);
                }

                // Draw check mark
                if (e.Item is ToolStripMenuItem menuItem && menuItem.Checked)
                {
                    Color checkColor = IsDarkMode ? Color.White : SystemColors.ControlText;
                    using (Brush checkBrush = new SolidBrush(checkColor))
                    {
                        // Draw a simple check mark
                        Point[] checkPoints = new Point[]
                        {
                            new Point(rect.Left + 2, rect.Top + rect.Height / 2),
                            new Point(rect.Left + rect.Width / 2 - 2, rect.Bottom - 4),
                            new Point(rect.Right - 2, rect.Top + 2)
                        };
                        using (Pen checkPen = new Pen(checkBrush, 2))
                        {
                            e.Graphics.DrawLines(checkPen, checkPoints);
                        }
                    }
                }
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                Color separatorColor = IsDarkMode ? Color.FromArgb(60, 60, 60) : Color.FromArgb(200, 200, 200);
                using (Pen pen = new Pen(separatorColor))
                {
                    e.Graphics.DrawLine(pen, e.Item.ContentRectangle.Left, e.Item.ContentRectangle.Height / 2,
                        e.Item.ContentRectangle.Right, e.Item.ContentRectangle.Height / 2);
                }
            }

            protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
            {
                // Override to prevent white margin in dark mode
                Color marginColor = IsDarkMode ? Color.FromArgb(45, 45, 45) : SystemColors.Control;
                using (Brush brush = new SolidBrush(marginColor))
                {
                    e.Graphics.FillRectangle(brush, e.AffectedBounds);
                }
            }
        }

        // Custom color table for themed menu
        private class ThemedColorTable : ProfessionalColorTable
        {
            private Func<bool> _isDarkModeFunc;

            public ThemedColorTable(Func<bool> isDarkModeFunc)
            {
                _isDarkModeFunc = isDarkModeFunc;
            }

            private bool IsDarkMode => _isDarkModeFunc();

            public override Color MenuBorder => IsDarkMode ? Color.FromArgb(60, 60, 60) : Color.FromArgb(200, 200, 200);
            public override Color MenuItemBorder => IsDarkMode ? Color.FromArgb(60, 60, 60) : Color.FromArgb(200, 200, 200);
            public override Color MenuItemSelected => IsDarkMode ? Color.FromArgb(60, 60, 60) : SystemColors.Highlight;
            public override Color MenuItemSelectedGradientBegin => IsDarkMode ? Color.FromArgb(60, 60, 60) : SystemColors.Highlight;
            public override Color MenuItemSelectedGradientEnd => IsDarkMode ? Color.FromArgb(60, 60, 60) : SystemColors.Highlight;
            public override Color MenuStripGradientBegin => IsDarkMode ? Color.FromArgb(45, 45, 45) : SystemColors.Control;
            public override Color MenuStripGradientEnd => IsDarkMode ? Color.FromArgb(45, 45, 45) : SystemColors.Control;
            public override Color ToolStripDropDownBackground => IsDarkMode ? Color.FromArgb(45, 45, 45) : SystemColors.Control;
            public override Color ImageMarginGradientBegin => IsDarkMode ? Color.FromArgb(45, 45, 45) : SystemColors.Control;
            public override Color ImageMarginGradientMiddle => IsDarkMode ? Color.FromArgb(45, 45, 45) : SystemColors.Control;
            public override Color ImageMarginGradientEnd => IsDarkMode ? Color.FromArgb(45, 45, 45) : SystemColors.Control;
            public override Color CheckBackground => IsDarkMode ? Color.FromArgb(45, 45, 45) : SystemColors.Control;
            public override Color CheckSelectedBackground => IsDarkMode ? Color.FromArgb(60, 60, 60) : SystemColors.Highlight;
            public override Color CheckPressedBackground => IsDarkMode ? Color.FromArgb(60, 60, 60) : SystemColors.Highlight;
        }

        // 配置迁移：旧版ID格式的兼容逻辑已在 GetSavedBrightnessForMonitor / IsMonitorHidden /
        // GetCurveForMonitor / RefreshMonitors 中按需实时完成，无需额外的全量迁移。

        public List<MonitorInfo> GetMonitors()
        {
            return SnapshotMonitors();
        }

        /// <summary>
        /// 按 UniqueId（或同名哈希）查找当前显示器，供曲线编辑器保存时定位到最新 ID
        /// </summary>
        public MonitorInfo? FindMonitorByUniqueIdOrHash(string uniqueId)
        {
            var snapshot = SnapshotMonitors();
            var exact = snapshot.FirstOrDefault(m => m.UniqueId == uniqueId);
            if (exact != null) return exact;

            if (uniqueId.StartsWith("DDC_") && uniqueId.Contains("_H")) {
                string[] parts = uniqueId.Split('_');
                if (parts.Length >= 2) {
                    string hash = parts[1];
                    return snapshot.FirstOrDefault(m => m.UniqueId.StartsWith($"DDC_{hash}_H"));
                }
            }
            return null;
        }

        /// <summary>曲线保存后立即按新曲线重新应用到硬件，避免“保存了但看不出变化”</summary>
        public void ReapplyBrightness(MonitorInfo m)
        {
            ApplyBrightness(m, m.LastBrightness, true, false);
            ShowUnifiedOsd();
        }

        /// <summary>
        /// 根据 UniqueId 获取最新的显示器句柄（用于睡眠唤醒后刷新句柄）
        /// </summary>
        /// <param name="uniqueId">显示器唯一标识</param>
        /// <returns>最新的显示器物理句柄，如果未找到则返回 IntPtr.Zero</returns>
        private IntPtr GetLatestMonitorHandle(string uniqueId)
        {
            // 先在当前显示器列表中查找
            var monitor = SnapshotMonitors().FirstOrDefault(m => m.UniqueId == uniqueId);
            if (monitor != null && monitor.Handle != IntPtr.Zero)
            {
                return monitor.Handle;
            }

            // 如果未找到，尝试重新枚举显示器获取最新句柄
            IntPtr foundHandle = IntPtr.Zero;
            string targetId = uniqueId;
            
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.Rect lprcMonitor, IntPtr dwData) {
                int count = 0;
                NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, ref count);
                if (count > 0) {
                    var pMs = new NativeMethods.PHYSICAL_MONITOR[count];
                    if (NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, pMs)) {
                        for (int i = 0; i < pMs.Length; i++) {
                            string originalName = new string(pMs[i].szPhysicalMonitorDescription).Trim('\0');
                            string currentUniqueId = GetMonitorUniqueId(originalName, pMs[i].hPhysicalMonitor, i);
                            
                            // 匹配 UniqueId
                            if (currentUniqueId == targetId) {
                                foundHandle = pMs[i].hPhysicalMonitor;
                            } else {
                                // 尝试匹配相同 nameHash 的旧 ID（句柄变化后的兼容）
                                if (targetId.StartsWith("DDC_") && targetId.Contains("_H")) {
                                    string[] parts = targetId.Split('_');
                                    if (parts.Length >= 4) {
                                        string targetNameHash = parts[1];
                                        string currentNameHash = GetStableHash(originalName);
                                        if (targetNameHash == currentNameHash) {
                                            // 找到相同名称哈希的显示器，使用新句柄
                                            foundHandle = pMs[i].hPhysicalMonitor;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            // 更新显示器列表中的句柄
            if (foundHandle != IntPtr.Zero) {
                lock (_monitorsLock) {
                    var existingMonitor = monitors.FirstOrDefault(m => m.UniqueId == uniqueId);
                    if (existingMonitor != null) {
                        existingMonitor.Handle = foundHandle;
                    } else {
                        // 如果显示器不存在于列表中，添加它
                        monitors.Add(new MonitorInfo { 
                            Type = MonitorType.DDC, 
                            Handle = foundHandle, 
                            UniqueId = uniqueId,
                            Name = "外接显示器",
                            LastBrightness = 50
                        });
                    }
                }
            }

            return foundHandle;
        }

        /// <summary>
        /// 纯数据扫描（不写配置、不碰共享列表），可在后台线程安全执行
        /// </summary>
        private List<MonitorInfo> DetectMonitors()
        {
            var detected = new List<MonitorInfo>();

            try {
                using var searcher = new ManagementObjectSearcher("root\\Wmi", "SELECT * FROM WmiMonitorBrightness");
                foreach (ManagementObject queryObj in searcher.Get()) {
                    string id = queryObj["InstanceName"]?.ToString() ?? "UnknownWmi";
                    if (!detected.Any(m => m.InstanceId == id)) {
                        string uniqueId = "WMI_" + GetStableHash(id);
                        string name = config.CustomNames.TryGetValue(uniqueId, out var customName) ? customName : "内置屏幕";
                        detected.Add(new MonitorInfo { Type = MonitorType.WMI, Name = name, InstanceId = id, UniqueId = uniqueId });
                    }
                }
            } catch { }

            // 使用 HashSet 跟踪已添加的显示器句柄，防止重复枚举
            HashSet<IntPtr> addedHandles = new HashSet<IntPtr>();

            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.Rect lprcMonitor, IntPtr dwData) {
                int count = 0;
                NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, ref count);
                if (count > 0) {
                    var pMs = new NativeMethods.PHYSICAL_MONITOR[count];
                    if (NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, pMs)) {
                        for (int i = 0; i < pMs.Length; i++) {
                            if (!addedHandles.Add(pMs[i].hPhysicalMonitor)) continue;

                            string originalName = new string(pMs[i].szPhysicalMonitorDescription).Trim('\0');
                            string uniqueId = GetMonitorUniqueId(originalName, pMs[i].hPhysicalMonitor, i);
                            string name = config.CustomNames.TryGetValue(uniqueId, out var customName) ? customName : originalName;
                            detected.Add(new MonitorInfo { Type = MonitorType.DDC, Name = name, Handle = pMs[i].hPhysicalMonitor, UniqueId = uniqueId });
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            return detected;
        }

        /// <summary>
        /// 用扫描结果替换共享列表（必须在 UI 线程调用），并顺带迁移旧 ID 的自定义名称
        /// </summary>
        private void ApplyDetectedMonitors(List<MonitorInfo> detected)
        {
            bool migratedNames = false;
            foreach (var m in detected) {
                if (m.Type != MonitorType.DDC) continue;
                if (config.CustomNames.ContainsKey(m.UniqueId)) continue;

                var parts = m.UniqueId.Split('_');
                if (parts.Length < 4) continue;
                string nameHash = parts[1];

                // 旧版 ID：DDC_hash_IDX_i
                string oldId = $"DDC_{nameHash}_IDX_{parts[parts.Length - 1]}";
                if (config.CustomNames.TryGetValue(oldId, out var oldName)) {
                    config.CustomNames[m.UniqueId] = oldName;
                    m.Name = oldName;
                    migratedNames = true;
                    continue;
                }

                // 旧版句柄 ID（DDC_hash_Hxxx_IDX_i）：新稳定 ID 也需要继承自定义名称
                string? aliasName = null;
                foreach (var kv in config.CustomNames) {
                    if (kv.Key.StartsWith($"DDC_{nameHash}_H")) {
                        aliasName = kv.Value;
                        break;
                    }
                }
                if (aliasName != null) {
                    config.CustomNames[m.UniqueId] = aliasName;
                    m.Name = aliasName;
                    migratedNames = true;
                }
            }

            bool pruned = PruneLegacyAliases(detected);

            lock (_monitorsLock) {
                monitors.Clear();
                monitors.AddRange(detected);
            }

            if (migratedNames || pruned) config.Save();
        }

        /// <summary>
        /// 清理旧版因物理句柄变化留下的海量历史 ID。
        /// 只在同一 nameHash 恰好对应一台“已检测到”的显示器、且别名数量明显异常（≥10）时执行，
        /// 避免误伤两台同型号显示器的正常配置。
        /// </summary>
        private bool PruneLegacyAliases(List<MonitorInfo> detected)
        {
            bool changed = false;

            var groups = detected
                .Where(m => m.Type == MonitorType.DDC)
                .GroupBy(m => {
                    var parts = m.UniqueId.Split('_');
                    return parts.Length >= 4 ? parts[1] : "";
                })
                .Where(g => g.Key.Length > 0 && g.Count() == 1);

            foreach (var group in groups) {
                var monitor = group.First();
                string hash = group.Key;
                string stableId = monitor.UniqueId;
                string prefix = $"DDC_{hash}_";

                int aliasCount = config.CustomNames.Keys.Count(k => k.StartsWith(prefix))
                               + config.SavedBrightness.Keys.Count(k => k.StartsWith(prefix))
                               + config.Curves.Keys.Count(k => k.StartsWith(prefix))
                               + config.HiddenMonitors.Count(k => k.StartsWith(prefix));
                if (aliasCount < 10) continue;

                // 自定义名称：保留当前稳定 ID，删除其它别名
                if (!config.CustomNames.ContainsKey(stableId)) {
                    var alias = config.CustomNames.LastOrDefault(kv => kv.Key.StartsWith(prefix));
                    if (alias.Key != null) {
                        config.CustomNames[stableId] = alias.Value;
                        monitor.Name = alias.Value;
                        changed = true;
                    }
                }
                foreach (var key in config.CustomNames.Keys.Where(k => k.StartsWith(prefix) && k != stableId).ToList()) {
                    config.CustomNames.Remove(key);
                    changed = true;
                }

                // 隐藏状态：只要任一别名被隐藏，就把稳定 ID 标记为隐藏
                if (config.HiddenMonitors.Any(k => k != stableId && k.StartsWith(prefix))) {
                    if (!config.HiddenMonitors.Contains(stableId)) config.HiddenMonitors.Add(stableId);
                    config.HiddenMonitors.RemoveAll(k => k != stableId && k.StartsWith(prefix));
                    changed = true;
                }

                // 亮度：取最后写入的别名（字典顺序即写入顺序，最后一条最接近当前使用值）
                if (!config.SavedBrightness.ContainsKey(stableId)) {
                    string? lastKey = config.SavedBrightness.Keys.LastOrDefault(k => k.StartsWith(prefix));
                    if (lastKey != null) {
                        config.SavedBrightness[stableId] = config.SavedBrightness[lastKey];
                        changed = true;
                    }
                }
                foreach (var key in config.SavedBrightness.Keys.Where(k => k.StartsWith(prefix) && k != stableId).ToList()) {
                    config.SavedBrightness.Remove(key);
                    changed = true;
                }

                // 曲线：优先采用“唯一不同”的编辑版本；否则仅在所有别名一致时迁移
                if (!config.Curves.ContainsKey(stableId)) {
                    var chosen = config.FindOutlierCurve(hash);
                    if (chosen == null) {
                        var aliasCurves = config.Curves
                            .Where(kv => kv.Key.StartsWith(prefix))
                            .Select(kv => kv.Value)
                            .Where(c => c != null && c.Count >= 2)
                            .ToList();
                        if (aliasCurves.Count > 0 &&
                            aliasCurves.All(c => CurveSignature(c) == CurveSignature(aliasCurves[0]))) {
                            chosen = aliasCurves[0];
                        }
                    }
                    if (chosen != null) {
                        config.Curves[stableId] = chosen;
                        changed = true;
                    }
                }
                if (config.Curves.ContainsKey(stableId)) {
                    foreach (var key in config.Curves.Keys.Where(k => k.StartsWith(prefix) && k != stableId).ToList()) {
                        config.Curves.Remove(key);
                        changed = true;
                    }
                }
            }

            return changed;
        }

        private static string CurveSignature(Dictionary<int, int> curve)
        {
            return string.Join(";", curve.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value}"));
        }

        /// <summary>同步扫描（仅 UI 线程少量场景使用，例如重命名后立即刷新）</summary>
        public void RefreshMonitors()
        {
            var detected = DetectMonitors();
            ApplyDetectedMonitors(detected);
        }

        private static string GetStableHash(string str) {
            ulong hash = 5381;
            foreach (char c in str) hash = ((hash << 5) + hash) + c;
            return hash.ToString();
        }

        // 生成稳定的唯一 ID：使用显示输出设备名（\\.\DISPLAY1）而不是物理句柄。
        // 物理句柄在睡眠唤醒/重新枚举后会变化，旧逻辑会把同一台显示器变成多个 ID，
        // 导致曲线在历史别名之间来回迁移（表现为“改过的曲线过一会儿又变回去”）。
        private static string GetMonitorUniqueId(string originalName, IntPtr hMonitor, int index) {
            string nameHash = GetStableHash(originalName);
            string deviceHash = GetMonitorDeviceHash(hMonitor);
            string suffix = string.IsNullOrEmpty(deviceHash) ? hMonitor.ToInt32().ToString() : deviceHash;
            return $"DDC_{nameHash}_H{suffix}_IDX_{index}";
        }

        /// <summary>取显示输出设备名的哈希（如 \\.\DISPLAY1），用于构造稳定 ID</summary>
        private static string GetMonitorDeviceHash(IntPtr hMonitor)
        {
            try {
                var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
                if (NativeMethods.GetMonitorInfo(hMonitor, ref info) && !string.IsNullOrEmpty(info.szDevice))
                    return GetStableHash(info.szDevice);
            } catch { }
            return "";
        }
    }

    public static class BrightnessController 
    {
        private const byte VCP_BRIGHTNESS = 0x10;

        // 显示器句柄刷新回调（用于睡眠唤醒后重新获取最新句柄）
        public static Func<string, IntPtr>? RefreshMonitorHandleCallback { get; set; }

        // ================== DDC/CI：每个显示器一个后台写线程，只写最新值 ==================
        private sealed class DdcChannel
        {
            public readonly object Sync = new object();
            public MonitorInfo Target = new MonitorInfo();
            public int Level;
            public int DebounceMs;
            public long LastRequest;
            public bool HasPending;
        }

        private static readonly object _channelsLock = new object();
        private static readonly Dictionary<string, DdcChannel> _channels = new Dictionary<string, DdcChannel>();

        public static void SetBrightnessDebounced(MonitorInfo monitor, int level, int debounceMs) {
            if (monitor.Type == MonitorType.WMI) { QueueWmiBrightness(level); return; }
            if (monitor.Type != MonitorType.DDC) return;

            DdcChannel channel;
            lock (_channelsLock) {
                if (!_channels.TryGetValue(monitor.UniqueId, out channel!)) {
                    channel = new DdcChannel();
                    _channels[monitor.UniqueId] = channel;
                    var worker = new Thread(() => DdcWorkerLoop(channel)) {
                        IsBackground = true,
                        Name = "DdcBrightnessWriter"
                    };
                    worker.Start();
                }
            }

            lock (channel.Sync) {
                channel.Target = monitor;
                channel.Level = level;
                channel.DebounceMs = Math.Max(0, debounceMs);
                channel.LastRequest = Environment.TickCount64;
                channel.HasPending = true;
                Monitor.Pulse(channel.Sync);
            }
        }

        public static void SetBrightnessImmediate(MonitorInfo monitor, int level) {
            if (monitor.Type == MonitorType.WMI) { QueueWmiBrightness(level); return; }
            // DDC 也走通道线程（0ms 防抖），避免在 UI/钩子线程上阻塞 I²C 写入
            SetBrightnessDebounced(monitor, level, 0);
        }

        private static void DdcWorkerLoop(DdcChannel channel) {
            while (true) {
                int level;
                MonitorInfo target;

                lock (channel.Sync) {
                    while (!channel.HasPending) Monitor.Wait(channel.Sync);

                    // 防抖窗口内持续接收新值，最后一次请求后等待 debounceMs 再写
                    while (true) {
                        long remaining = channel.LastRequest + channel.DebounceMs - Environment.TickCount64;
                        if (remaining <= 0) break;
                        Monitor.Wait(channel.Sync, (int)Math.Min(remaining, 1000));
                    }

                    channel.HasPending = false;
                    level = channel.Level;
                    target = channel.Target;
                }

                try { NativeMethods.SetVCPFeature(target.Handle, VCP_BRIGHTNESS, (uint)level); } catch { }
            }
        }

        // ================== WMI：独占线程 + 复用 COM 对象 ==================
        private static readonly object _wmiLock = new object();
        private static readonly AutoResetEvent _wmiSignal = new AutoResetEvent(false);
        private static Thread? _wmiThread;
        private static int _wmiPendingLevel;
        private static bool _wmiPending;

        private static void QueueWmiBrightness(int level) {
            lock (_wmiLock) {
                _wmiPendingLevel = level;
                _wmiPending = true;
                if (_wmiThread == null) {
                    _wmiThread = new Thread(WmiWorkerLoop) { IsBackground = true, Name = "WmiBrightnessWriter" };
                    _wmiThread.Start();
                }
            }
            _wmiSignal.Set();
        }

        private static void WmiWorkerLoop() {
            ManagementObject? method = null;
            ManagementObjectCollection? collection = null;
            ManagementObjectSearcher? searcher = null;

            while (true) {
                _wmiSignal.WaitOne();

                int level;
                lock (_wmiLock) {
                    if (!_wmiPending) continue;
                    _wmiPending = false;
                    level = _wmiPendingLevel;
                }

                try {
                    if (method == null) {
                        searcher = new ManagementObjectSearcher("root\\Wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
                        collection = searcher.Get();
                        foreach (ManagementObject m in collection) { method = m; break; }
                    }
                    method?.InvokeMethod("WmiSetBrightness", new object[] { 1, level });
                } catch {
                    // COM 对象失效（唤醒/驱动重载）时丢弃缓存，下次重建
                    method?.Dispose();
                    method = null;
                    collection = null;
                    searcher?.Dispose();
                    searcher = null;
                }
            }
        }

        public static int GetWmiBrightness() {
            try {
                using var searcher = new ManagementObjectSearcher("root\\Wmi", "SELECT * FROM WmiMonitorBrightness");
                foreach (ManagementObject obj in searcher.Get()) {
                    var val = obj["CurrentBrightness"];
                    if (val != null && int.TryParse(val.ToString(), out int current)) return current;
                }
            } catch { }
            return -1;
        }

        public static void SetPowerState(MonitorInfo monitor, bool turnOn, int? powerOffMode = null) { 
            if (monitor.Type == MonitorType.DDC) {
                // 睡眠唤醒后显示器句柄可能已变化，尝试刷新句柄
                IntPtr actualHandle = monitor.Handle;
                if (RefreshMonitorHandleCallback != null && !string.IsNullOrEmpty(monitor.UniqueId)) {
                    IntPtr refreshedHandle = RefreshMonitorHandleCallback(monitor.UniqueId);
                    if (refreshedHandle != IntPtr.Zero) {
                        actualHandle = refreshedHandle;
                    }
                }
                
                if (turnOn) {
                    NativeMethods.SetVCPFeature(actualHandle, 0xD6, 0x01u);
                } else {
                    int powerMode = powerOffMode ?? AppConfig.Load().PowerOffMode;
                    if (powerMode != 5) powerMode = 4;
                    NativeMethods.SetVCPFeature(actualHandle, 0xD6, (uint)powerMode); 
                }
            }
        }

        public static int GetVCPBrightness(IntPtr hMonitor) { 
            uint current = 50, max = 100; 
            if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(hMonitor, 0x10, IntPtr.Zero, ref current, ref max)) return (int)current; 
            return -1; 
        } 
    }

    public class AppConfig { 
        public int ScrollStep { get; set; } = 5; 
        public int DebounceTime { get; set; } = 200; 
        public int PowerOffMode { get; set; } = 4; // 4=待机, 5=关机
        public ThemeMode ThemeMode { get; set; } = ThemeMode.System;
        public string HotkeyIncrease { get; set; } = "Ctrl+F5";  // Default hotkey for brightness increase
        public string HotkeyDecrease { get; set; } = "Ctrl+F6";  // Default hotkey for brightness decrease
        public string TrayIconStyle { get; set; } = "Hybrid";  // Hybrid, Minimalist, Transparent
        public List<string> HiddenMonitors { get; set; } = new List<string>(); 
        public Dictionary<string, string> CustomNames { get; set; } = new Dictionary<string, string>(); 
        public Dictionary<string, Dictionary<int, int>> Curves { get; set; } = new Dictionary<string, Dictionary<int, int>>(); 
        public Dictionary<string, int> SavedBrightness { get; set; } = new Dictionary<string, int>();
        private static readonly string ConfigPath = Path.Combine(Application.StartupPath, "HMSimpleBrightness_Config.json"); 
        public static AppConfig Load() { if (File.Exists(ConfigPath)) { try { return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig(); } catch { } } return new AppConfig(); }
        /// <summary>
        /// 原子写入配置：先写临时文件再替换，避免写一半时断电/被杀导致 JSON 损坏。
        /// 保存失败时返回 false 并带出错误信息，便于界面提示而不是静默丢失修改。
        /// </summary>
        public bool SaveChecked(out string error) {
            error = "";
            try {
                string json = JsonSerializer.Serialize(this);
                string tempPath = ConfigPath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, ConfigPath, true);
                return true;
            } catch (Exception ex) {
                error = ex.Message;
                return false;
            }
        }
        public void Save() { SaveChecked(out _); }

        /// <summary>
        /// 在旧版遗留的大量同显示器别名曲线中，找出“唯一不同”的那条（最近一次编辑）。
        /// 只有当别名数量较多（≥5）且恰好存在一条唯一曲线时才判定，
        /// 避免把两台同型号显示器的正常差异误判为编辑。
        /// </summary>
        public Dictionary<int, int>? FindOutlierCurve(string nameHash)
        {
            string prefix = $"DDC_{nameHash}_";
            var groups = new Dictionary<string, (Dictionary<int, int> Curve, int Count)>();
            int total = 0;

            foreach (var kv in Curves) {
                if (!kv.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var curve = kv.Value;
                if (curve == null || curve.Count == 0) continue;
                total++;
                string signature = string.Join(";", curve.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value}"));
                if (groups.TryGetValue(signature, out var group))
                    groups[signature] = (group.Curve, group.Count + 1);
                else
                    groups[signature] = (curve, 1);
            }

            if (total < 5 || groups.Count != 2) return null;

            var singles = groups.Values.Where(g => g.Count == 1).ToList();
            if (singles.Count != 1) return null;

            // 另一组必须是有多条的“多数派”，否则无法判断哪条是编辑
            return groups.Values.Any(g => g.Count > 1) ? singles[0].Curve : null;
        }
    }
    public class MonitorInfo { public string Name { get; set; } = "Unknown"; public MonitorType Type { get; set; } public IntPtr Handle { get; set; } public string InstanceId { get; set; } = ""; public string UniqueId { get; set; } = ""; public int LastBrightness { get; set; } = 50; }
    public enum MonitorType { WMI, DDC }

    // ================== Hotkey Message Window ==================
    public class HotkeyMessageWindow : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private Dictionary<int, (uint modifiers, uint key)> _registeredHotkeys = new();
        
        public event Action<int>? HotkeyPressed;
        
        public HotkeyMessageWindow()
        {
            // Create a message-only window
            CreateHandle(new CreateParams
            {
                ExStyle = 0x80, // WS_EX_TOOLWINDOW (message-only window)
                Style = unchecked((int)0x80000000), // WS_POPUP
                Width = 0,
                Height = 0
            });
        }
        
        public bool RegisterHotkey(string hotkeyString, int hotkeyId)
        {
            if (string.IsNullOrEmpty(hotkeyString)) return false;
            
            var (modifiers, key) = ParseHotkey(hotkeyString);
            if (key == Keys.None) return false;
            
            bool result = RegisterHotKey(this.Handle, hotkeyId, modifiers, (uint)key);
            if (result)
            {
                _registeredHotkeys[hotkeyId] = (modifiers, (uint)key);
            }
            return result;
        }
        
        public void UnregisterHotkey(int hotkeyId)
        {
            UnregisterHotKey(this.Handle, hotkeyId);
            _registeredHotkeys.Remove(hotkeyId);
        }
        
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int hotkeyId = m.WParam.ToInt32();
                HotkeyPressed?.Invoke(hotkeyId);
            }
            base.WndProc(ref m);
        }
        
        private (uint, Keys) ParseHotkey(string hotkeyString)
        {
            var parts = hotkeyString.Split('+');
            uint modifiers = 0;
            Keys key = Keys.None;
            
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                switch (trimmed.ToLower())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= 0x0002; // MOD_CONTROL
                        break;
                    case "alt":
                        modifiers |= 0x0001; // MOD_ALT
                        break;
                    case "shift":
                        modifiers |= 0x0004; // MOD_SHIFT
                        break;
                    case "win":
                    case "windows":
                        modifiers |= 0x0008; // MOD_WIN
                        break;
                    default:
                        if (Enum.TryParse<Keys>(trimmed, true, out var parsedKey))
                        {
                            key = parsedKey;
                        }
                        break;
                }
            }
            
            return (modifiers, key);
        }
        
        public void Dispose()
        {
            foreach (var hotkeyId in _registeredHotkeys.Keys.ToList())
            {
                UnregisterHotkey(hotkeyId);
            }
            DestroyHandle();
        }
        
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }

    public class MouseHook { private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam); private LowLevelMouseProc _proc; private IntPtr _hookID = IntPtr.Zero; public event MouseEventHandler? MouseWheel; public MouseHook() { _proc = HookCallback; } public void Install() { _hookID = SetWindowsHookEx(14, _proc, GetModuleHandle(System.Diagnostics.Process.GetCurrentProcess().MainModule?.ModuleName ?? "user32"), 0); } public void Uninstall() { UnhookWindowsHookEx(_hookID); } private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) { if (nCode >= 0 && (int)wParam == 0x020A) { MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam); short delta = (short)((hookStruct.mouseData >> 16) & 0xffff); MouseWheel?.Invoke(this, new MouseEventArgs(MouseButtons.None, 0, hookStruct.pt.x, hookStruct.pt.y, delta)); } return CallNextHookEx(_hookID, nCode, wParam, lParam); } [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x; public int y; } [StructLayout(LayoutKind.Sequential)] private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; } [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId); [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hhk); [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam); [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr GetModuleHandle(string lpModuleName); }

    internal static class NativeMethods { 
        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData); public delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData); 
        [DllImport("dxva2.dll")] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref int pdwNumberOfPhysicalMonitors); 
        [DllImport("dxva2.dll", EntryPoint = "GetPhysicalMonitorsFromHMONITOR")] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, int dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray); 
        [DllImport("dxva2.dll")] public static extern bool SetVCPFeature(IntPtr hMonitor, byte bVCPCode, uint dwNewValue); 
        [DllImport("dxva2.dll")] public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte bVCPCode, IntPtr pvct, ref uint pdwCurrentValue, ref uint pdwMaximumValue); 
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse); 
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point p); 
        [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount); 
        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Auto)] public static extern IntPtr GetParent(IntPtr hWnd); 
        [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo); 
        [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, int wParam, int lParam); 
        [DllImport("shell32.dll", SetLastError = true)]
        public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out Rect iconLocation);
        
        // Windows 11 DWM APIs
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref Margins pMarInset);
        
        // DWM Window Attribute Constants
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        public const int DWMWA_CAPTION_COLOR = 35;
        public const int DWMWA_BORDER_COLOR = 34;
        public const int DWMWA_TEXT_COLOR = 36;
        
        // DWM Corner Preference Constants
        public const int DWMWCP_DEFAULT = 0;
        public const int DWMWCP_DONOTROUND = 1;
        public const int DWMWCP_ROUND = 2;
        public const int DWMWCP_ROUNDSMALL = 3;
        
        // DWM System Backdrop Type Constants
        public const int DWMSBT_AUTO = 0;
        public const int DWMSBT_NONE = 1;
        public const int DWMSBT_MAINWINDOW = 2;      // Mica
        public const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
        public const int DWMSBT_TABBEDWINDOW = 4;    // Tabbed
        
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int left; public int top; public int right; public int bottom; } 
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public Rect rcMonitor;
            public Rect rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] public struct PHYSICAL_MONITOR { public IntPtr hPhysicalMonitor; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription; } 
        [StructLayout(LayoutKind.Sequential)]
        public struct NOTIFYICONIDENTIFIER
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public Guid guidItem;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct Margins
        {
            public int cxLeftWidth;
            public int cxRightWidth;
            public int cyTopHeight;
            public int cyBottomHeight;
        }
        
        // Windows 11 Style Helper
        public static bool IsWindows11() => Environment.OSVersion.Version.Build >= 22000;
        
        public static void ApplyWindows11Style(Form form, bool darkMode = true, bool mica = true, int cornerPreference = DWMWCP_ROUND)
        {
            if (!IsWindows11()) return;
            
            // Apply corner preference
            int corner = cornerPreference;
            DwmSetWindowAttribute(form.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            
            // Apply dark mode
            if (darkMode)
            {
                int dark = 1;
                DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            }
            
            // Apply Mica/Acrylic backdrop
            if (mica)
            {
                int backdrop = DWMSBT_MAINWINDOW;
                DwmSetWindowAttribute(form.Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            }
        }
    }
}

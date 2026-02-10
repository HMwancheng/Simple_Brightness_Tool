using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
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
        private UnifiedOsdForm? _unifiedOsd; 
        private DateTime _lastIconHoverTime = DateTime.MinValue;
        private bool _isDebugMode = false;

        public MyCustomApplicationContext()
        {
            config = AppConfig.Load();
            config.Save();

            RefreshMonitors();
            RestoreOrReadBrightness();
            
            Application.ApplicationExit += (s, e) => { UnregisterSystemEvents(); SaveAllSettings(); };
            RegisterSystemEvents();

            contextMenu = new ContextMenuStrip();
            
            contextMenu.Items.Add("食用说明", null, (s, e) => new HelpForm().ShowDialog());
            contextMenu.Items.Add("查看本项目", null, (s, e) => OpenUrl("https://github.com/HMwancheng/Simple_Brightness_Tool"));
            contextMenu.Items.Add("查看作者主页", null, (s, e) => OpenUrl("https://github.com/HMwancheng"));
            
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("设置", null, (s, e) => ShowSettings());
            
            var debugItem = new ToolStripMenuItem("🛠 调试模式 (忽略曲线)", null, (s, e) => {
                _isDebugMode = !_isDebugMode;
                ((ToolStripMenuItem)s).Checked = _isDebugMode;
                // 切换调试模式后刷新一下，确保数值逻辑一致
                ReloadMonitorsSafe();
            });
            contextMenu.Items.Add(debugItem);
            
            contextMenu.Items.Add("重新扫描", null, (s, e) => { ReloadMonitorsSafe(); });
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("退出", null, (s, e) => {
                SaveAllSettings();
                mouseHook?.Uninstall();
                trayIcon.Visible = false;
                Application.Exit(); 
            });

            trayIcon = new NotifyIcon()
            {
                Icon = IconDrawer.DrawNativeIcon(), 
                ContextMenuStrip = contextMenu,
                Visible = true,
                Text = "HM's Simple Brightness Tool"
            };

            trayIcon.MouseMove += (s, e) => _lastIconHoverTime = DateTime.Now;
            
            trayIcon.MouseClick += (s, e) => { 
                if (e.Button == MouseButtons.Left) ShowBrightnessWindow(); 
                else if (e.Button == MouseButtons.Middle) SyncAllBrightness();
            };

            mouseHook = new MouseHook();
            mouseHook.MouseWheel += OnGlobalMouseWheel;
            mouseHook.Install();
        }

        private void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("无法打开链接: " + ex.Message); }
        }

        private void SyncAllBrightness()
        {
            if (monitors.Count == 0) return;
            int targetVal = monitors[0].LastBrightness;
            foreach (var m in monitors)
            {
                if (config.HiddenMonitors.Contains(m.UniqueId)) continue;
                m.LastBrightness = targetVal;
                ApplyBrightness(m, targetVal, true);
            }
        }

        private void RegisterSystemEvents() {
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        }
        private void UnregisterSystemEvents() {
            SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
            SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        }
        private async void SystemEvents_PowerModeChanged(object? sender, PowerModeChangedEventArgs e) {
            if (e.Mode == PowerModes.Resume) { await Task.Delay(2000); ReloadMonitorsSafe(); }
        }
        private async void SystemEvents_SessionSwitch(object? sender, SessionSwitchEventArgs e) {
            if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect) { await Task.Delay(2000); ReloadMonitorsSafe(); }
        }
        private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e) { ReloadMonitorsSafe(); }

        // 获取显示器的亮度配置值，支持新旧ID格式兼容
        private int? GetSavedBrightnessForMonitor(MonitorInfo m, int index)
        {
            // 先尝试新ID
            if (config.SavedBrightness.TryGetValue(m.UniqueId, out int val)) return val;
            
            // 尝试旧ID格式 (DDC_{hash}_IDX_{i})
            string oldId = "DDC_" + GetStableHash(m.Name) + "_IDX_" + index;
            if (config.SavedBrightness.TryGetValue(oldId, out int oldVal)) {
                // 迁移：复制到新ID
                config.SavedBrightness[m.UniqueId] = oldVal;
                return oldVal;
            }
            return null;
        }

        // 检查显示器是否被隐藏，支持新旧ID格式
        private bool IsMonitorHidden(MonitorInfo m, int index)
        {
            // 先检查新ID
            if (config.HiddenMonitors.Contains(m.UniqueId)) return true;
            
            // 检查旧ID格式
            string oldId = "DDC_" + GetStableHash(m.Name) + "_IDX_" + index;
            if (config.HiddenMonitors.Contains(oldId)) {
                // 迁移
                config.HiddenMonitors.Remove(oldId);
                config.HiddenMonitors.Add(m.UniqueId);
                return true;
            }
            return false;
        }

        // 获取显示器的曲线配置，支持新旧ID格式
        private Dictionary<int, int>? GetCurveForMonitor(MonitorInfo m, int index)
        {
            // 先尝试新ID
            if (config.Curves.TryGetValue(m.UniqueId, out var curve)) return curve;
            
            // 尝试旧ID格式
            string oldId = "DDC_" + GetStableHash(m.Name) + "_IDX_" + index;
            if (config.Curves.TryGetValue(oldId, out var oldCurve)) {
                config.Curves[m.UniqueId] = oldCurve;
                return oldCurve;
            }
            return null;
        }

        // 获取曲线配置的便捷方法（使用显示器在列表中的索引）
        private Dictionary<int, int> GetCurveForMonitorWithFallback(MonitorInfo m)
        {
            int index = monitors.IndexOf(m);
            if (index < 0) index = 0;
            
            var curve = GetCurveForMonitor(m, index);
            if (curve != null) return curve;
            
            // 返回默认曲线
            return new Dictionary<int, int> { { 0, 0 }, { 100, 100 } };
        }

        private void ReloadMonitorsSafe() {
            var form = Application.OpenForms.OfType<BrightnessForm>().FirstOrDefault();
            if (form != null && !form.IsDisposed && form.Visible) form.Invoke(new Action(() => form.Close()));
            
            if (_unifiedOsd != null && !_unifiedOsd.IsDisposed) {
                _unifiedOsd.Invoke(new Action(() => _unifiedOsd.Close()));
                _unifiedOsd = null;
            }
            foreach(var m in monitors) config.SavedBrightness[m.UniqueId] = m.LastBrightness;
            RefreshMonitors();
            int idx = 0;
            foreach(var m in monitors) {
                var savedVal = GetSavedBrightnessForMonitor(m, idx);
                if (savedVal.HasValue) m.LastBrightness = savedVal.Value;
                idx++;
            }
            Task.Run(() => ReadRealBrightness());
        }

        private void RestoreOrReadBrightness()
        {
            bool needReadHardware = false;
            int idx = 0;
            foreach (var m in monitors) {
                var savedVal = GetSavedBrightnessForMonitor(m, idx);
                if (savedVal.HasValue) m.LastBrightness = savedVal.Value;
                else needReadHardware = true;
                idx++;
            }
            if (needReadHardware) Task.Run(() => ReadRealBrightness());
        }

        private void SaveAllSettings()
        {
            foreach(var m in monitors) config.SavedBrightness[m.UniqueId] = m.LastBrightness;
            config.Save();
        }

        // [核心修复] 读取硬件亮度并反推软件值
        private void ReadRealBrightness()
        {
            foreach (var m in monitors) {
                int realHardwareVal = -1;
                
                if (m.Type == MonitorType.WMI) {
                    try {
                        var searcher = new ManagementObjectSearcher("root\\Wmi", "SELECT * FROM WmiMonitorBrightness");
                        foreach (ManagementObject obj in searcher.Get()) {
                            var val = obj["CurrentBrightness"];
                            if (val != null && int.TryParse(val.ToString(), out int pVal)) 
                                realHardwareVal = pVal;
                        }
                    } catch { }
                } else if (m.Type == MonitorType.DDC) {
                     realHardwareVal = BrightnessController.GetVCPBrightness(m.Handle);
                }

                if (realHardwareVal != -1) {
                    int finalSoftwareVal = realHardwareVal;

                    // 如果不是调试模式，且是 DDC 显示器，则需要根据曲线反推软件数值
                    // 解决：硬件20 -> 反推软件40。如果不反推，直接赋20，下次操作会基于20计算(对应硬件5)，导致亮度骤降。
                    if (!_isDebugMode && m.Type == MonitorType.DDC) {
                         var curve = GetCurveForMonitorWithFallback(m);
                         finalSoftwareVal = ReverseInterpolate(realHardwareVal, curve);
                    }

                    // [修复] 睡眠唤醒后亮度插值微小偏差问题
                    // 如果配置中有保存的亮度值，且计算值与保存值差异很小（<=2），则使用保存值
                    int idx = monitors.IndexOf(m);
                    if (idx < 0) idx = 0;
                    var savedBrightness = GetSavedBrightnessForMonitor(m, idx);
                    if (savedBrightness.HasValue) {
                        int diff = Math.Abs(finalSoftwareVal - savedBrightness.Value);
                        if (diff <= 2 && diff > 0) {
                            // 偏差很小，使用缓存值避免视觉上的亮度跳动
                            finalSoftwareVal = savedBrightness.Value;
                        }
                    }

                    m.LastBrightness = finalSoftwareVal;
                    
                    var form = Application.OpenForms.OfType<BrightnessForm>().FirstOrDefault();
                    if (form != null && !form.IsDisposed && form.Visible)
                        form.Invoke(new Action(() => form.UpdateSlider(m.UniqueId, finalSoftwareVal)));
                }
            }
        }

        private void OnGlobalMouseWheel(object? sender, MouseEventArgs e)
        {
            if (!IsMouseOverTrayArea()) return;
            if ((DateTime.Now - _lastIconHoverTime).TotalSeconds < 0.75)
            {
                _lastIconHoverTime = DateTime.Now; 
                int change = e.Delta > 0 ? config.ScrollStep : -config.ScrollStep;
                int idx = 0;
                foreach (var m in monitors)
                {
                    if (!IsMonitorHidden(m, idx))
                    {
                        int newVal = Math.Clamp(m.LastBrightness + change, 0, 100);
                        ApplyBrightness(m, newVal, true);
                    }
                    idx++;
                }
            }
        }

        private bool IsMouseOverTrayArea()
        {
            IntPtr hWnd = NativeMethods.WindowFromPoint(Cursor.Position);
            if (hWnd == IntPtr.Zero) return false;
            for (int i = 0; i < 6; i++) {
                StringBuilder sb = new StringBuilder(256);
                NativeMethods.GetClassName(hWnd, sb, 256);
                string cls = sb.ToString();
                if (cls == "TrayNotifyWnd" || cls == "NotifyIconOverflowWindow" || cls == "Shell_SecondaryTrayWnd" || cls == "Shell_TrayWnd") return true;
                hWnd = NativeMethods.GetParent(hWnd);
                if (hWnd == IntPtr.Zero) break;
            }
            return false;
        }

        public void ApplyBrightness(MonitorInfo m, int val, bool updateUi = true)
        {
            m.LastBrightness = val;
            int finalVal = val;
            if (!_isDebugMode && m.Type == MonitorType.DDC) {
                // 获取曲线配置，支持新旧ID格式兼容
                var curve = GetCurveForMonitorWithFallback(m);
                finalVal = Interpolate(val, curve);
            }
            
            if (m.Type == MonitorType.WMI) {
                Task.Run(() => BrightnessController.SetBrightnessImmediate(m, finalVal));
            } else {
                BrightnessController.SetBrightnessDebounced(m, finalVal, config.DebounceTime);
            }
            
            var form = Application.OpenForms.OfType<BrightnessForm>().FirstOrDefault();
            bool isMainWinVisible = (form != null && !form.IsDisposed && form.Visible);

            if (isMainWinVisible) {
                if (updateUi) form!.Invoke(new Action(() => form.UpdateSlider(m.UniqueId, val)));
            } else {
                ShowUnifiedOsd();
            }
        }

        private void ShowUnifiedOsd()
        {
            if (_unifiedOsd == null || _unifiedOsd.IsDisposed) {
                var visibleMonitors = new List<MonitorInfo>();
                int idx = 0;
                foreach (var m in monitors) {
                    if (!IsMonitorHidden(m, idx)) visibleMonitors.Add(m);
                    idx++;
                }
                if (visibleMonitors.Count == 0) return;
                _unifiedOsd = new UnifiedOsdForm(visibleMonitors);
            }
            
            if (_unifiedOsd.InvokeRequired) 
                _unifiedOsd.Invoke(new Action(() => _unifiedOsd.UpdateDisplay()));
            else 
                _unifiedOsd.UpdateDisplay();
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
            var form = Application.OpenForms.OfType<BrightnessForm>().FirstOrDefault();
            if (form == null || form.IsDisposed)
                form = new BrightnessForm(monitors, config, this, contextMenu);
            form.Show(); form.Activate();
            foreach(var m in monitors) form.UpdateSlider(m.UniqueId, m.LastBrightness);
        }

        private void ShowSettings() { var form = new SettingsForm(config); form.Show(); }

        // 配置迁移：将旧版ID格式的配置迁移到新版
        private void MigrateOldConfig()
        {
            bool needSave = false;
            
            // 迁移 CustomNames
            var oldCustomNames = config.CustomNames.Keys.ToList();
            foreach (var oldId in oldCustomNames) {
                if (oldId.StartsWith("DDC_") && !oldId.Contains("_H")) {
                    // 这是旧版ID格式: DDC_{hash}_IDX_{i}
                    // 需要找到对应的新显示器并迁移
                    // 由于无法直接映射，我们保留旧配置，在RefreshMonitors中处理
                }
            }
            
            // 迁移 SavedBrightness
            var oldBrightnessKeys = config.SavedBrightness.Keys.ToList();
            foreach (var oldId in oldBrightnessKeys) {
                if (oldId.StartsWith("DDC_") && !oldId.Contains("_H")) {
                    // 旧版亮度配置，暂时保留
                }
            }
            
            // 迁移 Curves
            var oldCurveKeys = config.Curves.Keys.ToList();
            foreach (var oldId in oldCurveKeys) {
                if (oldId.StartsWith("DDC_") && !oldId.Contains("_H")) {
                    // 旧版曲线配置，暂时保留
                }
            }
            
            // 迁移 HiddenMonitors
            var oldHidden = config.HiddenMonitors.ToList();
            foreach (var oldId in oldHidden) {
                if (oldId.StartsWith("DDC_") && !oldId.Contains("_H")) {
                    // 旧版隐藏配置，暂时保留
                }
            }
            
            if (needSave) config.Save();
        }

        public void RefreshMonitors()
        {
            monitors.Clear();
            try {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\Wmi", "SELECT * FROM WmiMonitorBrightness");
                foreach (ManagementObject queryObj in searcher.Get()) {
                    string id = queryObj["InstanceName"]?.ToString() ?? "UnknownWmi";
                    if (!monitors.Any(m => m.InstanceId == id)) {
                        string uniqueId = "WMI_" + GetStableHash(id);
                        string name = config.CustomNames.ContainsKey(uniqueId) ? config.CustomNames[uniqueId] : "内置屏幕";
                        monitors.Add(new MonitorInfo { Type = MonitorType.WMI, Name = name, InstanceId = id, UniqueId = uniqueId });
                    }
                }
            } catch { }
            // 使用HashSet跟踪已添加的显示器句柄，防止重复枚举
            HashSet<IntPtr> addedHandles = new HashSet<IntPtr>();
            
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.Rect lprcMonitor, IntPtr dwData) {
                int count = 0;
                NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, ref count);
                if (count > 0) {
                    var pMs = new NativeMethods.PHYSICAL_MONITOR[count];
                    if (NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, pMs)) {
                        for (int i = 0; i < pMs.Length; i++) {
                            // 检查是否已添加过此句柄（防止重复）
                            if (addedHandles.Contains(pMs[i].hPhysicalMonitor)) {
                                continue;
                            }
                            addedHandles.Add(pMs[i].hPhysicalMonitor);
                            
                            string originalName = new string(pMs[i].szPhysicalMonitorDescription).Trim('\0');
                            // 使用更稳定的唯一ID生成方法，避免同名显示器冲突
                            string uniqueId = GetMonitorUniqueId(originalName, pMs[i].hPhysicalMonitor, i);
                            // 尝试获取自定义名称：先检查新ID，再检查旧ID（向后兼容）
                            string name = originalName;
                            if (config.CustomNames.ContainsKey(uniqueId)) {
                                name = config.CustomNames[uniqueId];
                            } else {
                                // 尝试旧版ID格式，用于迁移旧配置
                                string oldUniqueId = "DDC_" + GetStableHash(originalName) + "_IDX_" + i;
                                if (config.CustomNames.ContainsKey(oldUniqueId)) {
                                    name = config.CustomNames[oldUniqueId];
                                    // 迁移：将旧配置复制到新ID
                                    config.CustomNames[uniqueId] = name;
                                    config.Save();
                                }
                            }
                            monitors.Add(new MonitorInfo { Type = MonitorType.DDC, Name = name, Handle = pMs[i].hPhysicalMonitor, UniqueId = uniqueId });
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);
        }

        private static string GetStableHash(string str) {
            ulong hash = 5381;
            foreach (char c in str) hash = ((hash << 5) + hash) + c;
            return hash.ToString();
        }

        // 生成更稳定的唯一ID，包含显示器句柄信息以避免同名显示器冲突
        private static string GetMonitorUniqueId(string originalName, IntPtr hMonitor, int index) {
            // 结合名称哈希、显示器句柄和索引，确保唯一性
            string nameHash = GetStableHash(originalName);
            // 使用显示器句柄的低32位作为额外标识
            int handleId = hMonitor.ToInt32();
            return $"DDC_{nameHash}_H{handleId}_IDX_{index}";
        }
    }

    public static class BrightnessController 
    {
        private static ConcurrentDictionary<string, CancellationTokenSource> _debounceTokens = new ConcurrentDictionary<string, CancellationTokenSource>();

        public static void SetBrightnessDebounced(MonitorInfo monitor, int level, int debounceMs) {
            if (_debounceTokens.TryGetValue(monitor.UniqueId, out CancellationTokenSource? oldCts)) { oldCts.Cancel(); oldCts.Dispose(); }
            var cts = new CancellationTokenSource(); _debounceTokens[monitor.UniqueId] = cts;
            Task.Run(async () => {
                try { await Task.Delay(debounceMs, cts.Token); SetBrightnessImmediate(monitor, level); } catch (TaskCanceledException) { }
            });
        }

        public static void SetBrightnessImmediate(MonitorInfo monitor, int level) {
            if (monitor.Type == MonitorType.WMI) { 
                try { 
                    var searcher = new ManagementObjectSearcher("root\\Wmi", "SELECT * FROM WmiMonitorBrightnessMethods"); 
                    foreach (ManagementObject m in searcher.Get()) m.InvokeMethod("WmiSetBrightness", new object[] { 1, level }); 
                } catch {} 
            } else if (monitor.Type == MonitorType.DDC) { 
                NativeMethods.SetVCPFeature(monitor.Handle, 0x10, (uint)level); 
            } 
        }

        public static void SetPowerState(MonitorInfo monitor, bool turnOn, bool useSoftwareMode) { 
            if (useSoftwareMode) {
                if (turnOn) {
                    // 改进的唤醒策略：多种方式组合唤醒
                    WakeDisplaySoftware();
                } else {
                    // 只关闭指定显示器（如果是DDC类型），否则使用系统API
                    if (monitor.Type == MonitorType.DDC) {
                        // 使用DDC/CI关闭特定显示器，而不是广播到所有窗口
                        NativeMethods.SetVCPFeature(monitor.Handle, 0xD6, 0x04u);
                    } else {
                        // WMI类型（内置屏幕）使用系统API关闭
                        NativeMethods.SendMessage(new IntPtr(0xFFFF), 0x0112, 0xF170, 2);
                    }
                }
            } else if (monitor.Type == MonitorType.DDC) {
                if (turnOn) {
                    // 改进的DDC唤醒：多次尝试和渐进式唤醒
                    WakeDisplayDDC(monitor.Handle);
                } else {
                    uint code = 0x04u; 
                    NativeMethods.SetVCPFeature(monitor.Handle, 0xD6, code); 
                }
            }
        }

        // 软件模式唤醒 - 改进的鼠标和键盘事件组合
        private static void WakeDisplaySoftware() {
            // 方法1: 鼠标微动
            NativeMethods.mouse_event(0x0001, 0, 1, 0, UIntPtr.Zero);
            Thread.Sleep(10);
            NativeMethods.mouse_event(0x0001, 0, -1, 0, UIntPtr.Zero);
            Thread.Sleep(50);
            
            // 方法2: 发送虚拟键盘事件 (VK_SHIFT)
            NativeMethods.keybd_event(0x10, 0, 0, UIntPtr.Zero);
            Thread.Sleep(10);
            NativeMethods.keybd_event(0x10, 0, 0x0002, UIntPtr.Zero);
            Thread.Sleep(50);
            
            // 方法3: 再次鼠标微动确保唤醒
            NativeMethods.mouse_event(0x0001, 1, 0, 0, UIntPtr.Zero);
            Thread.Sleep(10);
            NativeMethods.mouse_event(0x0001, -1, 0, 0, UIntPtr.Zero);
        }

        // DDC/CI 模式唤醒 - 多次尝试和渐进式唤醒策略
        private static void WakeDisplayDDC(IntPtr hMonitor) {
            // 策略: 先发送软唤醒，如果失败再尝试硬唤醒
            
            // 尝试1: 发送电源开命令 (0x01)
            NativeMethods.SetVCPFeature(hMonitor, 0xD6, 0x01u);
            Thread.Sleep(100);
            
            // 尝试2: 发送短暂黑屏后恢复 (某些显示器需要这个序列)
            NativeMethods.SetVCPFeature(hMonitor, 0xD6, 0x01u);
            Thread.Sleep(200);
            
            // 尝试3: 如果显示器支持，发送背光开启命令
            // 0xD6 = 0x01 (正常操作)
            for (int i = 0; i < 3; i++) {
                NativeMethods.SetVCPFeature(hMonitor, 0xD6, 0x01u);
                Thread.Sleep(100);
            }
            
            // 尝试4: 同时触发软件唤醒作为后备
            WakeDisplaySoftware();
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
        public bool UseSoftwarePower { get; set; } = false;
        public List<string> HiddenMonitors { get; set; } = new List<string>(); 
        public Dictionary<string, string> CustomNames { get; set; } = new Dictionary<string, string>(); 
        public Dictionary<string, Dictionary<int, int>> Curves { get; set; } = new Dictionary<string, Dictionary<int, int>>(); 
        public Dictionary<string, int> SavedBrightness { get; set; } = new Dictionary<string, int>();
        private static string ConfigPath = Path.Combine(Application.StartupPath, "HMSimpleBrightness_Config.json"); 
        public static AppConfig Load() { if (File.Exists(ConfigPath)) { try { return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig(); } catch { } } return new AppConfig(); } 
        public void Save() { try { File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this)); } catch { } } 
        public Dictionary<int, int> GetCurveForMonitor(string id) { if (Curves.ContainsKey(id)) return Curves[id]; return new Dictionary<int, int> { { 0, 0 }, { 100, 100 } }; } 
    }
    public class MonitorInfo { public string Name { get; set; } = "Unknown"; public MonitorType Type { get; set; } public IntPtr Handle { get; set; } public string InstanceId { get; set; } = ""; public string UniqueId { get; set; } = ""; public int LastBrightness { get; set; } = 50; }
    public enum MonitorType { WMI, DDC }

    public class MouseHook { private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam); private LowLevelMouseProc _proc; private IntPtr _hookID = IntPtr.Zero; public event MouseEventHandler? MouseWheel; public MouseHook() { _proc = HookCallback; } public void Install() { _hookID = SetWindowsHookEx(14, _proc, GetModuleHandle(System.Diagnostics.Process.GetCurrentProcess().MainModule?.ModuleName ?? "user32"), 0); } public void Uninstall() { UnhookWindowsHookEx(_hookID); } private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) { if (nCode >= 0 && (int)wParam == 0x020A) { MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam); short delta = (short)((hookStruct.mouseData >> 16) & 0xffff); MouseWheel?.Invoke(this, new MouseEventArgs(MouseButtons.None, 0, hookStruct.pt.x, hookStruct.pt.y, delta)); } return CallNextHookEx(_hookID, nCode, wParam, lParam); } [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x; public int y; } [StructLayout(LayoutKind.Sequential)] private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; } [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId); [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hhk); [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam); [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr GetModuleHandle(string lpModuleName); }

    internal static class NativeMethods { 
        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData); public delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData); 
        [DllImport("dxva2.dll")] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref int pdwNumberOfPhysicalMonitors); 
        [DllImport("dxva2.dll", EntryPoint = "GetPhysicalMonitorsFromHMONITOR")] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, int dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray); 
        [DllImport("dxva2.dll")] public static extern bool SetVCPFeature(IntPtr hMonitor, byte bVCPCode, uint dwNewValue); 
        [DllImport("dxva2.dll")] public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte bVCPCode, IntPtr pvct, ref uint pdwCurrentValue, ref uint pdwMaximumValue); 
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse); 
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point p); 
        [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount); 
        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Auto)] public static extern IntPtr GetParent(IntPtr hWnd); 
        [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo); 
        [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, int wParam, int lParam); 
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int left; public int top; public int right; public int bottom; } 
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] public struct PHYSICAL_MONITOR { public IntPtr hPhysicalMonitor; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription; } 
    }
}

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text; // <--- 关键修复：添加这行以支持 StringBuilder
using System.Threading.Tasks;
using System.Windows.Forms;
using SimpleBrightness.Model;
using SimpleBrightness.UI;
using SimpleBrightness.Utils;

namespace SimpleBrightness.Core
{
    public class MyCustomApplicationContext : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip contextMenu;
        private BrightnessForm? brightnessWindow;
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
            contextMenu.Items.Add("关于 & 说明", null, (s, e) => new HelpForm().ShowDialog());
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("设置", null, (s, e) => ShowSettings());
            
            var debugItem = new ToolStripMenuItem("🛠 调试模式 (忽略曲线)", null, (s, e) => {
                _isDebugMode = !_isDebugMode;
                ((ToolStripMenuItem)s).Checked = _isDebugMode;
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
                // 确保这里调用的是 DrawSunIcon
                Icon = IconDrawer.DrawSunIcon(), 
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

        private void ReloadMonitorsSafe() {
            if (brightnessWindow != null && !brightnessWindow.IsDisposed && brightnessWindow.Visible) {
                brightnessWindow.Invoke(new Action(() => brightnessWindow.Close()));
                brightnessWindow = null;
            }
            if (_unifiedOsd != null && !_unifiedOsd.IsDisposed) {
                _unifiedOsd.Invoke(new Action(() => _unifiedOsd.Close()));
                _unifiedOsd = null;
            }
            foreach(var m in monitors) config.SavedBrightness[m.UniqueId] = m.LastBrightness;
            RefreshMonitors();
            foreach(var m in monitors) {
                if (config.SavedBrightness.ContainsKey(m.UniqueId)) m.LastBrightness = config.SavedBrightness[m.UniqueId];
            }
            Task.Run(() => ReadRealBrightness());
        }

        private void RestoreOrReadBrightness()
        {
            bool needReadHardware = false;
            foreach (var m in monitors) {
                if (config.SavedBrightness.ContainsKey(m.UniqueId)) {
                    m.LastBrightness = config.SavedBrightness[m.UniqueId];
                } else {
                    needReadHardware = true;
                }
            }
            if (needReadHardware) Task.Run(() => ReadRealBrightness());
        }

        private void SaveAllSettings()
        {
            foreach(var m in monitors) config.SavedBrightness[m.UniqueId] = m.LastBrightness;
            config.Save();
        }

        private void ReadRealBrightness()
        {
            foreach (var m in monitors) {
                int realVal = -1;
                if (m.Type == MonitorType.WMI) {
                    try {
                        var searcher = new ManagementObjectSearcher("root\\Wmi", "SELECT * FROM WmiMonitorBrightness");
                        foreach (ManagementObject obj in searcher.Get()) {
                            var val = obj["CurrentBrightness"];
                            if (val != null) realVal = int.Parse(val.ToString() ?? "50");
                        }
                    } catch { }
                } else if (m.Type == MonitorType.DDC) {
                     realVal = BrightnessController.GetVCPBrightness(m.Handle);
                }
                if (realVal != -1) {
                    m.LastBrightness = realVal;
                    if (brightnessWindow != null && brightnessWindow.Visible)
                        brightnessWindow.Invoke(new Action(() => brightnessWindow.UpdateSlider(m.UniqueId, realVal)));
                }
            }
        }

        private void OnGlobalMouseWheel(object? sender, MouseEventArgs e)
        {
            if (!IsMouseOverTrayArea()) return;

            // 超时检测 0.75s
            if ((DateTime.Now - _lastIconHoverTime).TotalSeconds < 0.75)
            {
                _lastIconHoverTime = DateTime.Now; 
                int change = e.Delta > 0 ? config.ScrollStep : -config.ScrollStep;
                foreach (var m in monitors.Where(x => !config.HiddenMonitors.Contains(x.UniqueId)))
                {
                    int newVal = Math.Clamp(m.LastBrightness + change, 0, 100);
                    ApplyBrightness(m, newVal, true); 
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
                var curve = config.GetCurveForMonitor(m.UniqueId);
                finalVal = Interpolate(val, curve);
            }
            
            if (m.Type == MonitorType.WMI) {
                Task.Run(() => BrightnessController.SetBrightnessImmediate(m, finalVal));
            } else {
                BrightnessController.SetBrightnessDebounced(m, finalVal, config.DebounceTime);
            }
            
            ShowUnifiedOsd();
            
            if (updateUi && brightnessWindow != null && !brightnessWindow.IsDisposed && brightnessWindow.Visible) {
                brightnessWindow.Invoke(new Action(() => brightnessWindow.UpdateSlider(m.UniqueId, val)));
            }
        }

        private void ShowUnifiedOsd()
        {
            if (_unifiedOsd == null || _unifiedOsd.IsDisposed) {
                var visibleMonitors = monitors.Where(x => !config.HiddenMonitors.Contains(x.UniqueId)).ToList();
                if (visibleMonitors.Count == 0) return;
                _unifiedOsd = new UnifiedOsdForm(visibleMonitors);
            }
            
            if (_unifiedOsd.InvokeRequired) 
                _unifiedOsd.Invoke(new Action(() => _unifiedOsd.UpdateDisplay()));
            else 
                _unifiedOsd.UpdateDisplay();
        }

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

        private void ShowBrightnessWindow() {
            if (brightnessWindow == null || brightnessWindow.IsDisposed)
                brightnessWindow = new BrightnessForm(monitors, config, this, contextMenu);
            brightnessWindow.Show();
            brightnessWindow.Activate();
            foreach(var m in monitors) brightnessWindow.UpdateSlider(m.UniqueId, m.LastBrightness);
        }

        private void ShowSettings() { var form = new SettingsForm(config); form.Show(); }
    }
}

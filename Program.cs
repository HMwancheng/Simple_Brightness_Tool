using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

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

            using (Mutex mutex = new Mutex(false, "Global\\" + "HMSimpleBrightness_v36_NativeUI"))
            {
                if (!mutex.WaitOne(0, false)) return;
                ApplicationConfiguration.Initialize();
                Application.Run(new MyCustomApplicationContext());
            }
        }
    }

    // ================== 1. 核心上下文 ==================
    public class MyCustomApplicationContext : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip contextMenu;
        private BrightnessForm? brightnessWindow;
        private List<MonitorInfo> monitors = new List<MonitorInfo>();
        private AppConfig config;
        private MouseHook mouseHook;
        // 统一 OSD 实例
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
            contextMenu.Items.Add("软件使用说明", null, (s, e) => new HelpForm().ShowDialog());
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

            // 超时检测
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
            
            // 显示 OSD
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
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.Rect lprcMonitor, IntPtr dwData) {
                int count = 0;
                NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, ref count);
                if (count > 0) {
                    var pMs = new NativeMethods.PHYSICAL_MONITOR[count];
                    if (NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, pMs)) {
                        for (int i = 0; i < pMs.Length; i++) {
                            string originalName = new string(pMs[i].szPhysicalMonitorDescription).Trim('\0');
                            string uniqueId = "DDC_" + GetStableHash(originalName) + "_IDX_" + i;
                            string name = config.CustomNames.ContainsKey(uniqueId) ? config.CustomNames[uniqueId] : originalName;
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
    }

    // ================== 2. 图标 (System Style + Offset) ==================
    public static class IconDrawer { 
        public static Icon DrawNativeIcon() { 
            using (Bitmap bmp = new Bitmap(32, 32)) using (Graphics g = Graphics.FromImage(bmp)) { 
                g.SmoothingMode = SmoothingMode.AntiAlias; 
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                Font iconFont = new Font("Segoe MDL2 Assets", 18, FontStyle.Regular);
                // X:-7, Y:-1 (Centered for Win10/11 Tray)
                TextRenderer.DrawText(g, "\uE706", iconFont, new Point(-7, -1), Color.White);
                return Icon.FromHandle(bmp.GetHicon()); 
            } 
        } 
    }

    // ================== 3. UnifiedOsdForm (Visual Upgrade) ==================
    public class UnifiedOsdForm : Form
    {
        private System.Windows.Forms.Timer _timer;
        private List<MonitorInfo> _monitors;

        public UnifiedOsdForm(List<MonitorInfo> monitors)
        {
            _monitors = monitors;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.BackColor = Color.FromArgb(32, 32, 32); // 纯正深色背景
            this.DoubleBuffered = true;
            this.StartPosition = FormStartPosition.Manual;
            this.Padding = new Padding(15);
            
            // 自动计算高度
            int rowHeight = 50; 
            int totalHeight = 30 + (monitors.Count * rowHeight);
            this.Size = new Size(340, totalHeight);
            
            // 圆角
            this.Region = Region.FromHrgn(NativeMethods.CreateRoundRectRgn(0, 0, Width, Height, 12, 12));
            
            _timer = new System.Windows.Forms.Timer { Interval = 1500 };
            _timer.Tick += (s, e) => this.Hide();
        }

        protected override bool ShowWithoutActivation => true;

        public void UpdateDisplay()
        {
            // 始终显示在鼠标所在屏幕底部
            var screen = Screen.FromPoint(Cursor.Position);
            this.Location = new Point(screen.Bounds.X + (screen.Bounds.Width - Width) / 2, screen.Bounds.Bottom - this.Height - 100);
            
            this.Show();
            this.Refresh();
            _timer.Stop();
            _timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // 绘制逻辑：垂直居中，视觉对齐
            int rowHeight = 50;
            int startY = 15;

            foreach (var m in _monitors)
            {
                int centerY = startY + (rowHeight / 2);
                
                // 1. 名字 (左侧)
                string name = m.Name.Length > 12 ? m.Name.Substring(0, 12) + ".." : m.Name;
                Size nameSize = TextRenderer.MeasureText(name, new Font("Segoe UI", 10));
                Point namePos = new Point(20, centerY - (nameSize.Height / 2));
                TextRenderer.DrawText(e.Graphics, name, new Font("Segoe UI", 10), namePos, Color.White);
                
                // 2. 进度条背景 (中间)
                int barX = 140;
                int barY = centerY - 2; // 高度4的一半
                Rectangle bgRect = new Rectangle(barX, barY, 130, 4); // 细条
                using (var b = new SolidBrush(Color.FromArgb(80, 80, 80))) e.Graphics.FillRectangle(b, bgRect);

                // 3. 进度条前景 (白色)
                int w = (int)(130 * (m.LastBrightness / 100.0));
                if (w > 0) using (var b = new SolidBrush(Color.White)) e.Graphics.FillRectangle(b, barX, barY, w, 4);

                // 4. 数值 (右侧)
                string valStr = $"{m.LastBrightness}";
                Size valSize = TextRenderer.MeasureText(valStr, new Font("Segoe UI", 10, FontStyle.Bold));
                Point valPos = new Point(290, centerY - (valSize.Height / 2));
                TextRenderer.DrawText(e.Graphics, valStr, new Font("Segoe UI", 10, FontStyle.Bold), valPos, Color.White);

                startY += rowHeight;
            }
        }
    }

    // ================== 4. Controlling Logic ==================
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
                    NativeMethods.mouse_event(0x0001, 0, 1, 0, UIntPtr.Zero);
                    Thread.Sleep(10);
                    NativeMethods.mouse_event(0x0001, 0, -1, 0, UIntPtr.Zero);
                } else {
                    NativeMethods.SendMessage(new IntPtr(0xFFFF), 0x0112, 0xF170, 2);
                }
            } else if (monitor.Type == MonitorType.DDC) {
                uint code = turnOn ? 0x01u : 0x04u; 
                NativeMethods.SetVCPFeature(monitor.Handle, 0xD6, code); 
            }
        } 

        public static int GetVCPBrightness(IntPtr hMonitor) { 
            uint current = 50, max = 100; 
            if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(hMonitor, 0x10, IntPtr.Zero, ref current, ref max)) return (int)current; 
            return -1; 
        } 
    }

    // ================== UI Classes ==================
    public class HelpForm : Form {
        public HelpForm() {
            this.Text = "关于 & 说明"; this.Size = new Size(520, 480); this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog; this.MaximizeBox = false; this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(31, 31, 31); this.ForeColor = Color.White;
            Label title = new Label { Text = "HM's Simple Brightness Tool", Top = 20, Left = 20, AutoSize = true, Font = new Font("Segoe UI", 14, FontStyle.Bold) };
            TextBox info = new TextBox { 
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Top = 60, Left = 20, Width = 460, Height = 320,
                BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.LightGray, BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9),
                Text = 
@"HM's Simple Brightness Tool

一个极致轻量、便携、现代化的 Windows 屏幕亮度控制工具。
专为解决多显示器亮度管理难题而生，拒绝臃肿，专注于“顺手”与“精准”。

【核心功能】
1. 极速调节：鼠标悬停任务栏托盘图标，滚动滚轮即可调节。
   (移出图标区域调节立即停止，防止误触)
2. 中键同步：对着托盘图标点击【中键】，强制将所有屏幕亮度同步为主屏数值。

【独家：非线性曲线】
在控制中心点击“编辑曲线”，可自定义亮度映射。
解决副屏“调到10%太暗，调到20%又太亮”的问题。
示例：设置软件50%对应硬件20%，实现极致微调。

【设置说明】
- 调节响应延迟：针对老旧显示器，调高此值可防止卡顿丢包（防抖动）。
- 电源按钮模式：
  DDC/CI：硬件指令，彻底断电（部分显示器唤不醒）。
  Windows API：软件黑屏信号，兼容性最好（推荐笔记本外接使用）。"
            };
            info.SelectionLength = 0;
            Button btnOk = new Button { Text = "关闭", Top = 400, Left = 380, Width = 100, Height = 35, BackColor = Color.Teal, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.OK };
            this.Controls.Add(title); this.Controls.Add(info); this.Controls.Add(btnOk);
        }
    }

    public class SettingsForm : Form { 
        public SettingsForm(AppConfig config) { 
            this.Text = "设置"; this.Size = new Size(350, 480); 
            this.StartPosition = FormStartPosition.CenterScreen; this.FormBorderStyle = FormBorderStyle.FixedDialog; this.MaximizeBox = false; 
            int y = 30; 
            
            CheckBox chkAuto = new CheckBox { Text = "开机自动启动", Top = y, Left = 30, Width = 250, Checked = IsAutoStart(), Font = new Font("Microsoft YaHei UI", 9) }; 
            this.Controls.Add(chkAuto); y += 60; 
            
            Label lblStep = new Label { Text = "滚轮步长 (1-20):", Top = y, Left = 30, AutoSize = true, Font = new Font("Microsoft YaHei UI", 9) }; 
            NumericUpDown numStep = new NumericUpDown { Minimum = 1, Maximum = 20, Top = y + 25, Left = 30, Width = 100 };
            numStep.Value = Math.Clamp(config.ScrollStep, 1, 20); 
            this.Controls.Add(lblStep); this.Controls.Add(numStep); y += 80; 
            
            Label lblDelay = new Label { Text = "调节响应延迟 (防卡顿 ms):", Top = y, Left = 30, AutoSize = true, Font = new Font("Microsoft YaHei UI", 9) }; 
            NumericUpDown numDelay = new NumericUpDown { Minimum = 0, Maximum = 2000, Top = y + 25, Left = 30, Width = 100 };
            numDelay.Value = Math.Clamp(config.DebounceTime, 0, 2000); 
            this.Controls.Add(lblDelay); this.Controls.Add(numDelay); y += 80;

            Label lblPower = new Label { Text = "电源按钮模式:", Top = y, Left = 30, AutoSize = true, Font = new Font("Microsoft YaHei UI", 9) };
            ComboBox cmbPower = new ComboBox { Top = y + 25, Left = 30, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbPower.Items.Add("DDC/CI (硬件指令 - 推荐)");
            cmbPower.Items.Add("Windows API (软件信号 - 兼容)");
            cmbPower.SelectedIndex = config.UseSoftwarePower ? 1 : 0;
            this.Controls.Add(lblPower); this.Controls.Add(cmbPower); y += 80;

            Button btnClearHidden = new Button { Text = $"重置隐藏显示器 ({config.HiddenMonitors.Count})", Top = y, Left = 30, Width = 280, Height = 40 }; 
            btnClearHidden.Click += (s, e) => { config.HiddenMonitors.Clear(); MessageBox.Show("已重置，请重新扫描或重启软件。", "提示"); }; 
            this.Controls.Add(btnClearHidden); y += 80; 
            
            Button btnOk = new Button { Text = "保存设置", Top = y, Left = 110, Width = 100, Height = 40, DialogResult = DialogResult.OK, BackColor = Color.Teal, ForeColor = Color.White, FlatStyle = FlatStyle.Flat }; 
            btnOk.Click += (s, e) => { 
                config.ScrollStep = (int)numStep.Value; 
                config.DebounceTime = (int)numDelay.Value; 
                config.UseSoftwarePower = (cmbPower.SelectedIndex == 1);
                SetAutoStart(chkAuto.Checked); 
                this.Close(); 
            }; 
            this.Controls.Add(btnOk); 
        } 
        private bool IsAutoStart() { using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false)) return key?.GetValue("SimpleBrightness") != null; } 
        private void SetAutoStart(bool enable) { using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)) { if (enable) key?.SetValue("SimpleBrightness", Application.ExecutablePath); else key?.DeleteValue("SimpleBrightness", false); } } 
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

    // ================== MouseHook ==================
    public class MouseHook { private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam); private LowLevelMouseProc _proc; private IntPtr _hookID = IntPtr.Zero; public event MouseEventHandler? MouseWheel; public MouseHook() { _proc = HookCallback; } public void Install() { _hookID = SetWindowsHookEx(14, _proc, GetModuleHandle(System.Diagnostics.Process.GetCurrentProcess().MainModule?.ModuleName ?? "user32"), 0); } public void Uninstall() { UnhookWindowsHookEx(_hookID); } private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) { if (nCode >= 0 && (int)wParam == 0x020A) { MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam); short delta = (short)((hookStruct.mouseData >> 16) & 0xffff); MouseWheel?.Invoke(this, new MouseEventArgs(MouseButtons.None, 0, hookStruct.pt.x, hookStruct.pt.y, delta)); } return CallNextHookEx(_hookID, nCode, wParam, lParam); } [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x; public int y; } [StructLayout(LayoutKind.Sequential)] private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; } [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId); [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hhk); [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam); [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr GetModuleHandle(string lpModuleName); }
    internal static class NativeMethods { [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData); public delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData); [DllImport("dxva2.dll")] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref int pdwNumberOfPhysicalMonitors); [DllImport("dxva2.dll", EntryPoint = "GetPhysicalMonitorsFromHMONITOR")] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, int dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray); [DllImport("dxva2.dll")] public static extern bool SetVCPFeature(IntPtr hMonitor, byte bVCPCode, uint dwNewValue); [DllImport("dxva2.dll")] public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte bVCPCode, IntPtr pvct, ref uint pdwCurrentValue, ref uint pdwMaximumValue); [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse); [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point p); [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount); [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Auto)] public static extern IntPtr GetParent(IntPtr hWnd); [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo); [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, int wParam, int lParam); [StructLayout(LayoutKind.Sequential)] public struct Rect { public int left; public int top; public int right; public int bottom; } [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] public struct PHYSICAL_MONITOR { public IntPtr hPhysicalMonitor; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription; } }
    
    // ================== BrightnessForm (Main UI) ==================
    public class BrightnessForm : Form
    {
        private List<MonitorInfo> _monitors; private AppConfig _config; private MyCustomApplicationContext _context; private Dictionary<string, TrackBar> _sliders = new Dictionary<string, TrackBar>(); private Dictionary<string, Label> _valLabels = new Dictionary<string, Label>(); private FlowLayoutPanel _mainPanel;
        public BrightnessForm(List<MonitorInfo> monitors, AppConfig config, MyCustomApplicationContext context, ContextMenuStrip menu) {
            _monitors = monitors; _config = config; _context = context; this.FormBorderStyle = FormBorderStyle.None; this.ShowInTaskbar = false; this.BackColor = Color.FromArgb(31, 31, 31); this.StartPosition = FormStartPosition.Manual; this.TopMost = true; this.AutoSize = true; this.AutoSizeMode = AutoSizeMode.GrowAndShrink; this.Padding = new Padding(2);
            this.Deactivate += (s, e) => { if (Application.OpenForms.OfType<CurveEditorForm>().Any() || Application.OpenForms.OfType<SettingsForm>().Any() || Application.OpenForms.OfType<InputBox>().Any() || Application.OpenForms.OfType<HelpForm>().Any()) return; if (!this.Bounds.Contains(Cursor.Position)) this.Hide(); else this.Activate(); };
            _mainPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10), BackColor = Color.FromArgb(31, 31, 31), MaximumSize = new Size(500, 2000) }; this.Controls.Add(_mainPanel);
            Panel header = new Panel { Size = new Size(450, 50), Margin = new Padding(0, 0, 0, 5) }; Label title = new Label { Text = "Control Center", Location = new Point(5, 10), ForeColor = Color.White, Font = new Font("Segoe UI", 14, FontStyle.Bold), AutoSize = true }; Button btnMenu = new Button { Text = "☰", Location = new Point(410, 5), Size = new Size(35, 35), FlatStyle = FlatStyle.Flat, ForeColor = Color.White, Cursor = Cursors.Hand }; btnMenu.FlatAppearance.BorderSize = 0; btnMenu.Click += (s, e) => menu.Show(Cursor.Position); header.Controls.Add(title); header.Controls.Add(btnMenu); _mainPanel.Controls.Add(header);
            var visibleMonitors = _monitors.Where(m => !_config.HiddenMonitors.Contains(m.UniqueId)).ToList(); if (visibleMonitors.Count == 0) visibleMonitors = _monitors; 
            foreach (var m in visibleMonitors) {
                FlowLayoutPanel card = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Width = 450, Padding = new Padding(0, 5, 0, 10), Margin = new Padding(0, 0, 0, 10) };
                Panel row1 = new Panel { Size = new Size(440, 30), Margin = new Padding(5, 0, 5, 5) }; Label lblName = new Label { Text = m.Name, Location = new Point(0, 0), ForeColor = Color.LightGray, AutoSize = true, Font = new Font("Segoe UI", 10), Cursor = Cursors.Hand, MaximumSize = new Size(350, 30) }; SetupNameMenu(lblName, m); Label lblVal = new Label { Text = $"{m.LastBrightness}%", Location = new Point(380, 0), ForeColor = Color.Cyan, AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold) }; _valLabels[m.UniqueId] = lblVal; row1.Controls.Add(lblName); row1.Controls.Add(lblVal); card.Controls.Add(row1);
                TrackBar slider = new TrackBar { Size = new Size(440, 45), Maximum = 100, Minimum = 0, Value = m.LastBrightness, TickStyle = TickStyle.None, Cursor = Cursors.Hand, Margin = new Padding(0) }; _sliders[m.UniqueId] = slider; Action<int> updateLogic = (newVal) => { lblVal.Text = $"{newVal}%"; _context.ApplyBrightness(m, newVal, false); }; slider.Scroll += (s, e) => updateLogic(slider.Value); slider.MouseWheel += (s, e) => { int change = e.Delta > 0 ? _config.ScrollStep : -_config.ScrollStep; slider.Value = Math.Clamp(slider.Value + change, 0, 100); updateLogic(slider.Value); ((HandledMouseEventArgs)e).Handled = true; }; card.Controls.Add(slider);
                if (m.Type == MonitorType.DDC) { 
                    FlowLayoutPanel btnRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(10, 5, 0, 0) }; 
                    Button btnCurve = CreateModernButton("编辑曲线", 110); 
                    btnCurve.Click += (s, e) => { var editor = new CurveEditorForm(m, _config); editor.Show(this); }; 
                    btnRow.Controls.Add(btnCurve); 
                    Button btnPower = CreateModernButton("⏻ 电源", 90); 
                    btnPower.ForeColor = Color.LightGreen; 
                    btnPower.MouseDown += (s, e) => { Task.Run(() => BrightnessController.SetPowerState(m, e.Button == MouseButtons.Left, _config.UseSoftwarePower)); }; 
                    ToolTip tip = new ToolTip(); tip.SetToolTip(btnPower, "左键：开启 (On)\n右键：关闭 (Off)");
                    btnRow.Controls.Add(btnPower); 
                    card.Controls.Add(btnRow); 
                }
                if (m != visibleMonitors.Last()) { Panel div = new Panel { Size = new Size(440, 1), BackColor = Color.FromArgb(50, 50, 50), Margin = new Padding(5, 15, 5, 0) }; card.Controls.Add(div); } _mainPanel.Controls.Add(card);
            }
        }
        protected override void OnLoad(EventArgs e) { base.OnLoad(e); var screen = Screen.FromPoint(Cursor.Position); int x = screen.WorkingArea.Right - this.Width - 2; int y = screen.WorkingArea.Bottom - this.Height - 2; if (y < screen.WorkingArea.Top) y = screen.WorkingArea.Top + 50; if (x < screen.WorkingArea.Left) x = screen.WorkingArea.Left + 10; this.Location = new Point(x, y); this.Region = Region.FromHrgn(NativeMethods.CreateRoundRectRgn(0, 0, Width, Height, 18, 18)); }
        private void SetupNameMenu(Label lbl, MonitorInfo m) { ContextMenuStrip menu = new ContextMenuStrip(); menu.Items.Add("重命名", null, (s, e) => { InputBox input = new InputBox("重命名", "输入新名称:", m.Name); if (input.ShowDialog() == DialogResult.OK) { _config.CustomNames[m.UniqueId] = input.ResultText; _config.Save(); _context.RefreshMonitors(); this.Close(); } }); menu.Items.Add("隐藏", null, (s, e) => { _config.HiddenMonitors.Add(m.UniqueId); _config.Save(); this.Close(); }); lbl.MouseClick += (s, e) => { if (e.Button == MouseButtons.Right) menu.Show(Cursor.Position); }; }
        private Button CreateModernButton(string text, int width) { return new Button { Text = text, Size = new Size(width, 38), FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(50, 50, 50), Cursor = Cursors.Hand, Font = new Font("Segoe UI", 9), Margin = new Padding(0, 0, 10, 0) }; }
        public void UpdateSlider(string id, int val) { if (_sliders.ContainsKey(id)) { _sliders[id].Value = val; _valLabels[id].Text = val + "%"; } }
    }

    // ================== 7. CurveEditorForm ==================
    public class CurveEditorForm : Form
    {
        private MonitorInfo _monitor; private AppConfig _config; private Dictionary<int, int> _currentPoints; private FlowLayoutPanel _panel;
        public CurveEditorForm(MonitorInfo monitor, AppConfig config) {
            _monitor = monitor; _config = config; _currentPoints = new Dictionary<int, int>(config.GetCurveForMonitor(monitor.UniqueId));
            this.Size = new Size(850, 500); this.BackColor = Color.FromArgb(31, 31, 31); this.StartPosition = FormStartPosition.CenterScreen; this.Text = "Curve Editor";
            
            Panel top = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(25, 25, 25) };
            Label title = new Label { Text = $"编辑: {monitor.Name}", Location = new Point(15, 18), AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
            
            NumericUpDown num = new NumericUpDown { Value = 50, Width = 80, Location = new Point(350, 13), Font = new Font("Segoe UI", 10) };
            Button add = new Button { Text = "添加节点", Width = 110, Height = 34, Location = new Point(440, 13), BackColor = Color.Gray, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            Button save = new Button { Text = "保存并生效", Width = 120, Height = 34, Location = new Point(700, 13), BackColor = Color.Teal, ForeColor = Color.White, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat };
            
            add.Click += (s, e) => { int x = (int)num.Value; if (!_currentPoints.ContainsKey(x)) { _currentPoints[x] = x; RefreshSliders(); }};
            save.Click += (s, e) => { _config.Curves[_monitor.UniqueId] = new Dictionary<int, int>(_currentPoints); _config.Save(); this.Close(); };
            
            top.Controls.AddRange(new Control[] { title, num, add, save });
            this.Controls.Add(top);
            
            _panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(20, 10, 0, 0) };
            this.Controls.Add(_panel); 
            _panel.BringToFront();

            RefreshSliders();
        }
        
        private void RefreshSliders() { _panel.Controls.Clear(); if (!_currentPoints.ContainsKey(0)) _currentPoints[0]=0; if(!_currentPoints.ContainsKey(100)) _currentPoints[100]=100; foreach(var k in _currentPoints.Keys.OrderBy(x=>x)) _panel.Controls.Add(CreateItem(k, _currentPoints[k])); }
        
        private Control CreateItem(int x, int y) { 
             Panel p = new Panel { Width = 70, Height = 320, Margin = new Padding(8), BackColor = Color.FromArgb(45,45,45) };
             Label l = new Label { Text = y.ToString(), Top = 5, Width = 70, Height = 25, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Cyan, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
             int panelH = 320; int labelH = 20; int btnH = 20; 
             Label k = new Label { Text = x + "%", Top = panelH - labelH - btnH - 5, Width = 70, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new Font("Segoe UI", 9) };
             Control bottomCtrl;
             if (x != 0 && x != 100) {
                 Button d = new Button { Text = "×", Top = panelH - btnH - 5, Left = 20, Width = 30, Height = 20, ForeColor = Color.Red, FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent }; d.FlatAppearance.BorderSize = 0;
                 d.Click += (s, e) => { _currentPoints.Remove(x); RefreshSliders(); }; bottomCtrl = d;
             } else {
                 Label lockLbl = new Label { Text = "🔒", Top = panelH - btnH - 5, Width = 70, Height = 20, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8) }; bottomCtrl = lockLbl;
             }
             int sliderTop = 35; int sliderH = (panelH - labelH - btnH - 5) - sliderTop - 5;
             TrackBar t = new TrackBar { Orientation = Orientation.Vertical, Minimum = 0, Maximum = 100, Value = y, Top = sliderTop, Height = sliderH, Width = 45, Left = 12, TickStyle = TickStyle.None };
             ToolTip tip = new ToolTip(); 
             t.Scroll += (s, e) => { _currentPoints[x] = t.Value; l.Text = t.Value.ToString(); tip.SetToolTip(t, t.Value.ToString()); };
             t.MouseWheel += (s, e) => { int change = e.Delta > 0 ? 1 : -1; t.Value = Math.Clamp(t.Value + change, 0, 100); _currentPoints[x] = t.Value; l.Text = t.Value.ToString(); ((HandledMouseEventArgs)e).Handled = true; };
             p.Controls.AddRange(new Control[]{l, t, k, bottomCtrl}); 
             return p;
        }
    }

    // ================== 9. InputBox ==================
    public class InputBox : Form { public string ResultText { get; private set; } = ""; public InputBox(string title, string prompt, string defaultText) { this.Size = new Size(300, 180); this.Text = title; this.StartPosition = FormStartPosition.CenterScreen; this.FormBorderStyle = FormBorderStyle.FixedDialog; Label l = new Label { Text = prompt, Top = 20, Left = 20, AutoSize = true }; TextBox t = new TextBox { Text = defaultText, Top = 50, Left = 20, Width = 240 }; Button b = new Button { Text = "确定", Top = 90, Left = 180, DialogResult = DialogResult.OK }; b.Click += (s, e) => { ResultText = t.Text; this.Close(); }; this.Controls.AddRange(new Control[] { l, t, b }); this.AcceptButton = b; } }
}

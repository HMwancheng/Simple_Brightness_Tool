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

            using (Mutex mutex = new Mutex(false, "Global\\" + "HMSimpleBrightness_v32_LayoutPerfect"))
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
        private Dictionary<string, OsdForm> osdForms = new Dictionary<string, OsdForm>();
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
                Icon = IconDrawer.DrawNativeIcon(), // 系统原生风格
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
            
            ShowMonitorOsd(m, val, finalVal);
            if (updateUi && brightnessWindow != null && !brightnessWindow.IsDisposed && brightnessWindow.Visible) {
                brightnessWindow.Invoke(new Action(() => brightnessWindow.UpdateSlider(m.UniqueId, val)));
            }
        }

        private void ShowMonitorOsd(MonitorInfo m, int uiVal, int realVal)
        {
            if (!osdForms.ContainsKey(m.UniqueId) || osdForms[m.UniqueId].IsDisposed) osdForms[m.UniqueId] = new OsdForm(m.Name);
            var osd = osdForms[m.UniqueId];
            osd.UpdateName(m.Name);

            Screen targetScreen = Screen.FromHandle(m.Handle);
            if (targetScreen == null) targetScreen = Screen.PrimaryScreen ?? Screen.AllScreens[0];

            int stackIndex = 0;
            foreach (var kvp in osdForms) {
                if (kvp.Key == m.UniqueId) continue;
                if (kvp.Value.Visible && Screen.FromPoint(kvp.Value.Location).DeviceName == targetScreen.DeviceName) {
                    stackIndex++;
                }
            }

            int x = targetScreen.Bounds.X + (targetScreen.Bounds.Width - osd.Width) / 2;
            int y = targetScreen.Bounds.Bottom - 150 - (stackIndex * 70);

            if (osd.InvokeRequired) osd.Invoke(new Action(() => osd.ShowOSD(uiVal, _isDebugMode, realVal, x, y))); 
            else osd.ShowOSD(uiVal, _isDebugMode, realVal, x, y);
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
                // 偏移量：X=-7, Y=-1，确保居中
                TextRenderer.DrawText(g, "\uE706", iconFont, new Point(-7, -1), Color.White);
                return Icon.FromHandle(bmp.GetHicon()); 
            } 
        } 
    }

    // ================== 3. 控制器 ==================
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
                // 逻辑保持：Left Click=False(关), Right Click=True(开)
                uint code = turnOn ? 0x04u : 0x01u; 
                NativeMethods.SetVCPFeature(monitor.Handle, 0xD6, code); 
            }
        } 

        public static int GetVCPBrightness(IntPtr hMonitor) { 
            uint current = 50, max = 100; 
            if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(hMonitor, 0x10, IntPtr.Zero, ref current, ref max)) return (int)current; 
            return -1; 
        } 
    }

    // ================== 4. HelpForm ==================
    public class HelpForm : Form {
        public HelpForm() {
            this.Text = "关于 & 说明"; this.Size = new Size(520, 450); this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog; this.MaximizeBox = false; this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(31, 31, 31); this.ForeColor = Color.White;
            Label title = new Label { Text = "HM's Simple Brightness Tool", Top = 20, Left = 20, AutoSize = true, Font = new Font("Segoe UI", 14, FontStyle.Bold) };
            TextBox info = new TextBox { 
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Top = 60, Left = 20, Width = 460, Height = 280,
                BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.LightGray, BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9),
                Text = 
@"HM's Simple Brightness Tool

一个极致轻量、便携、现代化的 Windows 屏幕亮度控制工具。
(用不惯Twinkle Tray，软件体积大，卡，提交issue也得不到支持。故自己搓一个适合自己体制的程序)

【独家功能：非线性曲线】
独家支持自定义亮度映射曲线，让亮度阶梯不一致的显示器也能使用相同的亮度映射。
例如：让 0-28% 对应主屏幕0-55%，83-100% 对应主屏幕92-100%。

【滚轮调节操作】
1. 将鼠标悬停在任务栏托盘区域的【本软件图标】上。
2. 激活后，滚动滚轮即可调节。
3. 离开图标区域后，调节立即停止。

【配置文件】
配置文件保存在软件同级目录下的 'HMSimpleBrightness_Config.json'。
您可以将本文件夹移动到任何位置（便携模式）。"
            };
            info.SelectionLength = 0;
            Button btnOk = new Button { Text = "关闭", Top = 360, Left = 380, Width = 100, Height = 35, BackColor = Color.Teal, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.OK };
            this.Controls.Add(title); this.Controls.Add(info); this.Controls.Add(btnOk);
        }
    }

    // ================== 5. SettingsForm (FlowLayout 自适应) ==================
    public class SettingsForm : Form { 
        public SettingsForm(AppConfig config) { 
            this.Text = "设置"; 
            this.AutoSize = true; 
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.StartPosition = FormStartPosition.CenterScreen; 
            this.FormBorderStyle = FormBorderStyle.FixedDialog; 
            this.MaximizeBox = false; 
            
            FlowLayoutPanel mainLayout = new FlowLayoutPanel {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(20),
                Width = 400
            };
            this.Controls.Add(mainLayout);

            CheckBox chkAuto = new CheckBox { Text = "开机自动启动", Width = 350, Checked = IsAutoStart(), Font = new Font("Microsoft YaHei UI", 9), Margin = new Padding(0, 0, 0, 15) }; 
            
            Label lblStep = new Label { Text = "滚轮步长 (1-20):", AutoSize = true, Font = new Font("Microsoft YaHei UI", 9) }; 
            NumericUpDown numStep = new NumericUpDown { Minimum = 1, Maximum = 20, Width = 100, Margin = new Padding(0, 5, 0, 15) };
            numStep.Value = Math.Clamp(config.ScrollStep, 1, 20); 
            
            Label lblDelay = new Label { Text = "调节响应延迟 (防卡顿 ms):", AutoSize = true, Font = new Font("Microsoft YaHei UI", 9) }; 
            NumericUpDown numDelay = new NumericUpDown { Minimum = 0, Maximum = 2000, Width = 100, Margin = new Padding(0, 5, 0, 15) };
            numDelay.Value = Math.Clamp(config.DebounceTime, 0, 2000); 

            Label lblPower = new Label { Text = "电源按钮模式:", AutoSize = true, Font = new Font("Microsoft YaHei UI", 9) };
            ComboBox cmbPower = new ComboBox { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 5, 0, 15) };
            cmbPower.Items.Add("DDC/CI (硬件指令 - 推荐)");
            cmbPower.Items.Add("Windows API (软件信号 - 兼容)");
            cmbPower.SelectedIndex = config.UseSoftwarePower ? 1 : 0;

            Button btnClearHidden = new Button { Text = $"重置隐藏显示器 ({config.HiddenMonitors.Count})", Width = 300, Height = 40, Margin = new Padding(0, 5, 0, 15) }; 
            btnClearHidden.Click += (s, e) => { config.HiddenMonitors.Clear(); MessageBox.Show("已重置，请重新扫描或重启软件。", "提示"); }; 
            
            Button btnOk = new Button { Text = "保存设置", Width = 120, Height = 40, DialogResult = DialogResult.OK, BackColor = Color.Teal, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(120, 10, 0, 0) }; 
            btnOk.Click += (s, e) => { 
                config.ScrollStep = (int)numStep.Value; 
                config.DebounceTime = (int)numDelay.Value; 
                config.UseSoftwarePower = (cmbPower.SelectedIndex == 1);
                SetAutoStart(chkAuto.Checked); 
                this.Close(); 
            }; 

            mainLayout.Controls.AddRange(new Control[] { chkAuto, lblStep, numStep, lblDelay, numDelay, lblPower, cmbPower, btnClearHidden, btnOk });
        } 
        private bool IsAutoStart() { using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false)) return key?.GetValue("SimpleBrightness") != null; } 
        private void SetAutoStart(bool enable) { using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)) { if (enable) key?.SetValue("SimpleBrightness", Application.ExecutablePath); else key?.DeleteValue("SimpleBrightness", false); } } 
    }

    // ================== 6. BrightnessForm ==================
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
                    // 修复：添加 ToolTip
                    ToolTip tip = new ToolTip(); tip.SetToolTip(btnPower, "左键：关闭 (Standby)\n右键：开启 (On)");
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

    // ================== 7. CurveEditorForm (修复布局) ==================
    public class CurveEditorForm : Form
    {
        private MonitorInfo _monitor; private AppConfig _config; private Dictionary<int, int> _currentPoints; private FlowLayoutPanel _panel;
        
        public CurveEditorForm(MonitorInfo monitor, AppConfig config) {
            _monitor = monitor; _config = config; _currentPoints = new Dictionary<int, int>(config.GetCurveForMonitor(monitor.UniqueId));
            this.Size = new Size(850, 500); 
            this.BackColor = Color.FromArgb(31, 31, 31); 
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "Curve Editor";

            // 1. 顶部固定区域 (Panel Dock Top)
            Panel top = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(25, 25, 25) };
            
            Label title = new Label { Text = $"编辑: {monitor.Name}", Location = new Point(15, 18), AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
            
            // 修复：数值框上调 5px
            NumericUpDown num = new NumericUpDown { Value = 50, Width = 80, Location = new Point(350, 13), Font = new Font("Segoe UI", 10) };
            
            // 修复：按钮宽度增加到 110px
            Button add = new Button { Text = "添加节点", Width = 110, Height = 34, Location = new Point(440, 13), BackColor = Color.Gray, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            Button save = new Button { Text = "保存并生效", Width = 120, Height = 34, Location = new Point(700, 13), BackColor = Color.Teal, ForeColor = Color.White, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat };
            
            add.Click += (s, e) => { int x = (int)num.Value; if (!_currentPoints.ContainsKey(x)) { _currentPoints[x] = x; RefreshSliders(); }};
            save.Click += (s, e) => { _config.Curves[_monitor.UniqueId] = new Dictionary<int, int>(_currentPoints); _config.Save(); this.Close(); };
            
            top.Controls.AddRange(new Control[] { title, num, add, save });
            this.Controls.Add(top);
            
            // 2. 下方内容区域 (Fill) - 注意：先添加Top再添加Fill
            _panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(20, 10, 0, 0) };
            this.Controls.Add(_panel); 
            _panel.BringToFront(); // 确保显示在最前

            RefreshSliders();
        }
        
        private void RefreshSliders() { _panel.Controls.Clear(); if (!_currentPoints.ContainsKey(0)) _currentPoints[0]=0; if(!_currentPoints.ContainsKey(100)) _currentPoints[100]=100; foreach(var k in _currentPoints.Keys.OrderBy(x=>x)) _panel.Controls.Add(CreateItem(k, _currentPoints[k])); }
        
        private Control CreateItem(int x, int y) { 
             // 缩减高度适应窗口
             Panel p = new Panel { Width = 70, Height = 350, Margin = new Padding(8), BackColor = Color.FromArgb(45,45,45) };
             
             // 修复：黄色数值标签高度增加到 45px，并往下移一点，防止切头
             Label l = new Label { Text = y.ToString(), Top = 10, Width = 70, Height = 45, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Yellow, Font = new Font("Segoe UI", 16, FontStyle.Bold) };
             
             Label k = new Label { Text = x + "%", Top = 325, Width = 70, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new Font("Segoe UI", 9) };
             
             if(x!=0&&x!=100) { 
                 Button d = new Button { Text = "×", Top = 325, Left = 20, Width = 30, Height = 20, ForeColor = Color.Red, FlatStyle=FlatStyle.Flat, BackColor = Color.Transparent }; 
                 d.FlatAppearance.BorderSize = 0; d.BringToFront();
                 d.Click+=(s,e)=>{_currentPoints.Remove(x);RefreshSliders();}; 
                 p.Controls.Add(d); k.Top = 305;
             }
             
             // 调整滑块位置，避开上方大号Label
             int sliderTop = 60; 
             int sliderBottom = (x!=0&&x!=100) ? 300 : 320;
             TrackBar t = new TrackBar { Orientation = Orientation.Vertical, Minimum = 0, Maximum = 100, Value = y, Top = sliderTop, Height = sliderBottom - sliderTop, Width = 45, Left = 12, TickStyle = TickStyle.None };
             
             ToolTip tip = new ToolTip(); 
             t.Scroll += (s, e) => { _currentPoints[x] = t.Value; l.Text = t.Value.ToString(); tip.SetToolTip(t, t.Value.ToString()); };
             t.MouseWheel += (s, e) => { int change = e.Delta > 0 ? 1 : -1; t.Value = Math.Clamp(t.Value + change, 0, 100); _currentPoints[x] = t.Value; l.Text = t.Value.ToString(); ((HandledMouseEventArgs)e).Handled = true; };
             
             p.Controls.AddRange(new Control[]{l,t,k}); 
             return p;
        }
    }

    // ================== 8. OsdForm ==================
    public class OsdForm : Form { 
        private System.Windows.Forms.Timer _timer; private int _targetValue = 50; private string _monitorName;
        public OsdForm(string name) { 
            _monitorName = name; this.FormBorderStyle = FormBorderStyle.None; this.ShowInTaskbar = false; this.TopMost = true; this.Size = new Size(220, 60); this.StartPosition = FormStartPosition.Manual; this.BackColor = Color.Black; this.Opacity = 0.85; this.DoubleBuffered = true; 
            this.Region = Region.FromHrgn(NativeMethods.CreateRoundRectRgn(0, 0, Width, Height, 15, 15)); 
            _timer = new System.Windows.Forms.Timer { Interval = 1500 }; _timer.Tick += (s, e) => this.Hide(); 
        }
        protected override bool ShowWithoutActivation => true; 
        public void UpdateName(string name) { _monitorName = name; this.Invalidate(); }
        public void ShowOSD(int value, bool isDebug, int realVal, int x, int y) { 
            _targetValue = value; if (isDebug) { _monitorName += " [DEBUG]"; _targetValue = realVal; } 
            this.Location = new Point(x, y); 
            this.Show(); this.Refresh(); _timer.Stop(); _timer.Start(); 
        }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; Color textColor = _monitorName.Contains("[DEBUG]") ? Color.Orange : Color.LightGray; TextRenderer.DrawText(e.Graphics, _monitorName, new Font("Segoe UI", 9), new Point(15, 5), textColor); Rectangle barRect = new Rectangle(15, 40, 190, 8); using(var b = new SolidBrush(Color.FromArgb(80, 80, 80))) e.Graphics.FillRectangle(b, barRect); int w = (int)(190 * (_targetValue / 100.0)); if (w > 0) e.Graphics.FillRectangle(Brushes.White, 15, 40, w, 8); TextRenderer.DrawText(e.Graphics, $"{_targetValue}%", new Font("Segoe UI", 12, FontStyle.Bold), new Point(160, 0), Color.White); } 
    }

    // ================== 9. InputBox ==================
    public class InputBox : Form { public string ResultText { get; private set; } = ""; public InputBox(string title, string prompt, string defaultText) { this.Size = new Size(300, 180); this.Text = title; this.StartPosition = FormStartPosition.CenterScreen; this.FormBorderStyle = FormBorderStyle.FixedDialog; Label l = new Label { Text = prompt, Top = 20, Left = 20, AutoSize = true }; TextBox t = new TextBox { Text = defaultText, Top = 50, Left = 20, Width = 240 }; Button b = new Button { Text = "确定", Top = 90, Left = 180, DialogResult = DialogResult.OK }; b.Click += (s, e) => { ResultText = t.Text; this.Close(); }; this.Controls.AddRange(new Control[] { l, t, b }); this.AcceptButton = b; } }

    // ================== 10. AppConfig & MonitorInfo ==================
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

    // ================== 11. MouseHook ==================
    public class MouseHook { private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam); private LowLevelMouseProc _proc; private IntPtr _hookID = IntPtr.Zero; public event MouseEventHandler? MouseWheel; public MouseHook() { _proc = HookCallback; } public void Install() { _hookID = SetWindowsHookEx(14, _proc, GetModuleHandle(System.Diagnostics.Process.GetCurrentProcess().MainModule?.ModuleName ?? "user32"), 0); } public void Uninstall() { UnhookWindowsHookEx(_hookID); } private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) { if (nCode >= 0 && (int)wParam == 0x020A) { MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam); short delta = (short)((hookStruct.mouseData >> 16) & 0xffff); MouseWheel?.Invoke(this, new MouseEventArgs(MouseButtons.None, 0, hookStruct.pt.x, hookStruct.pt.y, delta)); } return CallNextHookEx(_hookID, nCode, wParam, lParam); } [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x; public int y; } [StructLayout(LayoutKind.Sequential)] private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; } [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId); [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hhk); [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam); [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr GetModuleHandle(string lpModuleName); }
    
    // ================== 12. NativeMethods ==================
    internal static class NativeMethods { [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData); public delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData); [DllImport("dxva2.dll")] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref int pdwNumberOfPhysicalMonitors); [DllImport("dxva2.dll", EntryPoint = "GetPhysicalMonitorsFromHMONITOR")] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, int dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray); [DllImport("dxva2.dll")] public static extern bool SetVCPFeature(IntPtr hMonitor, byte bVCPCode, uint dwNewValue); [DllImport("dxva2.dll")] public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte bVCPCode, IntPtr pvct, ref uint pdwCurrentValue, ref uint pdwMaximumValue); [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse); [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point p); [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount); [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Auto)] public static extern IntPtr GetParent(IntPtr hWnd); [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo); [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, int wParam, int lParam); [StructLayout(LayoutKind.Sequential)] public struct Rect { public int left; public int top; public int right; public int bottom; } [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] public struct PHYSICAL_MONITOR { public IntPtr hPhysicalMonitor; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription; } }
}

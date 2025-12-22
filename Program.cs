using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
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
            using (Mutex mutex = new Mutex(false, "Global\\" + "SimpleBrightness_v8_Stable_Final"))
            {
                if (!mutex.WaitOne(0, false)) return;
                ApplicationConfiguration.Initialize();
                Application.Run(new MyCustomApplicationContext());
            }
        }
    }

    // ================== 核心上下文 ==================
    public class MyCustomApplicationContext : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip contextMenu;
        private BrightnessForm? brightnessWindow;
        private List<MonitorInfo> monitors = new List<MonitorInfo>();
        private AppConfig config;
        private MouseHook mouseHook;
        
        // 每个显示器对应一个独立的 OSD 窗口，解决“打架”问题
        private Dictionary<string, OsdForm> osdForms = new Dictionary<string, OsdForm>();
        
        private DateTime _lastIconHoverTime = DateTime.MinValue;

        public MyCustomApplicationContext()
        {
            config = AppConfig.Load();
            RefreshMonitors();
            
            // 启动异步读取真实亮度
            Task.Run(() => ReadRealBrightness());

            contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("设置", null, (s, e) => ShowSettings());
            contextMenu.Items.Add("重新扫描", null, (s, e) => { RefreshMonitors(); Task.Run(() => ReadRealBrightness()); });
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("退出", null, (s, e) => {
                mouseHook?.Uninstall();
                trayIcon.Visible = false;
                Application.Exit(); 
            });

            trayIcon = new NotifyIcon()
            {
                Icon = IconDrawer.DrawSunIcon(),
                ContextMenuStrip = contextMenu,
                Visible = true,
                Text = "Simple Brightness (鼠标悬停滚动)"
            };

            trayIcon.MouseMove += (s, e) => _lastIconHoverTime = DateTime.Now;
            trayIcon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowBrightnessWindow(); };

            mouseHook = new MouseHook();
            mouseHook.MouseWheel += OnGlobalMouseWheel;
            mouseHook.Install();
        }

        private void ReadRealBrightness()
        {
            foreach (var m in monitors) {
                int realVal = -1;
                if (m.Type == MonitorType.WMI) {
                    try {
                        var searcher = new ManagementObjectSearcher("root\\Wmi", "SELECT * FROM WmiMonitorBrightness");
                        foreach (ManagementObject obj in searcher.Get()) realVal = int.Parse(obj["CurrentBrightness"].ToString());
                    } catch { }
                } else if (m.Type == MonitorType.DDC) {
                     realVal = BrightnessController.GetVCPBrightness(m.Handle);
                }
                if (realVal != -1) m.LastBrightness = realVal;
            }
        }

        private void OnGlobalMouseWheel(object? sender, MouseEventArgs e)
        {
            if ((DateTime.Now - _lastIconHoverTime).TotalSeconds < 0.8)
            {
                _lastIconHoverTime = DateTime.Now; 
                int change = e.Delta > 0 ? config.ScrollStep : -config.ScrollStep;
                
                // 只调节未隐藏的
                foreach (var m in monitors.Where(x => !config.HiddenMonitors.Contains(x.UniqueId)))
                {
                    int newVal = Math.Clamp(m.LastBrightness + change, 0, 100);
                    ApplyBrightness(m, newVal, true); 
                }
            }
        }

        public void ApplyBrightness(MonitorInfo m, int val, bool updateUi = true)
        {
            m.LastBrightness = val;
            int finalVal = val;
            if (m.Type == MonitorType.DDC) {
                var curve = config.GetCurveForMonitor(m.UniqueId);
                finalVal = Interpolate(val, curve);
            }
            
            Task.Run(() => BrightnessController.SetBrightness(m, finalVal));
            
            // 调用对应显示器的 OSD
            ShowMonitorOsd(m, val);

            if (updateUi && brightnessWindow != null && !brightnessWindow.IsDisposed && brightnessWindow.Visible) {
                brightnessWindow.Invoke(new Action(() => brightnessWindow.UpdateSlider(m.UniqueId, val)));
            }
        }

        // 显示多实例 OSD
        private void ShowMonitorOsd(MonitorInfo m, int val)
        {
            if (!osdForms.ContainsKey(m.UniqueId) || osdForms[m.UniqueId].IsDisposed) {
                osdForms[m.UniqueId] = new OsdForm(m.Name);
            }
            // 在 UI 线程显示
            var osd = osdForms[m.UniqueId];
            if (osd.InvokeRequired) osd.Invoke(new Action(() => osd.ShowOSD(val)));
            else osd.ShowOSD(val);
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
            
            var screen = Screen.FromPoint(Cursor.Position);
            int x = screen.WorkingArea.Right - brightnessWindow.Width - 10;
            int y = screen.WorkingArea.Bottom - brightnessWindow.Height - 10;
            if (y < screen.WorkingArea.Top) y = screen.WorkingArea.Top + 50;
            if (x < screen.WorkingArea.Left) x = screen.WorkingArea.Left + 10;

            brightnessWindow.Location = new Point(x, y);
            brightnessWindow.Show();
            brightnessWindow.Activate();
        }

        private void ShowSettings() { using (var form = new SettingsForm(config)) { if (form.ShowDialog() == DialogResult.OK) config.Save(); } }

        private void RefreshMonitors()
        {
            monitors.Clear();
            // WMI
            try {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\Wmi", "SELECT * FROM WmiMonitorBrightness");
                foreach (ManagementObject queryObj in searcher.Get()) {
                    string id = queryObj["InstanceName"]?.ToString() ?? "UnknownWmi";
                    if (!monitors.Any(m => m.InstanceId == id)) monitors.Add(new MonitorInfo { Type = MonitorType.WMI, Name = "内置屏幕", InstanceId = id, UniqueId = "WMI_" + GetStableHash(id) });
                }
            } catch { }
            // DDC
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.Rect lprcMonitor, IntPtr dwData) {
                int count = 0;
                NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, ref count);
                if (count > 0) {
                    var pMs = new NativeMethods.PHYSICAL_MONITOR[count];
                    if (NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, pMs)) {
                        for (int i = 0; i < pMs.Length; i++) {
                            string desc = new string(pMs[i].szPhysicalMonitorDescription).Trim('\0');
                            // 修复：使用确定性 Hash，解决重启复活问题
                            string stableId = "DDC_" + GetStableHash(desc) + "_IDX_" + i;
                            monitors.Add(new MonitorInfo { Type = MonitorType.DDC, Name = desc, Handle = pMs[i].hPhysicalMonitor, UniqueId = stableId });
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);
        }

        // 手写确定性哈希，替代 .NET Core 随机的 GetHashCode
        private static string GetStableHash(string str) {
            ulong hash = 5381;
            foreach (char c in str) hash = ((hash << 5) + hash) + c;
            return hash.ToString();
        }
    }

    // ================== 主窗口 (动态高度布局) ==================
    public class BrightnessForm : Form
    {
        private List<MonitorInfo> _monitors;
        private AppConfig _config;
        private MyCustomApplicationContext _context;
        private Dictionary<string, TrackBar> _sliders = new Dictionary<string, TrackBar>();
        private Dictionary<string, Label> _valLabels = new Dictionary<string, Label>();

        public BrightnessForm(List<MonitorInfo> monitors, AppConfig config, MyCustomApplicationContext context, ContextMenuStrip menu)
        {
            _monitors = monitors; _config = config; _context = context;
            this.FormBorderStyle = FormBorderStyle.None; this.ShowInTaskbar = false; this.BackColor = Color.FromArgb(32, 32, 32); 
            this.StartPosition = FormStartPosition.Manual; this.TopMost = true; 
            this.Deactivate += (s, e) => {
                if (Application.OpenForms.OfType<CurveEditorForm>().Any() || Application.OpenForms.OfType<SettingsForm>().Any()) return;
                if (!this.Bounds.Contains(Cursor.Position)) this.Hide(); else this.Activate();
            };

            var visibleMonitors = _monitors.Where(m => !_config.HiddenMonitors.Contains(m.UniqueId)).ToList();
            if (visibleMonitors.Count == 0) visibleMonitors = _monitors; 

            // 动态计算总高度
            int totalHeight = 60; // Header
            foreach (var m in visibleMonitors) totalHeight += (m.Type == MonitorType.WMI ? 90 : 150); // 内置屏矮一点，外接屏高一点
            this.Size = new Size(420, totalHeight + 10);

            Label title = new Label { Text = "控制中心", Top = 20, Left = 20, ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold), AutoSize = true };
            this.Controls.Add(title);

            Button btnSettings = new Button { Text = "⋮", Top = 15, Left = 370, Width = 30, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
            btnSettings.FlatAppearance.BorderSize = 0;
            btnSettings.Click += (s, e) => menu.Show(Cursor.Position); 
            this.Controls.Add(btnSettings);

            int y = 60;
            foreach (var m in visibleMonitors)
            {
                int currentItemHeight = (m.Type == MonitorType.WMI ? 90 : 150);

                // 1. 名称 (修复：增加 Top 间距防止被切)
                Label lblName = new Label { Text = m.Name, Top = y + 5, Left = 20, ForeColor = Color.LightGray, AutoSize = true, Font = new Font("Microsoft YaHei UI", 9) };
                new ToolTip().SetToolTip(lblName, "右键点击可隐藏此显示器");
                lblName.MouseClick += (s, e) => {
                    if (e.Button == MouseButtons.Right && MessageBox.Show($"隐藏 \"{m.Name}\"？", "确认", MessageBoxButtons.YesNo) == DialogResult.Yes) {
                        _config.HiddenMonitors.Add(m.UniqueId); _config.Save(); this.Close();
                    }
                };
                this.Controls.Add(lblName);

                // 2. 数值
                Label lblVal = new Label { Text = $"{m.LastBrightness}%", Top = y + 5, Left = 350, ForeColor = Color.Cyan, AutoSize = true, Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold) };
                _valLabels[m.UniqueId] = lblVal;
                this.Controls.Add(lblVal);

                // 3. 滑块
                TrackBar slider = new TrackBar { 
                    Top = y + 30, Left = 10, Width = 380, Height = 45,
                    Maximum = 100, Minimum = 0, Value = m.LastBrightness,
                    TickStyle = TickStyle.None
                };
                _sliders[m.UniqueId] = slider;

                Action<int> updateLogic = (newVal) => {
                    lblVal.Text = $"{newVal}%";
                    _context.ApplyBrightness(m, newVal, false);
                };
                slider.Scroll += (s, e) => updateLogic(slider.Value);
                slider.MouseWheel += (s, e) => {
                    int change = e.Delta > 0 ? _config.ScrollStep : -_config.ScrollStep;
                    slider.Value = Math.Clamp(slider.Value + change, 0, 100);
                    updateLogic(slider.Value); ((HandledMouseEventArgs)e).Handled = true;
                };
                this.Controls.Add(slider);

                // 4. 功能键 (仅 DDC 显示)
                if (m.Type == MonitorType.DDC)
                {
                    int btnY = y + 80; 
                    // 修复：拉宽按钮，防止“编辑曲”
                    Button btnCurve = new Button { 
                        Text = "编辑曲线", Top = btnY, Left = 20, Width = 100, Height = 35,
                        FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(60,60,60)
                    };
                    btnCurve.Click += (s, e) => { new CurveEditorForm(m, _config).ShowDialog(); };
                    
                    Button btnPower = new Button { 
                        Text = "⏻ 电源", Top = btnY, Left = 130, Width = 80, Height = 35,
                        FlatStyle = FlatStyle.Flat, ForeColor = Color.LightGreen, BackColor = Color.FromArgb(60,60,60)
                    };
                    btnPower.MouseDown += (s, e) => {
                         Task.Run(() => BrightnessController.SetPowerState(m, e.Button == MouseButtons.Left)); 
                    };
                    this.Controls.Add(btnCurve);
                    this.Controls.Add(btnPower);
                }
                
                // 分隔线
                Panel div = new Panel { Top = y + currentItemHeight - 1, Left = 10, Width = 400, Height = 1, BackColor = Color.FromArgb(50,50,50) };
                this.Controls.Add(div);

                y += currentItemHeight;
            }
        }

        public void UpdateSlider(string id, int val) {
            if (_sliders.ContainsKey(id)) { _sliders[id].Value = val; _valLabels[id].Text = val + "%"; }
        }
    }

    // ================== 曲线编辑器 (修复Tooltip) ==================
    public class CurveEditorForm : Form
    {
        private MonitorInfo _monitor; private AppConfig _config; private Dictionary<int, int> _currentPoints; private FlowLayoutPanel _panel;
        public CurveEditorForm(MonitorInfo monitor, AppConfig config) {
            _monitor = monitor; _config = config; _currentPoints = new Dictionary<int, int>(config.GetCurveForMonitor(monitor.UniqueId));
            this.Size = new Size(800, 600); this.BackColor = Color.FromArgb(45, 45, 48); this.StartPosition = FormStartPosition.CenterParent;
            
            Panel top = new Panel { Dock = DockStyle.Top, Height = 60 }; 
            NumericUpDown num = new NumericUpDown { Value = 50, Top = 15, Left = 15, Width = 80, Font = new Font("Microsoft YaHei UI", 10) };
            Button add = new Button { Text = "添加节点", Top = 12, Left = 110, Height = 35, Width = 120, BackColor = Color.Gray, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            Button save = new Button { Text = "保存并生效", Top = 12, Left = 600, Height = 35, Width = 120, BackColor = Color.Teal, ForeColor = Color.White, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat };
            add.Click += (s, e) => { int x = (int)num.Value; if (!_currentPoints.ContainsKey(x)) { _currentPoints[x] = x; RefreshSliders(); }};
            save.Click += (s, e) => { _config.Curves[_monitor.UniqueId] = new Dictionary<int, int>(_currentPoints); _config.Save(); this.Close(); };
            top.Controls.AddRange(new Control[] { num, add, save });
            this.Controls.Add(top);
            _panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(10) };
            this.Controls.Add(_panel); RefreshSliders();
        }
        private void RefreshSliders() { _panel.Controls.Clear(); if (!_currentPoints.ContainsKey(0)) _currentPoints[0]=0; if(!_currentPoints.ContainsKey(100)) _currentPoints[100]=100; foreach(var k in _currentPoints.Keys.OrderBy(x=>x)) _panel.Controls.Add(CreateItem(k, _currentPoints[k])); }
        
        private Control CreateItem(int x, int y) { 
             Panel p = new Panel { Width = 80, Height = 450, Margin = new Padding(5), BackColor = Color.FromArgb(60,60,60) };
             Label l = new Label { Text = y.ToString(), Top = 10, Width = 80, Height = 30, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Yellow, Font = new Font("Arial", 16, FontStyle.Bold) };
             TrackBar t = new TrackBar { Orientation = Orientation.Vertical, Minimum = 0, Maximum = 100, Value = y, Top = 50, Height = 320, Width = 45, Left = 17, TickStyle = TickStyle.None };
             
             // 修复：添加ToolTip显示当前值
             ToolTip tip = new ToolTip();
             t.Scroll += (s, e) => { 
                 _currentPoints[x] = t.Value; 
                 l.Text = t.Value.ToString(); 
                 tip.SetToolTip(t, t.Value.ToString()); // 动态更新鼠标提示
             };
             
             Label k = new Label { Text = x + "%", Top = 380, Width = 80, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new Font("Arial", 10) };
             if(x!=0&&x!=100) { Button d = new Button { Text = "×", Top = 410, Left = 25, Width = 30, Height = 25, ForeColor = Color.Red, FlatStyle=FlatStyle.Flat }; d.Click+=(s,e)=>{_currentPoints.Remove(x);RefreshSliders();}; p.Controls.Add(d); }
             t.MouseWheel += (s, e) => { int change = e.Delta > 0 ? 1 : -1; t.Value = Math.Clamp(t.Value + change, 0, 100); _currentPoints[x] = t.Value; l.Text = t.Value.ToString(); ((HandledMouseEventArgs)e).Handled = true; };
             p.Controls.AddRange(new Control[]{l,t,k}); 
             return p;
        }
    }

    // ================== 多实例 OSD ==================
    public class OsdForm : Form
    {
        private System.Windows.Forms.Timer _timer; private int _targetValue = 50; private string _monitorName;
        public OsdForm(string name) { 
            _monitorName = name;
            this.FormBorderStyle = FormBorderStyle.None; this.ShowInTaskbar = false; this.TopMost = true; 
            this.Size = new Size(220, 60); // 稍微加高一点显示名字
            this.StartPosition = FormStartPosition.Manual; this.BackColor = Color.Black; this.Opacity = 0.85; this.DoubleBuffered = true; 
            this.Region = Region.FromHrgn(NativeMethods.CreateRoundRectRgn(0, 0, Width, Height, 15, 15)); 
            _timer = new System.Windows.Forms.Timer { Interval = 1500 }; _timer.Tick += (s, e) => this.Hide(); 
        }
        protected override bool ShowWithoutActivation => true; 
        public void ShowOSD(int value) { 
            _targetValue = value; 
            var screen = Screen.FromPoint(Cursor.Position); // 简单跟随鼠标所在屏幕
            this.Location = new Point(screen.Bounds.X + (screen.Bounds.Width - Width) / 2, screen.Bounds.Bottom - 150); 
            this.Show(); this.Refresh(); _timer.Stop(); _timer.Start(); 
        }
        protected override void OnPaint(PaintEventArgs e) { 
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; 
            // 绘制显示器名字
            TextRenderer.DrawText(e.Graphics, _monitorName, new Font("Microsoft YaHei UI", 9), new Point(15, 5), Color.LightGray);
            // 绘制进度条
            Rectangle barRect = new Rectangle(15, 40, 190, 8); 
            using(var b = new SolidBrush(Color.FromArgb(80, 80, 80))) e.Graphics.FillRectangle(b, barRect); 
            int w = (int)(190 * (_targetValue / 100.0)); 
            if (w > 0) e.Graphics.FillRectangle(Brushes.White, 15, 40, w, 8); 
            // 绘制数值
            TextRenderer.DrawText(e.Graphics, $"{_targetValue}%", new Font("Microsoft YaHei UI", 12, FontStyle.Bold), new Point(160, 0), Color.White); 
        }
    }

    // ================== 设置窗口 (保持不变) ==================
    public class SettingsForm : Form
    {
        public SettingsForm(AppConfig config) {
            this.Text = "设置"; this.Size = new Size(350, 420); this.StartPosition = FormStartPosition.CenterScreen; this.FormBorderStyle = FormBorderStyle.FixedDialog; this.MaximizeBox = false;
            int y = 30;
            CheckBox chkAuto = new CheckBox { Text = "开机自动启动", Top = y, Left = 30, Width = 250, Checked = IsAutoStart(), Font = new Font("Microsoft YaHei UI", 9) };
            this.Controls.Add(chkAuto); y += 60;
            Label lblStep = new Label { Text = "滚轮步长 (1-20):", Top = y, Left = 30, AutoSize = true, Font = new Font("Microsoft YaHei UI", 9) };
            NumericUpDown numStep = new NumericUpDown { Value = config.ScrollStep, Minimum = 1, Maximum = 20, Top = y + 25, Left = 30, Width = 100 };
            this.Controls.Add(lblStep); this.Controls.Add(numStep); y += 80;
            Button btnClearHidden = new Button { Text = $"重置隐藏显示器 ({config.HiddenMonitors.Count})", Top = y, Left = 30, Width = 280, Height = 40 };
            btnClearHidden.Click += (s, e) => { config.HiddenMonitors.Clear(); MessageBox.Show("已重置，请重新扫描或重启软件。", "提示"); };
            this.Controls.Add(btnClearHidden); y += 80;
            Button btnOk = new Button { Text = "保存设置", Top = y, Left = 110, Width = 100, Height = 40, DialogResult = DialogResult.OK, BackColor = Color.Teal, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnOk.Click += (s, e) => { config.ScrollStep = (int)numStep.Value; SetAutoStart(chkAuto.Checked); this.Close(); };
            this.Controls.Add(btnOk);
        }
        private bool IsAutoStart() { using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false)) return key?.GetValue("SimpleBrightness") != null; }
        private void SetAutoStart(bool enable) { using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)) { if (enable) key?.SetValue("SimpleBrightness", Application.ExecutablePath); else key?.DeleteValue("SimpleBrightness", false); } }
    }

    // ================== 底层辅助 ==================
    public class AppConfig { public int ScrollStep { get; set; } = 5; public List<string> HiddenMonitors { get; set; } = new List<string>(); public Dictionary<string, Dictionary<int, int>> Curves { get; set; } = new Dictionary<string, Dictionary<int, int>>(); private static string ConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpleBrightness_Config_v8.json"); public static AppConfig Load() { if (File.Exists(ConfigPath)) try { return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig(); } catch { } return new AppConfig(); } public void Save() => File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this)); public Dictionary<int, int> GetCurveForMonitor(string id) { if (Curves.ContainsKey(id)) return Curves[id]; return new Dictionary<int, int> { { 0, 0 }, { 100, 100 } }; } }
    public class MonitorInfo { public string Name { get; set; } = "Unknown"; public MonitorType Type { get; set; } public IntPtr Handle { get; set; } public string InstanceId { get; set; } = ""; public string UniqueId { get; set; } = ""; public int LastBrightness { get; set; } = 50; }
    public enum MonitorType { WMI, DDC }
    public static class IconDrawer { public static Icon DrawSunIcon() { using (Bitmap bmp = new Bitmap(32, 32)) using (Graphics g = Graphics.FromImage(bmp)) { g.SmoothingMode = SmoothingMode.AntiAlias; g.FillEllipse(Brushes.Gold, 9, 9, 14, 14); g.DrawEllipse(new Pen(Color.Orange, 2), 9, 9, 14, 14); Pen rayPen = new Pen(Color.Gold, 3); for (int i = 0; i < 360; i += 45) { double rad = i * Math.PI / 180; float x1 = 16 + (float)(9 * Math.Cos(rad)); float y1 = 16 + (float)(9 * Math.Sin(rad)); float x2 = 16 + (float)(15 * Math.Cos(rad)); float y2 = 16 + (float)(15 * Math.Sin(rad)); g.DrawLine(rayPen, x1, y1, x2, y2); } return Icon.FromHandle(bmp.GetHicon()); } } }
    public class MouseHook { private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam); private LowLevelMouseProc _proc; private IntPtr _hookID = IntPtr.Zero; public event MouseEventHandler? MouseWheel; public MouseHook() { _proc = HookCallback; } public void Install() { _hookID = SetWindowsHookEx(14, _proc, GetModuleHandle(System.Diagnostics.Process.GetCurrentProcess().MainModule?.ModuleName ?? "user32"), 0); } public void Uninstall() { UnhookWindowsHookEx(_hookID); } private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) { if (nCode >= 0 && (int)wParam == 0x020A) { MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam); short delta = (short)((hookStruct.mouseData >> 16) & 0xffff); MouseWheel?.Invoke(this, new MouseEventArgs(MouseButtons.None, 0, hookStruct.pt.x, hookStruct.pt.y, delta)); } return CallNextHookEx(_hookID, nCode, wParam, lParam); } [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x; public int y; } [StructLayout(LayoutKind.Sequential)] private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; } [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId); [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hhk); [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam); [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr GetModuleHandle(string lpModuleName); }
    public static class BrightnessController { 
        public static void SetBrightness(MonitorInfo monitor, int level) { if (monitor.Type == MonitorType.WMI) { try { var searcher = new ManagementObjectSearcher("root\\Wmi", "SELECT * FROM WmiMonitorBrightnessMethods"); foreach (ManagementObject m in searcher.Get()) m.InvokeMethod("WmiSetBrightness", new object[] { 1, level }); } catch {} } else if (monitor.Type == MonitorType.DDC) { NativeMethods.SetVCPFeature(monitor.Handle, 0x10, (uint)level); } } 
        public static void SetPowerState(MonitorInfo monitor, bool turnOn) { if (monitor.Type == MonitorType.DDC) NativeMethods.SetVCPFeature(monitor.Handle, 0xD6, turnOn ? 0x01u : 0x04u); }
        public static int GetVCPBrightness(IntPtr hMonitor) { uint current = 50, max = 100; if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(hMonitor, 0x10, IntPtr.Zero, ref current, ref max)) return (int)current; return -1; }
    }
    internal static class NativeMethods { 
        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData); public delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData); 
        [DllImport("dxva2.dll")] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref int pdwNumberOfPhysicalMonitors); 
        [DllImport("dxva2.dll", EntryPoint = "GetPhysicalMonitorsFromHMONITOR")] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, int dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray); 
        [DllImport("dxva2.dll")] public static extern bool SetVCPFeature(IntPtr hMonitor, byte bVCPCode, uint dwNewValue); 
        [DllImport("dxva2.dll")] public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte bVCPCode, IntPtr pvct, ref uint pdwCurrentValue, ref uint pdwMaximumValue);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse); 
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int left; public int top; public int right; public int bottom; } 
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] public struct PHYSICAL_MONITOR { public IntPtr hPhysicalMonitor; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription; } 
    }
}

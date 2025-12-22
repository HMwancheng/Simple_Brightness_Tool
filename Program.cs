using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            // 防止多开
            using (Mutex mutex = new Mutex(false, "Global\\" + "SimpleBrightness_v2_Unique_ID"))
            {
                if (!mutex.WaitOne(0, false)) return;
                ApplicationConfiguration.Initialize();
                Application.Run(new MyCustomApplicationContext());
            }
        }
    }

    // ================== 核心逻辑与托盘 ==================
    public class MyCustomApplicationContext : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip contextMenu;
        private BrightnessForm brightnessWindow;
        private List<MonitorInfo> monitors = new List<MonitorInfo>();
        private AppConfig config;
        private MouseHook mouseHook;
        private OsdForm osd;

        public MyCustomApplicationContext()
        {
            config = AppConfig.Load();
            RefreshMonitors();

            // 初始化 OSD
            osd = new OsdForm();

            // 初始化菜单
            contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("设置 (步长/自启)", null, (s, e) => ShowSettings());
            contextMenu.Items.Add("重新扫描显示器", null, (s, e) => { RefreshMonitors(); });
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("退出", null, (s, e) => {
                mouseHook?.Uninstall();
                Application.Exit(); 
            });

            // 动态绘制图标
            Icon customIcon = IconDrawer.DrawSunIcon();

            trayIcon = new NotifyIcon()
            {
                Icon = customIcon,
                ContextMenuStrip = contextMenu,
                Visible = true,
                Text = "Simple Brightness"
            };

            trayIcon.MouseClick += (s, e) => {
                if (e.Button == MouseButtons.Left) ShowBrightnessWindow();
            };

            // 安装全局鼠标钩子 (用于托盘滚轮)
            mouseHook = new MouseHook();
            mouseHook.MouseWheel += OnGlobalMouseWheel;
            mouseHook.Install();
        }

        private void OnGlobalMouseWheel(object sender, MouseEventArgs e)
        {
            // 判断鼠标是否在任务栏区域
            // 简单的判定逻辑：鼠标位置不在 WorkingArea 内 (即在任务栏上)
            if (!Screen.GetWorkingArea(Cursor.Position).Contains(Cursor.Position))
            {
                // 找到鼠标所在的那个显示器
                var targetScreen = Screen.FromPoint(Cursor.Position);
                // 找到对应的 MonitorInfo (这里简化处理，尽量匹配名字或默认第一个)
                // 既然是在任务栏滚动，通常是想调节“当前主屏”或者“鼠标所在屏幕”
                // 这里我们简单地调节所有“非忽略”的屏幕，或者你可以改为只调节鼠标所在屏
                
                int change = e.Delta > 0 ? config.ScrollStep : -config.ScrollStep;
                
                foreach (var m in monitors.Where(x => !config.HiddenMonitors.Contains(x.UniqueId)))
                {
                    // 只有当显示器位于鼠标所在的 Screen 区域时才调节 (多屏独立调节)
                    // 但由于 MonitorInfo 和 Screen 很难完美对应，这里默认调节“所有未隐藏屏幕”
                    // 或者调节 index=0 的屏幕。
                    // 为了体验，我们这里调节“所有启用屏幕”，这符合大多数人“变亮变暗”的直觉
                    
                    int newVal = Math.Clamp(m.LastBrightness + change, 0, 100);
                    ApplyBrightness(m, newVal, false); // false = 不更新UI滑块防止跨线程崩溃，只更值
                }
            }
        }

        // 统一的应用亮度入口
        public void ApplyBrightness(MonitorInfo m, int val, bool updateUi = true)
        {
            m.LastBrightness = val;
            
            int finalVal = val;
            if (m.Type == MonitorType.DDC)
            {
                var curve = config.GetCurveForMonitor(m.UniqueId);
                finalVal = Interpolate(val, curve);
            }
            
            // 异步发送防止卡顿
            Task.Run(() => BrightnessController.SetBrightness(m, finalVal));

            // 显示 OSD
            osd.ShowOSD(val);

            // 如果主窗口开着，更新它的滑块
            if (updateUi && brightnessWindow != null && !brightnessWindow.IsDisposed && brightnessWindow.Visible)
            {
                brightnessWindow.UpdateSlider(m.UniqueId, val);
            }
        }

        private int Interpolate(int input, Dictionary<int, int> points)
        {
            var sorted = points.OrderBy(k => k.Key).ToList();
            if (input <= sorted.First().Key) return sorted.First().Value;
            if (input >= sorted.Last().Key) return sorted.Last().Value;

            for (int i = 0; i < sorted.Count - 1; i++)
            {
                if (input >= sorted[i].Key && input <= sorted[i+1].Key)
                {
                    int x0 = sorted[i].Key; int y0 = sorted[i].Value;
                    int x1 = sorted[i+1].Key; int y1 = sorted[i+1].Value;
                    return y0 + (input - x0) * (y1 - y0) / (x1 - x0);
                }
            }
            return input;
        }

        private void ShowBrightnessWindow()
        {
            if (brightnessWindow == null || brightnessWindow.IsDisposed)
            {
                brightnessWindow = new BrightnessForm(monitors, config, this);
            }
            
            // 智能定位
            var screen = Screen.FromPoint(Cursor.Position);
            var workArea = screen.WorkingArea;
            
            int x = workArea.Right - brightnessWindow.Width - 10;
            int y = workArea.Bottom - brightnessWindow.Height - 10;
            
            // 确保不超出边界
            if (y < workArea.Top) y = workArea.Top + 10;

            brightnessWindow.Location = new Point(x, y);
            brightnessWindow.Show();
            brightnessWindow.Activate();
        }

        private void ShowSettings()
        {
            using (var form = new SettingsForm(config))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    config.Save();
                }
            }
        }

        private void RefreshMonitors()
        {
            monitors.Clear();
            // 1. WMI
            try {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\Wmi", "SELECT * FROM WmiMonitorBrightness");
                foreach (ManagementObject queryObj in searcher.Get()) {
                    string id = queryObj["InstanceName"].ToString();
                    // 简单的去重策略
                    if (!monitors.Any(m => m.InstanceId == id)) {
                        monitors.Add(new MonitorInfo { 
                            Type = MonitorType.WMI, Name = "内置屏幕", InstanceId = id, UniqueId = "WMI_" + id 
                        });
                    }
                }
            } catch { }

            // 2. DDC
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, 
                delegate (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.Rect lprcMonitor, IntPtr dwData) 
                {
                    int count = 0;
                    NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, ref count);
                    if (count > 0)
                    {
                        var pMs = new NativeMethods.PHYSICAL_MONITOR[count];
                        if (NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, pMs))
                        {
                            foreach (var pm in pMs)
                            {
                                string desc = new string(pm.szPhysicalMonitorDescription).Trim('\0');
                                string uid = "DDC_" + desc.GetHashCode() + "_" + pm.hPhysicalMonitor; // 尽量唯一的ID
                                
                                monitors.Add(new MonitorInfo { 
                                    Type = MonitorType.DDC, 
                                    Name = desc, 
                                    Handle = pm.hPhysicalMonitor,
                                    UniqueId = uid
                                });
                            }
                        }
                    }
                    return true;
                }, IntPtr.Zero);
        }
    }

    // ================== 图标绘制 ==================
    public static class IconDrawer 
    {
        public static Icon DrawSunIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // 背景透明
                // 画太阳圆心
                g.FillEllipse(Brushes.Gold, 8, 8, 16, 16);
                g.DrawEllipse(Pens.Orange, 8, 8, 16, 16);
                // 画光芒
                Pen rayPen = new Pen(Color.Gold, 2);
                for (int i = 0; i < 360; i += 45) {
                    double rad = i * Math.PI / 180;
                    float x1 = 16 + (float)(8 * Math.Cos(rad));
                    float y1 = 16 + (float)(8 * Math.Sin(rad));
                    float x2 = 16 + (float)(14 * Math.Cos(rad));
                    float y2 = 16 + (float)(14 * Math.Sin(rad));
                    g.DrawLine(rayPen, x1, y1, x2, y2);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    // ================== OSD 悬浮窗 ==================
    public class OsdForm : Form
    {
        private System.Windows.Forms.Timer _timer;
        private int _targetValue = 50;
        
        public OsdForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.Size = new Size(200, 40);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.Black;
            this.Opacity = 0.8;
            this.DoubleBuffered = true;

            // 圆角
            this.Region = Region.FromHrgn(NativeMethods.CreateRoundRectRgn(0, 0, Width, Height, 20, 20));

            _timer = new System.Windows.Forms.Timer { Interval = 1500 };
            _timer.Tick += (s, e) => this.Hide();
        }

        protected override bool ShowWithoutActivation => true; // 不抢焦点

        public void ShowOSD(int value)
        {
            _targetValue = value;
            if (this.InvokeRequired) { this.Invoke(new Action(() => ShowOSD(value))); return; }
            
            // 每次显示重置位置到当前屏幕下方
            var screen = Screen.FromPoint(Cursor.Position);
            this.Location = new Point(screen.Bounds.X + (screen.Bounds.Width - Width) / 2, screen.Bounds.Bottom - 150);
            
            this.Show();
            this.Refresh();
            _timer.Stop();
            _timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            
            // 画进度条背景
            Rectangle barRect = new Rectangle(10, 25, 180, 6);
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(50, 50, 50)), barRect);

            // 画进度
            int w = (int)(180 * (_targetValue / 100.0));
            if (w > 0) e.Graphics.FillRectangle(Brushes.White, 10, 25, w, 6);

            // 画文字
            string text = $"亮度: {_targetValue}%";
            TextRenderer.DrawText(e.Graphics, text, new Font("Segoe UI", 12, FontStyle.Bold), new Point(10, 2), Color.White);
        }
    }

    // ================== 主界面 ==================
    public class BrightnessForm : Form
    {
        private List<MonitorInfo> _monitors;
        private AppConfig _config;
        private MyCustomApplicationContext _context;
        private Dictionary<string, TrackBar> _sliders = new Dictionary<string, TrackBar>();
        private Dictionary<string, Label> _valLabels = new Dictionary<string, Label>();

        public BrightnessForm(List<MonitorInfo> monitors, AppConfig config, MyCustomApplicationContext context)
        {
            _monitors = monitors;
            _config = config;
            _context = context;

            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.BackColor = Color.FromArgb(32, 32, 32); 
            this.StartPosition = FormStartPosition.Manual;
            this.Deactivate += (s, e) => {
                if (Application.OpenForms.OfType<CurveEditorForm>().Count() == 0 && Application.OpenForms.OfType<SettingsForm>().Count() == 0) 
                    this.Hide(); 
            };

            // 过滤掉被隐藏的显示器
            var visibleMonitors = _monitors.Where(m => !_config.HiddenMonitors.Contains(m.UniqueId)).ToList();
            if (visibleMonitors.Count == 0) visibleMonitors = _monitors; // 如果全隐藏了，就全显示防止无法操作

            int itemHeight = 85; // 增加高度防止文字显示不全
            this.Size = new Size(380, 50 + (visibleMonitors.Count * itemHeight));

            Label title = new Label { Text = "控制中心", Top = 15, Left = 15, ForeColor = Color.White, Font = new Font("Segoe UI", 12, FontStyle.Bold), AutoSize = true };
            this.Controls.Add(title);

            // 设置按钮
            Button btnSettings = new Button { Text = "⋮", Top = 10, Left = 340, Width = 30, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
            btnSettings.FlatAppearance.BorderSize = 0;
            btnSettings.Click += (s, e) => contextMenu.Show(Cursor.Position); // 呼出右键菜单
            this.Controls.Add(btnSettings);

            int y = 50;
            foreach (var m in visibleMonitors)
            {
                // 名称 (可右键隐藏)
                Label lblName = new Label { Text = m.Name, Top = y, Left = 15, ForeColor = Color.LightGray, AutoSize = true, Cursor = Cursors.Hand };
                new ToolTip().SetToolTip(lblName, "右键点击可隐藏此显示器");
                lblName.MouseClick += (s, e) => {
                    if (e.Button == MouseButtons.Right) {
                        if (MessageBox.Show($"确定要隐藏显示器 \"{m.Name}\" 吗？\n隐藏后它将不会出现在列表中。", "隐藏显示器", MessageBoxButtons.YesNo) == DialogResult.Yes) {
                            _config.HiddenMonitors.Add(m.UniqueId);
                            _config.Save();
                            MessageBox.Show("已隐藏。如需恢复，请在托盘右键菜单进入设置。", "提示");
                            this.Close(); // 关闭窗口刷新
                        }
                    }
                };
                this.Controls.Add(lblName);

                // 数值显示
                Label lblVal = new Label { Text = $"{m.LastBrightness}%", Top = y, Left = 330, ForeColor = Color.White, AutoSize = true };
                _valLabels[m.UniqueId] = lblVal;
                this.Controls.Add(lblVal);

                // 滑块
                TrackBar slider = new TrackBar { 
                    Top = y + 25, Left = 10, Width = 230, 
                    Maximum = 100, Minimum = 0, Value = m.LastBrightness,
                    TickStyle = TickStyle.None
                };
                _sliders[m.UniqueId] = slider;

                slider.Scroll += (s, e) => {
                    lblVal.Text = $"{slider.Value}%";
                    _context.ApplyBrightness(m, slider.Value, false);
                };
                // 窗口内滚轮支持
                slider.MouseWheel += (s, e) => {
                    int change = e.Delta > 0 ? _config.ScrollStep : -_config.ScrollStep;
                    slider.Value = Math.Clamp(slider.Value + change, 0, 100);
                    slider.Scroll(s, EventArgs.Empty); // 触发Scroll逻辑
                    ((HandledMouseEventArgs)e).Handled = true;
                };

                this.Controls.Add(slider);

                // DDC 功能区
                if (m.Type == MonitorType.DDC)
                {
                    // 曲线
                    Button btnCurve = new Button { 
                        Text = "曲线", Top = y + 25, Left = 250, Width = 50, Height = 30,
                        FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(60,60,60)
                    };
                    btnCurve.Click += (s, e) => {
                        new CurveEditorForm(m, _config).ShowDialog();
                    };
                    
                    // 电源
                    Button btnPower = new Button { 
                        Text = "⏻", Top = y + 25, Left = 310, Width = 40, Height = 30,
                        FlatStyle = FlatStyle.Flat, ForeColor = Color.LightGreen, BackColor = Color.FromArgb(60,60,60),
                        Font = new Font("Segoe UI Symbol", 9)
                    };
                    btnPower.MouseDown += (s, e) => {
                         Task.Run(() => BrightnessController.SetPowerState(m, e.Button == MouseButtons.Left)); // 左开右关
                    };

                    this.Controls.Add(btnCurve);
                    this.Controls.Add(btnPower);
                }
                y += itemHeight;
            }
        }

        public void UpdateSlider(string id, int val)
        {
            if (_sliders.ContainsKey(id)) {
                if (_sliders[id].InvokeRequired) _sliders[id].Invoke(new Action(() => { _sliders[id].Value = val; _valLabels[id].Text = val + "%"; }));
                else { _sliders[id].Value = val; _valLabels[id].Text = val + "%"; }
            }
        }
    }

    // ================== 设置界面 ==================
    public class SettingsForm : Form
    {
        public SettingsForm(AppConfig config)
        {
            this.Text = "设置"; this.Size = new Size(300, 300);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            int y = 20;
            
            // 开机自启
            CheckBox chkAuto = new CheckBox { Text = "开机自动启动", Top = y, Left = 20, Width = 200, Checked = IsAutoStart() };
            this.Controls.Add(chkAuto);
            y += 40;

            // 步长
            Label lblStep = new Label { Text = "滚轮步长 (1-20):", Top = y, Left = 20, AutoSize = true };
            NumericUpDown numStep = new NumericUpDown { Value = config.ScrollStep, Minimum = 1, Maximum = 20, Top = y-3, Left = 150, Width = 60 };
            this.Controls.Add(lblStep); this.Controls.Add(numStep);
            y += 40;

            // 清除隐藏列表
            Button btnClearHidden = new Button { Text = $"重置隐藏显示器 ({config.HiddenMonitors.Count})", Top = y, Left = 20, Width = 240 };
            btnClearHidden.Click += (s, e) => {
                config.HiddenMonitors.Clear();
                MessageBox.Show("已重置，请重新扫描或重启软件。", "提示");
            };
            this.Controls.Add(btnClearHidden);
            y += 60;

            Button btnOk = new Button { Text = "保存", Top = y, Left = 100, DialogResult = DialogResult.OK };
            btnOk.Click += (s, e) => {
                config.ScrollStep = (int)numStep.Value;
                SetAutoStart(chkAuto.Checked);
                this.Close();
            };
            this.Controls.Add(btnOk);
        }

        private bool IsAutoStart()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
                return key?.GetValue("SimpleBrightness") != null;
        }

        private void SetAutoStart(bool enable)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (enable) key.SetValue("SimpleBrightness", Application.ExecutablePath);
                else key.DeleteValue("SimpleBrightness", false);
            }
        }
    }

    // ================== 配置类 ==================
    public class AppConfig
    {
        public int ScrollStep { get; set; } = 5;
        public List<string> HiddenMonitors { get; set; } = new List<string>();
        public Dictionary<string, Dictionary<int, int>> Curves { get; set; } = new Dictionary<string, Dictionary<int, int>>();
        
        private static string ConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpleBrightness_Config_v2.json");

        public static AppConfig Load()
        {
            if (File.Exists(ConfigPath))
                try { return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig(); } catch { }
            return new AppConfig();
        }

        public void Save() => File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this));
        
        public Dictionary<int, int> GetCurveForMonitor(string id)
        {
            if (Curves.ContainsKey(id)) return Curves[id];
            return new Dictionary<int, int> { { 0, 0 }, { 100, 100 } };
        }
    }

    // ================== 数据结构 ==================
    public class MonitorInfo
    {
        public string Name { get; set; } = "Unknown";
        public MonitorType Type { get; set; }
        public IntPtr Handle { get; set; }
        public string InstanceId { get; set; } 
        public string UniqueId { get; set; }
        public int LastBrightness { get; set; } = 50;
    }
    public enum MonitorType { WMI, DDC }

    // ================== 曲线编辑器 (UI修复版) ==================
    public class CurveEditorForm : Form
    {
        // ... (保持之前的逻辑，只修改按钮高度) ...
        // 为了节省篇幅，这里简写，请确保将按钮高度设置为至少 30
        private MonitorInfo _monitor; private AppConfig _config; private Dictionary<int, int> _currentPoints; private FlowLayoutPanel _panel;
        public CurveEditorForm(MonitorInfo monitor, AppConfig config) {
            _monitor = monitor; _config = config; _currentPoints = new Dictionary<int, int>(config.GetCurveForMonitor(monitor.UniqueId));
            this.Size = new Size(600, 450); this.BackColor = Color.FromArgb(45, 45, 48);
            Panel top = new Panel { Dock = DockStyle.Top, Height = 50 }; // 增加高度
            NumericUpDown num = new NumericUpDown { Value = 50, Top = 10, Left = 10 };
            Button add = new Button { Text = "添加节点", Top = 8, Left = 140, Height = 30, Width = 100, BackColor = Color.Gray, ForeColor = Color.White };
            Button save = new Button { Text = "保存并生效", Top = 8, Left = 450, Height = 30, Width = 120, BackColor = Color.Teal, ForeColor = Color.White, DialogResult = DialogResult.OK };
            add.Click += (s, e) => { int x = (int)num.Value; if (!_currentPoints.ContainsKey(x)) { _currentPoints[x] = x; RefreshSliders(); }};
            save.Click += (s, e) => { _config.Curves[_monitor.UniqueId] = new Dictionary<int, int>(_currentPoints); _config.Save(); this.Close(); };
            top.Controls.AddRange(new Control[] { num, add, save });
            this.Controls.Add(top);
            _panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.LeftToRight };
            this.Controls.Add(_panel); RefreshSliders();
        }
        private void RefreshSliders() { /* ...逻辑同上一版... */ _panel.Controls.Clear(); if (!_currentPoints.ContainsKey(0)) _currentPoints[0]=0; if(!_currentPoints.ContainsKey(100)) _currentPoints[100]=100; foreach(var k in _currentPoints.Keys.OrderBy(x=>x)) _panel.Controls.Add(CreateItem(k, _currentPoints[k])); }
        private Control CreateItem(int x, int y) { /* ...逻辑同上一版，颜色改为White... */ 
             Panel p = new Panel { Width = 60, Height = 300 };
             Label l = new Label { Text = y.ToString(), Top = 0, Width = 60, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White };
             TrackBar t = new TrackBar { Orientation = Orientation.Vertical, Minimum = 0, Maximum = 100, Value = y, Top = 30, Height = 200, Width = 45, Left = 7, TickStyle = TickStyle.None };
             Label k = new Label { Text = x + "%", Top = 240, Width = 60, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White };
             if(x!=0&&x!=100) { Button d = new Button { Text = "×", Top = 270, Left = 15, Width = 30, ForeColor = Color.Red }; d.Click+=(s,e)=>{_currentPoints.Remove(x);RefreshSliders();}; p.Controls.Add(d); }
             t.Scroll+=(s,e)=>{_currentPoints[x]=t.Value;l.Text=t.Value.ToString();};
             t.MouseWheel += (s, e) => { int change = e.Delta > 0 ? 1 : -1; t.Value = Math.Clamp(t.Value + change, 0, 100); _currentPoints[x] = t.Value; l.Text = t.Value.ToString(); ((HandledMouseEventArgs)e).Handled = true; };
             p.Controls.AddRange(new Control[]{l,t,k}); return p;
        }
    }

    // ================== 全局鼠标钩子 (托盘滚轮用) ==================
    public class MouseHook
    {
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelMouseProc _proc;
        private IntPtr _hookID = IntPtr.Zero;
        public event MouseEventHandler MouseWheel;

        public MouseHook() { _proc = HookCallback; }
        public void Install() { _hookID = SetWindowsHookEx(14, _proc, GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName), 0); }
        public void Uninstall() { UnhookWindowsHookEx(_hookID); }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (MouseMessages)wParam == MouseMessages.WM_MOUSEWHEEL)
            {
                MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                short delta = (short)((hookStruct.mouseData >> 16) & 0xffff);
                MouseWheel?.Invoke(this, new MouseEventArgs(MouseButtons.None, 0, hookStruct.pt.x, hookStruct.pt.y, delta));
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private const int WH_MOUSE_LL = 14;
        private enum MouseMessages { WM_MOUSEWHEEL = 0x020A }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x; public int y; }
        [StructLayout(LayoutKind.Sequential)] private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr GetModuleHandle(string lpModuleName);
    }

    // ================== DDC控制 (不变) ==================
    public static class BrightnessController
    {
        public static void SetBrightness(MonitorInfo monitor, int level) {
            if (monitor.Type == MonitorType.WMI) { /* ... WMI ... */ }
            else if (monitor.Type == MonitorType.DDC) { NativeMethods.SetVCPFeature(monitor.Handle, 0x10, (uint)level); }
        }
        public static void SetPowerState(MonitorInfo monitor, bool turnOn) {
            if (monitor.Type == MonitorType.DDC) NativeMethods.SetVCPFeature(monitor.Handle, 0xD6, turnOn ? 0x01u : 0x04u);
        }
    }

    // ================== Native Methods ==================
    internal static class NativeMethods
    {
        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);
        public delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);
        [DllImport("dxva2.dll")] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref int pdwNumberOfPhysicalMonitors);
        [DllImport("dxva2.dll", EntryPoint = "GetPhysicalMonitorsFromHMONITOR")] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, int dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);
        [DllImport("dxva2.dll")] public static extern bool SetVCPFeature(IntPtr hMonitor, byte bVCPCode, uint dwNewValue);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int left; public int top; public int right; public int bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] public struct PHYSICAL_MONITOR { public IntPtr hPhysicalMonitor; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription; }
    }
}

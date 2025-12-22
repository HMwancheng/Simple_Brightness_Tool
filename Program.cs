using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks; // 引入 Task 用于异步防止卡顿
using System.Windows.Forms;

namespace SimpleBrightness
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MyCustomApplicationContext());
        }
    }

    // ================== 核心逻辑与托盘 ==================
    public class MyCustomApplicationContext : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip contextMenu;
        private BrightnessForm brightnessWindow;
        private List<MonitorInfo> monitors = new List<MonitorInfo>();

        public MyCustomApplicationContext()
        {
            RefreshMonitors();
            contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("重新扫描显示器", null, (s, e) => { RefreshMonitors(); });
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("退出", null, (s, e) => Application.Exit());

            trayIcon = new NotifyIcon()
            {
                Icon = SystemIcons.Shield, 
                ContextMenuStrip = contextMenu,
                Visible = true,
                Text = "点击打开面板 | 滚轮调节亮度"
            };

            trayIcon.MouseClick += (s, e) => {
                if (e.Button == MouseButtons.Left) ShowBrightnessWindow();
            };
        }

        private void ShowBrightnessWindow()
        {
            if (brightnessWindow == null || brightnessWindow.IsDisposed)
            {
                brightnessWindow = new BrightnessForm(monitors);
            }
            var screen = Screen.PrimaryScreen.WorkingArea;
            int x = screen.Width - brightnessWindow.Width - 10;
            int y = screen.Height - brightnessWindow.Height - 10;
            if (y < 0) y = 0;
            
            brightnessWindow.Location = new Point(x, y);
            brightnessWindow.Show();
            brightnessWindow.Activate();
        }

        private void RefreshMonitors()
        {
            monitors.Clear();
            // 1. WMI
            try {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\Wmi", "SELECT * FROM WmiMonitorBrightness");
                foreach (ManagementObject queryObj in searcher.Get()) {
                    monitors.Add(new MonitorInfo { 
                        Type = MonitorType.WMI, 
                        Name = "内置屏幕",
                        InstanceId = queryObj["InstanceName"].ToString() 
                    });
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
                                monitors.Add(new MonitorInfo { 
                                    Type = MonitorType.DDC, 
                                    Name = new string(pm.szPhysicalMonitorDescription).Trim('\0'), 
                                    Handle = pm.hPhysicalMonitor,
                                    UniqueId = pm.szPhysicalMonitorDescription.GetHashCode().ToString()
                                });
                            }
                        }
                    }
                    return true;
                }, IntPtr.Zero);
        }
    }

    public class MonitorInfo
    {
        public string Name { get; set; } = "Unknown";
        public MonitorType Type { get; set; }
        public IntPtr Handle { get; set; }
        public string InstanceId { get; set; } 
        public string UniqueId { get; set; }
    }

    public enum MonitorType { WMI, DDC }

    public class CurveConfig
    {
        public Dictionary<string, Dictionary<int, int>> Curves { get; set; } = new Dictionary<string, Dictionary<int, int>>();
        private static string ConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpleBrightness_Curves.json");

        public static CurveConfig Load()
        {
            if (File.Exists(ConfigPath))
            {
                try { return JsonSerializer.Deserialize<CurveConfig>(File.ReadAllText(ConfigPath)) ?? new CurveConfig(); }
                catch { return new CurveConfig(); }
            }
            return new CurveConfig();
        }

        public void Save()
        {
            try { File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this)); } catch { }
        }

        public Dictionary<int, int> GetCurveForMonitor(string id)
        {
            if (Curves.ContainsKey(id)) return Curves[id];
            return new Dictionary<int, int> { { 0, 0 }, { 100, 100 } };
        }
    }

    // ================== 主界面 ==================
    public class BrightnessForm : Form
    {
        private List<MonitorInfo> _monitors;
        private CurveConfig _config;

        public BrightnessForm(List<MonitorInfo> monitors)
        {
            _monitors = monitors;
            _config = CurveConfig.Load();

            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.BackColor = Color.FromArgb(32, 32, 32); 
            this.StartPosition = FormStartPosition.Manual;
            this.Deactivate += (s, e) => {
                if (Application.OpenForms.OfType<CurveEditorForm>().Count() == 0) 
                    this.Hide(); 
            };

            int itemHeight = 70;
            this.Size = new Size(360, 40 + (_monitors.Count * itemHeight)); // 稍微加宽一点放按钮

            Label title = new Label { Text = "屏幕控制中心", Top = 10, Left = 10, ForeColor = Color.White, Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold), AutoSize = true };
            this.Controls.Add(title);

            // 提示标签
            Label tip = new Label { Text = "右击电源键熄灭", Top = 12, Left = 240, ForeColor = Color.Gray, Font = new Font(this.Font.FontFamily, 8), AutoSize = true };
            this.Controls.Add(tip);

            int y = 40;
            foreach (var m in _monitors)
            {
                Label lbl = new Label { Text = m.Name, Top = y, Left = 10, ForeColor = Color.LightGray, AutoSize = true };
                this.Controls.Add(lbl);

                TrackBar slider = new TrackBar { 
                    Top = y + 20, Left = 5, Width = 230, 
                    Maximum = 100, Minimum = 0, Value = 50,
                    TickStyle = TickStyle.None
                };
                
                // DDC 专属控制区
                if (m.Type == MonitorType.DDC)
                {
                    // 1. 曲线编辑按钮 (齿轮)
                    Button btnCurve = new Button { 
                        Text = "⚙", Top = y + 20, Left = 240, Width = 30, Height = 30,
                        FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(60,60,60),
                        TextAlign = ContentAlignment.MiddleCenter
                    };
                    btnCurve.FlatAppearance.BorderSize = 0;
                    btnCurve.Click += (s, e) => {
                        var editor = new CurveEditorForm(m, _config);
                        editor.ShowDialog();
                    };
                    
                    // 2. 电源按钮 (开关图标)
                    // 左键：唤醒 (ON), 右键：熄灭 (Standby)
                    Button btnPower = new Button { 
                        Text = "⏻", Top = y + 20, Left = 280, Width = 60, Height = 30,
                        FlatStyle = FlatStyle.Flat, ForeColor = Color.LightGreen, BackColor = Color.FromArgb(60,60,60),
                        TextAlign = ContentAlignment.MiddleCenter,
                        Font = new Font("Segoe UI Symbol", 10)
                    };
                    btnPower.FlatAppearance.BorderSize = 0;
                    
                    // 绑定点击事件
                    btnPower.MouseDown += (s, e) => {
                        if (e.Button == MouseButtons.Left) {
                            // 左键：唤醒/开启
                            Task.Run(() => BrightnessController.SetPowerState(m, true));
                            MessageBox.Show("已发送唤醒信号 (Power ON)", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else if (e.Button == MouseButtons.Right) {
                            // 右键：熄灭/待机
                             Task.Run(() => BrightnessController.SetPowerState(m, false));
                        }
                    };

                    this.Controls.Add(btnCurve);
                    this.Controls.Add(btnPower);
                }

                Action<int> update = (val) => {
                    int finalVal = val;
                    if (m.Type == MonitorType.DDC) {
                        var curve = _config.GetCurveForMonitor(m.UniqueId);
                        finalVal = Interpolate(val, curve);
                    }
                    BrightnessController.SetBrightness(m, finalVal);
                };

                slider.Scroll += (s, e) => update(slider.Value);
                slider.MouseWheel += (s, e) => {
                    int change = e.Delta > 0 ? 5 : -5;
                    slider.Value = Math.Clamp(slider.Value + change, 0, 100);
                    update(slider.Value);
                };

                this.Controls.Add(slider);
                y += itemHeight;
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
    }

    public class CurveEditorForm : Form
    {
        private MonitorInfo _monitor;
        private CurveConfig _config;
        private Dictionary<int, int> _currentPoints;
        private FlowLayoutPanel _panel;

        public CurveEditorForm(MonitorInfo monitor, CurveConfig config)
        {
            _monitor = monitor;
            _config = config;
            _currentPoints = new Dictionary<int, int>(config.GetCurveForMonitor(monitor.UniqueId));

            this.Text = $"编辑曲线: {monitor.Name}";
            this.Size = new Size(600, 400);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(45, 45, 48);
            this.ForeColor = Color.White;

            Panel topPanel = new Panel { Dock = DockStyle.Top, Height = 40 };
            NumericUpDown numPos = new NumericUpDown { Minimum = 1, Maximum = 99, Value = 50, Top = 8, Left = 10, Width = 60 };
            Button btnAdd = new Button { Text = "在此处添加节点", Top = 8, Left = 80, Width = 120, BackColor = Color.Gray };
            Button btnSave = new Button { Text = "保存并生效", Top = 8, Left = 480, Width = 90, BackColor = Color.Teal, ForeColor = Color.White, DialogResult = DialogResult.OK };

            btnAdd.Click += (s, e) => {
                int x = (int)numPos.Value;
                if (!_currentPoints.ContainsKey(x)) { _currentPoints[x] = x; RefreshSliders(); }
            };

            btnSave.Click += (s, e) => {
                _config.Curves[_monitor.UniqueId] = new Dictionary<int, int>(_currentPoints);
                _config.Save();
                this.Close();
            };

            topPanel.Controls.AddRange(new Control[] { numPos, btnAdd, btnSave });
            this.Controls.Add(topPanel);

            _panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
            this.Controls.Add(_panel);

            RefreshSliders();
        }

        private void RefreshSliders()
        {
            _panel.Controls.Clear();
            if (!_currentPoints.ContainsKey(0)) _currentPoints[0] = 0;
            if (!_currentPoints.ContainsKey(100)) _currentPoints[100] = 100;
            foreach (var x in _currentPoints.Keys.OrderBy(k => k)) _panel.Controls.Add(CreateSliderItem(x, _currentPoints[x]));
        }

        private Control CreateSliderItem(int xInput, int yOutput)
        {
            Panel p = new Panel { Width = 60, Height = 300, Margin = new Padding(5) };
            Label lblVal = new Label { Text = yOutput.ToString(), Top = 5, Left = 0, Width = 60, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White };
            TrackBar bar = new TrackBar { Orientation = Orientation.Vertical, Minimum = 0, Maximum = 100, Value = yOutput, TickStyle = TickStyle.None, Top = 30, Height = 200, Width = 45, Left = 7 };
            Label lblKey = new Label { Text = $"{xInput}%", Top = 235, Left = 0, Width = 60, TextAlign = ContentAlignment.MiddleCenter, Font = new Font(this.Font, FontStyle.Bold), ForeColor = Color.White };
            
            if (xInput != 0 && xInput != 100) {
                Button btnDel = new Button { Text = "×", Top = 260, Left = 15, Width = 30, Height = 20, FlatStyle = FlatStyle.Flat, ForeColor = Color.Red };
                btnDel.Click += (s, e) => { _currentPoints.Remove(xInput); RefreshSliders(); };
                p.Controls.Add(btnDel);
            }

            bar.Scroll += (s, e) => { _currentPoints[xInput] = bar.Value; lblVal.Text = bar.Value.ToString(); };
            bar.MouseWheel += (s, e) => { int change = e.Delta > 0 ? 1 : -1; bar.Value = Math.Clamp(bar.Value + change, 0, 100); _currentPoints[xInput] = bar.Value; lblVal.Text = bar.Value.ToString(); ((HandledMouseEventArgs)e).Handled = true; };

            p.Controls.AddRange(new Control[] { lblVal, bar, lblKey });
            p.BackColor = Color.FromArgb(60, 60, 60);
            return p;
        }
    }

    public static class BrightnessController
    {
        public static void SetBrightness(MonitorInfo monitor, int level)
        {
            if (monitor.Type == MonitorType.WMI) { /* ... WMI Logic ... */ }
            else if (monitor.Type == MonitorType.DDC) {
                NativeMethods.SetVCPFeature(monitor.Handle, 0x10, (uint)level);
            }
        }

        // 新增：电源控制
        public static void SetPowerState(MonitorInfo monitor, bool turnOn)
        {
            if (monitor.Type == MonitorType.DDC)
            {
                // VCP Code 0xD6: Power Mode
                // 0x01 = On (DDC On)
                // 0x04 = Off (Standby) - 有些显示器可能是 0x05, 但通常软件控制用 0x04
                uint value = turnOn ? 0x01u : 0x04u;
                NativeMethods.SetVCPFeature(monitor.Handle, 0xD6, value);
            }
        }
    }

    internal static class NativeMethods
    {
        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);
        public delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);
        [DllImport("dxva2.dll")] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref int pdwNumberOfPhysicalMonitors);
        [DllImport("dxva2.dll", EntryPoint = "GetPhysicalMonitorsFromHMONITOR")] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, int dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);
        [DllImport("dxva2.dll")] public static extern bool SetVCPFeature(IntPtr hMonitor, byte bVCPCode, uint dwNewValue);
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int left; public int top; public int right; public int bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] public struct PHYSICAL_MONITOR { public IntPtr hPhysicalMonitor; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription; }
    }
}

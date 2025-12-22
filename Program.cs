using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json;
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
            // 定位到托盘上方
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
            // 1. WMI (内置)
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

            // 2. DDC (外接)
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
                                    UniqueId = pm.szPhysicalMonitorDescription.GetHashCode().ToString() // 简单生成个ID用于存配置
                                });
                            }
                        }
                    }
                    return true;
                }, IntPtr.Zero);
        }
    }

    // ================== 数据结构 ==================
    public class MonitorInfo
    {
        public string Name { get; set; } = "Unknown";
        public MonitorType Type { get; set; }
        public IntPtr Handle { get; set; }
        public string InstanceId { get; set; } 
        public string UniqueId { get; set; } // 用于保存配置的Key
    }

    public enum MonitorType { WMI, DDC }

    // 存储曲线配置的类
    public class CurveConfig
    {
        // Key: Monitor UniqueId, Value: List of Points
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
            // 默认曲线：0->0, 100->100
            return new Dictionary<int, int> { { 0, 0 }, { 100, 100 } };
        }
    }

    // ================== 主界面：亮度滑块 ==================
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
                // 如果没有打开子窗口（曲线编辑器），失去焦点才隐藏
                if (Application.OpenForms.OfType<CurveEditorForm>().Count() == 0) 
                    this.Hide(); 
            };

            // 动态计算窗口高度
            int itemHeight = 70;
            this.Size = new Size(320, 40 + (_monitors.Count * itemHeight));

            Label title = new Label { Text = "屏幕亮度控制", Top = 10, Left = 10, ForeColor = Color.White, Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold), AutoSize = true };
            this.Controls.Add(title);

            int y = 40;
            foreach (var m in _monitors)
            {
                // 显示器名称
                Label lbl = new Label { Text = m.Name, Top = y, Left = 10, ForeColor = Color.LightGray, AutoSize = true };
                this.Controls.Add(lbl);

                // 亮度滑块
                TrackBar slider = new TrackBar { 
                    Top = y + 20, Left = 5, Width = 260, 
                    Maximum = 100, Minimum = 0, Value = 50,
                    TickStyle = TickStyle.None
                };
                
                // 曲线编辑按钮 (仅对 DDC 外接显示器有效)
                if (m.Type == MonitorType.DDC)
                {
                    Button btnCurve = new Button { 
                        Text = "⚙", Top = y + 20, Left = 270, Width = 30, Height = 30,
                        FlatStyle = FlatStyle.Flat, ForeColor = Color.White 
                    };
                    btnCurve.Click += (s, e) => {
                        var editor = new CurveEditorForm(m, _config);
                        editor.ShowDialog(); // 模态窗口，编辑完再回来
                    };
                    this.Controls.Add(btnCurve);
                }

                // 调节逻辑
                Action<int> update = (val) => {
                    int finalVal = val;
                    // 如果有曲线，进行插值计算
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

    // ================== 曲线编辑器 (类似均衡器) ==================
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
            // 复制一份数据用于编辑，避免直接改配置
            _currentPoints = new Dictionary<int, int>(config.GetCurveForMonitor(monitor.UniqueId));

            this.Text = $"编辑曲线: {monitor.Name}";
            this.Size = new Size(600, 400);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(45, 45, 48);
            this.ForeColor = Color.White;

            // 顶部操作区
            Panel topPanel = new Panel { Dock = DockStyle.Top, Height = 40 };
            
            NumericUpDown numPos = new NumericUpDown { Minimum = 1, Maximum = 99, Value = 50, Top = 8, Left = 10, Width = 60 };
            Button btnAdd = new Button { Text = "在此处添加节点", Top = 8, Left = 80, Width = 120, BackColor = Color.Gray };
            Label lblHint = new Label { Text = "拖动滑块调整实际输出亮度", Top = 12, Left = 220, AutoSize = true, ForeColor = Color.Gray };
            Button btnSave = new Button { Text = "保存并生效", Top = 8, Left = 480, Width = 90, BackColor = Color.Teal, ForeColor = Color.White, DialogResult = DialogResult.OK };

            btnAdd.Click += (s, e) => {
                int x = (int)numPos.Value;
                if (!_currentPoints.ContainsKey(x)) {
                    _currentPoints[x] = x; // 默认线性值
                    RefreshSliders();
                }
            };

            btnSave.Click += (s, e) => {
                _config.Curves[_monitor.UniqueId] = new Dictionary<int, int>(_currentPoints);
                _config.Save();
                this.Close();
            };

            topPanel.Controls.AddRange(new Control[] { numPos, btnAdd, lblHint, btnSave });
            this.Controls.Add(topPanel);

            // 主滚动区域
            _panel = new FlowLayoutPanel { 
                Dock = DockStyle.Fill, 
                AutoScroll = true, 
                WrapContents = false, // 强制单行，水平滚动
                FlowDirection = FlowDirection.LeftToRight 
            };
            this.Controls.Add(_panel);

            RefreshSliders();
        }

        private void RefreshSliders()
        {
            _panel.Controls.Clear();
            
            // 必须要有 0 和 100
            if (!_currentPoints.ContainsKey(0)) _currentPoints[0] = 0;
            if (!_currentPoints.ContainsKey(100)) _currentPoints[100] = 100;

            var sortedKeys = _currentPoints.Keys.OrderBy(k => k).ToList();

            foreach (var x in sortedKeys)
            {
                var item = CreateSliderItem(x, _currentPoints[x]);
                _panel.Controls.Add(item);
            }
        }

        private Control CreateSliderItem(int xInput, int yOutput)
        {
            Panel p = new Panel { Width = 60, Height = 300, Margin = new Padding(5) };
            
            Label lblVal = new Label { Text = yOutput.ToString(), Top = 5, Left = 0, Width = 60, TextAlign = ContentAlignment.MiddleCenter };
            
            TrackBar bar = new TrackBar { 
                Orientation = Orientation.Vertical, 
                Minimum = 0, Maximum = 100, Value = yOutput, 
                TickStyle = TickStyle.None,
                Top = 30, Height = 200, Width = 45, Left = 7 
            };
            
            Label lblKey = new Label { Text = $"{xInput}%", Top = 235, Left = 0, Width = 60, TextAlign = ContentAlignment.MiddleCenter, Font = new Font(this.Font, FontStyle.Bold) };
            
            // 删除按钮 (0和100不可删)
            if (xInput != 0 && xInput != 100)
            {
                Button btnDel = new Button { Text = "×", Top = 260, Left = 15, Width = 30, Height = 20, FlatStyle = FlatStyle.Flat, ForeColor = Color.Red };
                btnDel.Click += (s, e) => {
                    _currentPoints.Remove(xInput);
                    RefreshSliders();
                };
                p.Controls.Add(btnDel);
            }

            bar.Scroll += (s, e) => {
                _currentPoints[xInput] = bar.Value;
                lblVal.Text = bar.Value.ToString();
            };
            // 滚轮微调
            bar.MouseWheel += (s, e) => {
                int change = e.Delta > 0 ? 1 : -1;
                bar.Value = Math.Clamp(bar.Value + change, 0, 100);
                _currentPoints[xInput] = bar.Value;
                lblVal.Text = bar.Value.ToString();
                ((HandledMouseEventArgs)e).Handled = true; // 防止滚轮导致外层面板滚动
            };

            p.Controls.AddRange(new Control[] { lblVal, bar, lblKey });
            p.BackColor = Color.FromArgb(60, 60, 60);
            return p;
        }
    }

    // ================== DDC/CI 底层 ==================
    public static class BrightnessController
    {
        public static void SetBrightness(MonitorInfo monitor, int level)
        {
            if (monitor.Type == MonitorType.WMI)
            {
                try {
                    var searcher = new ManagementObjectSearcher("root\\Wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
                    foreach (ManagementObject m in searcher.Get()) {
                        m.InvokeMethod("WmiSetBrightness", new object[] { 1, level }); 
                    }
                } catch { }
            }
            else if (monitor.Type == MonitorType.DDC)
            {
                NativeMethods.SetVCPFeature(monitor.Handle, 0x10, (uint)level);
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

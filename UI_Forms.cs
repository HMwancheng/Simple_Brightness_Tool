using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using SimpleBrightness;
using Microsoft.Win32;

namespace SimpleBrightness
{
    // ================== Icon ==================
    public static class IconDrawer {
        public static Icon DrawNativeIcon() {
            // 使用48x48画布确保图标完整显示
            int iconSize = 48;
            int fontSize = 24;

            using (Bitmap bmp = new Bitmap(iconSize, iconSize))
            using (Graphics g = Graphics.FromImage(bmp)) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.Transparent);

                Font iconFont = new Font("Segoe MDL2 Assets", fontSize, FontStyle.Regular);
                // 测量文本大小以精确居中
                Size textSize = TextRenderer.MeasureText("\uE706", iconFont);
                int x = (iconSize - textSize.Width) / 2;
                int y = (iconSize - textSize.Height) / 2;
                TextRenderer.DrawText(g, "\uE706", iconFont, new Point(x, y), Color.White);
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    // ================== Unified OSD (Windows Native Style - Modified) ==================
    public class UnifiedOsdForm : Form
    {
        private System.Windows.Forms.Timer _timer;
        private List<MonitorInfo> _monitors;

        // 颜色配置
        private readonly Color _bgColor = Color.FromArgb(25, 25, 25); 
        private readonly Color _borderColor = Color.FromArgb(55, 55, 55);
        private readonly Color _textColor = Color.FromArgb(221, 221, 221); // #DDDDDD
        private readonly Color _trackColor = Color.FromArgb(65, 65, 65);   
        private readonly Color _fillColor = Color.FromArgb(0, 120, 212);   
        
        public UnifiedOsdForm(List<MonitorInfo> monitors)
        {
            _monitors = monitors;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.BackColor = _bgColor; 
            this.DoubleBuffered = true;
            this.StartPosition = FormStartPosition.Manual;
            
            // 高度计算
            int itemHeight = 56; 
            int totalHeight = 20 + (monitors.Count * itemHeight);
            
            this.Size = new Size(360, totalHeight);
            this.Region = Region.FromHrgn(NativeMethods.CreateRoundRectRgn(0, 0, Width, Height, 18, 18));
            
            _timer = new System.Windows.Forms.Timer { Interval = 1500 };
            _timer.Tick += (s, e) => this.Hide();
        }

        protected override bool ShowWithoutActivation => true;

        public void UpdateDisplay()
        {
            var screen = Screen.FromPoint(Cursor.Position);
            this.Location = new Point(
                screen.Bounds.X + (screen.Bounds.Width - Width) / 2,
                screen.Bounds.Bottom - this.Height - 120
            );

            if (!this.Visible) this.Show();
            this.Refresh();
            _timer.Stop();
            _timer.Start();
        }

        // 更新显示器列表（用于隐藏状态变化后）
        public void UpdateMonitors(List<MonitorInfo> monitors)
        {
            _monitors = monitors;
            // 重新计算高度
            int itemHeight = 56;
            int totalHeight = 20 + (monitors.Count * itemHeight);
            this.Size = new Size(360, totalHeight);
            this.Region = Region.FromHrgn(NativeMethods.CreateRoundRectRgn(0, 0, Width, Height, 18, 18));
            this.Refresh();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // 绘制边框
            using (var borderPen = new Pen(_borderColor, 1)) {
                Rectangle rect = this.ClientRectangle;
                rect.Width -= 1; rect.Height -= 1;
                g.DrawRectangle(borderPen, rect);
            }

            int paddingX = 20;    
            int itemH = 56;       
            int startY = 10;      
            
            // 字体 9f Bold, 数值颜色 Cyan
            using (Font nameFont = new Font("Segoe UI", 9f, FontStyle.Bold)) 
            using (Font valFont = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (Brush textBrush = new SolidBrush(_textColor))
            using (Brush valBrush = new SolidBrush(Color.Cyan)) 
            using (Brush trackBrush = new SolidBrush(_trackColor))
            using (Brush fillBrush = new SolidBrush(_fillColor))
            using (StringFormat alignLeft = new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter })
            using (StringFormat alignRight = new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Far })
            {
                int currentY = startY;

                foreach (var m in _monitors)
                {
                    // 布局计算
                    Rectangle nameRect = new Rectangle(paddingX, currentY, 110, itemH);
                    Rectangle valRect = new Rectangle(this.Width - paddingX - 45, currentY, 45, itemH);
                    
                    int barLeft = nameRect.Right + 5;
                    int barRight = valRect.Left - 5;
                    int barWidth = barRight - barLeft;
                    int barHeight = 6; 
                    int barTop = currentY + (itemH - barHeight) / 2;

                    // 绘制名称
                    g.DrawString(m.Name, nameFont, textBrush, nameRect, alignLeft);

                    // 绘制进度条背景
                    FillRoundedRectangle(g, trackBrush, barLeft, barTop, barWidth, barHeight, barHeight / 2);

                    // 绘制进度条前景
                    int fillW = (int)(barWidth * (m.LastBrightness / 100.0f));
                    if (fillW < barHeight) fillW = (m.LastBrightness > 0) ? barHeight : 0; 
                    if (fillW > barWidth) fillW = barWidth;

                    if (fillW > 0)
                    {
                        FillRoundedRectangle(g, fillBrush, barLeft, barTop, fillW, barHeight, barHeight / 2);
                    }

                    // 绘制数值
                    g.DrawString(m.LastBrightness.ToString(), valFont, valBrush, valRect, alignRight);

                    currentY += itemH;
                }
            }
        }
        
        private void FillRoundedRectangle(Graphics g, Brush brush, float x, float y, float w, float h, float r) {
            if (r > h / 2) r = h / 2;
            if (r > w / 2) r = w / 2;
            using (GraphicsPath path = new GraphicsPath()) {
                path.AddArc(x, y, r * 2, r * 2, 180, 90);
                path.AddLine(x + r, y, x + w - r, y);
                path.AddArc(x + w - 2 * r, y, 2 * r, 2 * r, 270, 90);
                path.AddLine(x + w, y + r, x + w, y + h - r);
                path.AddArc(x + w - 2 * r, y + h - 2 * r, 2 * r, 2 * r, 0, 90);
                path.AddLine(x + w - r, y + h, x + r, y + h);
                path.AddArc(x, y + h - 2 * r, 2 * r, 2 * r, 90, 90);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }
    }

    // ================== HelpForm (Modified) ==================

    public class HelpForm : Form {
        public HelpForm() {
            this.Text = "使用说明"; // 修改标题
            this.Size = new Size(500, 420); 
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog; this.MaximizeBox = false; this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(31, 31, 31); this.ForeColor = Color.White;
            
            Label title = new Label { Text = "HM's Simple Brightness Tool", Top = 20, Left = 20, AutoSize = true, Font = new Font("Segoe UI", 14, FontStyle.Bold) };
            
            TextBox info = new TextBox { 
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Top = 60, Left = 20, Width = 440, Height = 260,
                BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.LightGray, BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9.5f),
                // 精简后的文案
                Text = 
@"【快捷操作】
• 滚轮调节：鼠标悬停托盘图标，滚动调整亮度。
• 中键同步：对着图标按中键，强制同步所有屏幕。

【特色功能】
• 曲线编辑：在控制中心自定义亮度映射，解决副屏“太暗/太亮”的非线性问题。
• 电源模式：支持 DDC/CI (硬关机) 与 Windows API (软黑屏)。

【常见问题】
• 调节卡顿：请在设置中调高“响应延迟” (推荐 200ms+)。
• 无法控制：部分显示器需在 OSD 菜单开启 DDC/CI 支持。"
            };
            info.SelectionLength = 0;
            
            Button btnOk = new Button { Text = "明白", Top = 330, Left = 360, Width = 100, Height = 35, BackColor = Color.Teal, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.OK };
            
            this.Controls.Add(title); this.Controls.Add(info); this.Controls.Add(btnOk);
        }
    }

    public class SettingsForm : Form { 
        public SettingsForm(AppConfig config) { 
            this.Text = "设置"; this.Size = new Size(350, 480); 
            this.StartPosition = FormStartPosition.CenterScreen; this.FormBorderStyle = FormBorderStyle.FixedDialog; this.MaximizeBox = false; 
            
            FlowLayoutPanel panel = new FlowLayoutPanel { 
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, 
                Padding = new Padding(20), Width = 400 
            };
            this.Controls.Add(panel);
            
            CheckBox chkAuto = new CheckBox { Text = "开机自动启动", AutoSize = true, Checked = IsAutoStart(), Font = new Font("Microsoft YaHei UI", 9), Margin = new Padding(0, 0, 0, 15) }; 
            
            Label lblStep = new Label { Text = "滚轮步长 (1-20):", AutoSize = true, Font = new Font("Microsoft YaHei UI", 9) }; 
            NumericUpDown numStep = new NumericUpDown { Minimum = 1, Maximum = 20, Width = 100, Value = Math.Clamp(config.ScrollStep, 1, 20), Margin = new Padding(0, 5, 0, 15) }; 
            
            Label lblDelay = new Label { Text = "调节响应延迟 (防卡顿 ms):", AutoSize = true, Font = new Font("Microsoft YaHei UI", 9) }; 
            NumericUpDown numDelay = new NumericUpDown { Minimum = 0, Maximum = 2000, Width = 100, Value = Math.Clamp(config.DebounceTime, 0, 2000), Margin = new Padding(0, 5, 0, 15) }; 

            Label lblPower = new Label { Text = "电源按钮模式:", AutoSize = true, Font = new Font("Microsoft YaHei UI", 9) };
            ComboBox cmbPower = new ComboBox { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 5, 0, 15) };
            cmbPower.Items.Add("DDC/CI (硬件指令 - 推荐)");
            cmbPower.Items.Add("Windows API (软件信号 - 兼容)");
            cmbPower.SelectedIndex = config.UseSoftwarePower ? 1 : 0;

            Button btnClearHidden = new Button { Text = $"重置隐藏显示器 ({config.HiddenMonitors.Count})", Width = 300, Height = 40, Margin = new Padding(0, 10, 0, 10) }; 
            btnClearHidden.Click += (s, e) => { config.HiddenMonitors.Clear(); MessageBox.Show("已重置，请重新扫描或重启软件。", "提示"); }; 
            
            Button btnOk = new Button { Text = "保存设置", Width = 120, Height = 40, DialogResult = DialogResult.OK, BackColor = Color.Teal, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(120, 10, 0, 0) }; 
            btnOk.Click += (s, e) => { 
                config.ScrollStep = (int)numStep.Value; 
                config.DebounceTime = (int)numDelay.Value; 
                config.UseSoftwarePower = (cmbPower.SelectedIndex == 1);
                SetAutoStart(chkAuto.Checked); 
                this.Close(); 
            }; 
            
            panel.Controls.AddRange(new Control[] { chkAuto, lblStep, numStep, lblDelay, numDelay, lblPower, cmbPower, btnClearHidden, btnOk });
        } 
        private bool IsAutoStart() { using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false)) return key?.GetValue("SimpleBrightness") != null; } 
        private void SetAutoStart(bool enable) { using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)) { if (enable) key?.SetValue("SimpleBrightness", Application.ExecutablePath); else key?.DeleteValue("SimpleBrightness", false); } } 
    }

    public class BrightnessForm : Form
    {
        private List<MonitorInfo> _monitors; private AppConfig _config; private MyCustomApplicationContext _context; private Dictionary<string, TrackBar> _sliders = new Dictionary<string, TrackBar>(); private Dictionary<string, Label> _valLabels = new Dictionary<string, Label>(); private FlowLayoutPanel _mainPanel;
        
        // 检查显示器是否被隐藏，支持新旧ID格式兼容
        private bool IsMonitorHidden(MonitorInfo m, int index) {
            // 先检查新ID
            if (_config.HiddenMonitors.Contains(m.UniqueId)) return true;
            // 检查旧ID格式 (DDC_{hash}_IDX_{i})
            if (m.UniqueId.StartsWith("DDC_") && m.UniqueId.Contains("_H")) {
                string nameHash = m.UniqueId.Split('_')[1];
                string oldId = $"DDC_{nameHash}_IDX_{index}";
                if (_config.HiddenMonitors.Contains(oldId)) {
                    // 迁移：将旧ID添加到新ID
                    _config.HiddenMonitors.Add(m.UniqueId);
                    return true;
                }
            }
            return false;
        }
        
        public BrightnessForm(List<MonitorInfo> monitors, AppConfig config, MyCustomApplicationContext context, ContextMenuStrip menu) {
            _monitors = monitors; _config = config; _context = context; this.FormBorderStyle = FormBorderStyle.None; this.ShowInTaskbar = false; this.BackColor = Color.FromArgb(31, 31, 31); this.StartPosition = FormStartPosition.Manual; this.TopMost = true; this.AutoSize = true; this.AutoSizeMode = AutoSizeMode.GrowAndShrink; this.Padding = new Padding(2);
            this.Deactivate += (s, e) => { if (Application.OpenForms.OfType<CurveEditorForm>().Any() || Application.OpenForms.OfType<SettingsForm>().Any() || Application.OpenForms.OfType<InputBox>().Any() || Application.OpenForms.OfType<HelpForm>().Any()) return; if (!this.Bounds.Contains(Cursor.Position)) this.Hide(); else this.Activate(); };
            _mainPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10), BackColor = Color.FromArgb(31, 31, 31), MaximumSize = new Size(500, 2000) }; this.Controls.Add(_mainPanel);
            Panel header = new Panel { Size = new Size(450, 50), Margin = new Padding(0, 0, 0, 5) }; Label title = new Label { Text = "Control Center", Location = new Point(5, 10), ForeColor = Color.White, Font = new Font("Segoe UI", 14, FontStyle.Bold), AutoSize = true }; Button btnMenu = new Button { Text = "☰", Location = new Point(410, 5), Size = new Size(35, 35), FlatStyle = FlatStyle.Flat, ForeColor = Color.White, Cursor = Cursors.Hand }; btnMenu.FlatAppearance.BorderSize = 0; btnMenu.Click += (s, e) => menu.Show(Cursor.Position); header.Controls.Add(title); header.Controls.Add(btnMenu); _mainPanel.Controls.Add(header);
            var visibleMonitors = new List<MonitorInfo>();
            int idx = 0;
            foreach (var m in _monitors) {
                if (!IsMonitorHidden(m, idx)) visibleMonitors.Add(m);
                idx++;
            }
            if (visibleMonitors.Count == 0) visibleMonitors = _monitors; 
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

    public class CurveEditorForm : Form
    {
        private MonitorInfo _monitor; private AppConfig _config; private Dictionary<int, int> _currentPoints; private FlowLayoutPanel _panel;
        
        // 获取曲线配置，支持新旧ID格式兼容
        private Dictionary<int, int> GetCurveWithFallback() {
            // 先尝试新ID
            if (_config.Curves.TryGetValue(_monitor.UniqueId, out var curve)) 
                return curve;
            
            // 尝试旧ID格式 (DDC_{hash}_IDX_{i})
            if (_monitor.UniqueId.StartsWith("DDC_") && _monitor.UniqueId.Contains("_H")) {
                string[] parts = _monitor.UniqueId.Split('_');
                if (parts.Length >= 4) {
                    string nameHash = parts[1];
                    // 从UniqueId中提取索引 (格式: DDC_{hash}_H{handle}_IDX_{i})
                    string idxStr = parts[parts.Length - 1];
                    string oldId = $"DDC_{nameHash}_IDX_{idxStr}";
                    if (_config.Curves.TryGetValue(oldId, out var oldCurve)) {
                        // 迁移：复制到新ID
                        _config.Curves[_monitor.UniqueId] = oldCurve;
                        return oldCurve;
                    }
                }
            }
            // 返回默认曲线
            return new Dictionary<int, int> { { 0, 0 }, { 100, 100 } };
        }
        
        public CurveEditorForm(MonitorInfo monitor, AppConfig config) {
            _monitor = monitor; _config = config; _currentPoints = new Dictionary<int, int>(GetCurveWithFallback());
            this.Size = new Size(850, 500); this.BackColor = Color.FromArgb(31, 31, 31); this.StartPosition = FormStartPosition.CenterScreen; this.Text = "Curve Editor";
            Panel top = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(25, 25, 25) };
            Label title = new Label { Text = $"编辑: {monitor.Name}", Location = new Point(15, 18), AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
            NumericUpDown num = new NumericUpDown { Value = 50, Width = 80, Location = new Point(350, 13), Font = new Font("Segoe UI", 10) };
            Button add = new Button { Text = "添加节点", Width = 110, Height = 34, Location = new Point(440, 13), BackColor = Color.Gray, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            Button save = new Button { Text = "保存并生效", Width = 120, Height = 34, Location = new Point(700, 13), BackColor = Color.Teal, ForeColor = Color.White, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat };
            add.Click += (s, e) => { int x = (int)num.Value; if (!_currentPoints.ContainsKey(x)) { _currentPoints[x] = x; RefreshSliders(); }};
            save.Click += (s, e) => { 
                _config.Curves[_monitor.UniqueId] = new Dictionary<int, int>(_currentPoints); 
                // 清理旧ID的曲线配置（如果存在）
                if (_monitor.UniqueId.StartsWith("DDC_") && _monitor.UniqueId.Contains("_H")) {
                    string[] parts = _monitor.UniqueId.Split('_');
                    if (parts.Length >= 4) {
                        string nameHash = parts[1];
                        string idxStr = parts[parts.Length - 1];
                        string oldId = $"DDC_{nameHash}_IDX_{idxStr}";
                        _config.Curves.Remove(oldId);
                    }
                }
                _config.Save(); 
                this.Close(); 
            };
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
                 Label lockLbl = new Label { Text = "🔒", Top = panelH - btnH - 5, Width = 70, Height = 20, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8) };
                 bottomCtrl = lockLbl;
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

    public class InputBox : Form { public string ResultText { get; private set; } = ""; public InputBox(string title, string prompt, string defaultText) { this.Size = new Size(300, 180); this.Text = title; this.StartPosition = FormStartPosition.CenterScreen; this.FormBorderStyle = FormBorderStyle.FixedDialog; Label l = new Label { Text = prompt, Top = 20, Left = 20, AutoSize = true }; TextBox t = new TextBox { Text = defaultText, Top = 50, Left = 20, Width = 240 }; Button b = new Button { Text = "确定", Top = 90, Left = 180, DialogResult = DialogResult.OK }; b.Click += (s, e) => { ResultText = t.Text; this.Close(); }; this.Controls.AddRange(new Control[] { l, t, b }); this.AcceptButton = b; } }
    
    // OsdForm (Unified Placeholder for old ref, actual logic is in UnifiedOsdForm above)
    public class OsdForm : Form { public OsdForm(string n){} public void UpdateName(string n){} public void ShowOSD(int v, bool d, int r, int x, int y){} }
}

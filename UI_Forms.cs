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
    // ================== Theme Colors ==================
    public static class ThemeColors
    {
        // 获取系统主题色（Windows强调色）
        public static Color GetAccentColor()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("AccentColor");
                        if (value is int colorValue)
                        {
                            // Windows存储的是ABGR格式，需要转换为ARGB
                            byte a = (byte)((colorValue >> 24) & 0xFF);
                            byte b = (byte)((colorValue >> 16) & 0xFF);
                            byte g = (byte)((colorValue >> 8) & 0xFF);
                            byte r = (byte)(colorValue & 0xFF);
                            return Color.FromArgb(a, r, g, b);
                        }
                    }
                }
            }
            catch { }
            // 默认返回Windows 11默认蓝色
            return Color.FromArgb(0, 120, 212);
        }
        
        // 根据主题色生成填充色（更淡的版本）
        public static Color GetFillColor(Color accent)
        {
            return Color.FromArgb(
                Math.Min(255, accent.R + 80),
                Math.Min(255, accent.G + 80),
                Math.Min(255, accent.B + 120)
            );
        }
        
        // 背景色
        public static Color Background { get; } = Color.FromArgb(40, 40, 40);
        public static Color BackgroundDark { get; } = Color.FromArgb(35, 35, 38);
        public static Color CardBackground { get; } = Color.FromArgb(50, 50, 55);
        public static Color TrackColor { get; } = Color.FromArgb(100, 100, 105);
        public static Color TextColor { get; } = Color.FromArgb(220, 220, 220);
        public static Color TextSecondary { get; } = Color.FromArgb(180, 180, 180);
        
        // 圆角半径
        public const int CornerRadiusSmall = 12;
        public const int CornerRadiusMedium = 16;
        public const int CornerRadiusLarge = 20;
        
        // 轨道高度
        public const int TrackHeight = 6;
    }

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
                // 添加垂直偏移修正，解决图标偏上问题
                int y = (iconSize - textSize.Height) / 2 + 2;
                TextRenderer.DrawText(g, "\uE706", iconFont, new Point(x, y), Color.White);
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    // ================== Modern Vertical Slider (for Curve Editor) ==================
    public class ModernVerticalSlider : Panel
    {
        private int _value = 50;
        private int _minimum = 0;
        private int _maximum = 100;
        private bool _isDragging = false;
        private bool _isHovering = false;
        private Color _accentColor;
        private Color _fillColor;
        
        public Color TrackColor { get; set; }
        public Color FillColor { get => _fillColor; set => _fillColor = value; }
        public Color ThumbColor { get; set; } = Color.White;
        
        public int Value 
        { 
            get => _value; 
            set 
            { 
                _value = Math.Max(_minimum, Math.Min(_maximum, value));
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        
        public int Minimum { get => _minimum; set { _minimum = value; Invalidate(); } }
        public int Maximum { get => _maximum; set { _maximum = value; Invalidate(); } }
        
        public event EventHandler? ValueChanged;
        
        public ModernVerticalSlider()
        {
            this.Width = 20;
            this.DoubleBuffered = true;
            this.Cursor = Cursors.Hand;
            _accentColor = ThemeColors.GetAccentColor();
            _fillColor = ThemeColors.GetFillColor(_accentColor);
            TrackColor = ThemeColors.TrackColor;
        }
        
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            
            int trackWidth = ThemeColors.TrackHeight; // 使用主题轨道高度
            int thumbSize = _isHovering || _isDragging ? 14 : 12;
            int centerX = this.Width / 2;
            
            // 计算滑块位置（垂直方向，从上到下）
            float percent = 1.0f - (float)(_value - _minimum) / (_maximum - _minimum);
            int thumbY = (int)(percent * (this.Height - thumbSize)) + thumbSize / 2;
            
            // 绘制轨道背景
            using (var trackBrush = new SolidBrush(TrackColor))
            {
                var trackRect = new Rectangle(centerX - trackWidth / 2, 0, trackWidth, this.Height);
                g.FillRectangle(trackBrush, trackRect);
            }
            
            // 绘制填充部分（从底部到滑块位置）
            using (var fillBrush = new SolidBrush(_fillColor))
            {
                var fillRect = new Rectangle(centerX - trackWidth / 2, thumbY, trackWidth, this.Height - thumbY);
                g.FillRectangle(fillBrush, fillRect);
            }
            
            // 绘制滑块（圆点）
            var thumbRect = new Rectangle(centerX - thumbSize / 2, thumbY - thumbSize / 2, thumbSize, thumbSize);
            using (var thumbBrush = new SolidBrush(ThumbColor))
            {
                g.FillEllipse(thumbBrush, thumbRect);
            }
        }
        
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                UpdateValueFromMouse(e.Y);
            }
        }
        
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _isHovering = true;
            if (_isDragging)
            {
                UpdateValueFromMouse(e.Y);
            }
        }
        
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isDragging)
            {
                _isDragging = false;
                Invalidate();
            }
        }
        
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovering = false;
            Invalidate();
        }
        
        private void UpdateValueFromMouse(int mouseY)
        {
            int thumbSize = 12;
            float percent = 1.0f - (float)(mouseY - thumbSize / 2) / (this.Height - thumbSize);
            percent = Math.Max(0, Math.Min(1, percent));
            int newValue = (int)(_minimum + percent * (_maximum - _minimum));
            if (newValue != _value)
            {
                _value = newValue;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    // ================== Modern Slider Control ==================
    public class ModernSlider : Panel
    {
        private int _value = 50;
        private int _minimum = 0;
        private int _maximum = 100;
        private bool _isDragging = false;
        private bool _isHovering = false;
        private Color _accentColor;
        private Color _fillColor;
        
        // 颜色配置
        public Color TrackColor { get; set; }
        public Color FillColor { get => _fillColor; set => _fillColor = value; }
        public Color ThumbColor { get; set; } = Color.White;
        public Color ThumbHoverColor { get; set; } = Color.FromArgb(220, 220, 255);
        
        public int Value 
        { 
            get => _value; 
            set 
            { 
                _value = Math.Max(_minimum, Math.Min(_maximum, value));
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        
        public int Minimum { get => _minimum; set { _minimum = value; Invalidate(); } }
        public int Maximum { get => _maximum; set { _maximum = value; Invalidate(); } }
        
        public event EventHandler? ValueChanged;
        public event EventHandler? UserChangedValue;
        
        public ModernSlider()
        {
            this.Height = 28; // 稍微增加高度以适应更大的轨道
            this.DoubleBuffered = true;
            this.Cursor = Cursors.Hand;
            _accentColor = ThemeColors.GetAccentColor();
            _fillColor = ThemeColors.GetFillColor(_accentColor);
            TrackColor = ThemeColors.TrackColor;
        }
        
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            
            int trackHeight = ThemeColors.TrackHeight; // 使用主题轨道高度
            int thumbSize = _isHovering || _isDragging ? 16 : 14;
            int centerY = this.Height / 2;
            
            // 计算滑块位置
            float percent = (float)(_value - _minimum) / (_maximum - _minimum);
            int thumbX = (int)(percent * (this.Width - thumbSize)) + thumbSize / 2;
            
            // 绘制轨道背景
            using (var trackBrush = new SolidBrush(TrackColor))
            {
                var trackRect = new Rectangle(0, centerY - trackHeight / 2, this.Width, trackHeight);
                g.FillRectangle(trackBrush, trackRect);
            }
            
            // 绘制填充部分
            using (var fillBrush = new SolidBrush(_fillColor))
            {
                var fillRect = new Rectangle(0, centerY - trackHeight / 2, thumbX, trackHeight);
                g.FillRectangle(fillBrush, fillRect);
            }
            
            // 绘制滑块（圆点）
            var thumbRect = new Rectangle(thumbX - thumbSize / 2, centerY - thumbSize / 2, thumbSize, thumbSize);
            using (var thumbBrush = new SolidBrush(_isHovering || _isDragging ? ThumbHoverColor : ThumbColor))
            {
                g.FillEllipse(thumbBrush, thumbRect);
            }
            // 滑块阴影效果
            using (var shadowPen = new Pen(Color.FromArgb(40, 0, 0, 0), 1))
            {
                g.DrawEllipse(shadowPen, thumbRect.X + 1, thumbRect.Y + 1, thumbSize - 2, thumbSize - 2);
            }
        }
        
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                UpdateValueFromMouse(e.X);
            }
        }
        
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool wasHovering = _isHovering;
            _isHovering = true;
            if (wasHovering != _isHovering) Invalidate();
            
            if (_isDragging)
            {
                UpdateValueFromMouse(e.X);
            }
        }
        
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isDragging)
            {
                _isDragging = false;
                UserChangedValue?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }
        
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovering = false;
            Invalidate();
        }
        
        private void UpdateValueFromMouse(int mouseX)
        {
            int thumbSize = 12;
            float percent = (float)(mouseX - thumbSize / 2) / (this.Width - thumbSize);
            percent = Math.Max(0, Math.Min(1, percent));
            int newValue = (int)(_minimum + percent * (_maximum - _minimum));
            if (newValue != _value)
            {
                _value = newValue;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    // ================== Unified OSD (Modern Style with System Accent Color) ==================
    public class UnifiedOsdForm : Form
    {
        private System.Windows.Forms.Timer _timer;
        private List<MonitorInfo> _monitors;
        private Color _accentColor;
        private Color _fillColor;

        // 颜色配置 - 使用系统主题色
        private Color _bgColor; 
        private Color _borderColor;
        private Color _textColor;
        private Color _trackColor;
        
        public UnifiedOsdForm(List<MonitorInfo> monitors)
        {
            _monitors = monitors;
            _accentColor = ThemeColors.GetAccentColor();
            _fillColor = ThemeColors.GetFillColor(_accentColor);
            _bgColor = ThemeColors.Background;
            _borderColor = Color.FromArgb(70, 70, 75);
            _textColor = ThemeColors.TextColor;
            _trackColor = ThemeColors.TrackColor;
            
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.BackColor = _bgColor; 
            this.DoubleBuffered = true;
            this.StartPosition = FormStartPosition.Manual;
            
            // 高度计算 - 更紧凑的布局
            int itemHeight = 40; 
            int totalHeight = 16 + (monitors.Count * itemHeight);
            
            this.Size = new Size(300, totalHeight);
            // 加大圆角
            this.Region = Region.FromHrgn(NativeMethods.CreateRoundRectRgn(0, 0, Width, Height, ThemeColors.CornerRadiusLarge, ThemeColors.CornerRadiusLarge));
            
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
            int itemHeight = 40;
            int totalHeight = 16 + (monitors.Count * itemHeight);
            this.Size = new Size(300, totalHeight);
            this.Region = Region.FromHrgn(NativeMethods.CreateRoundRectRgn(0, 0, Width, Height, ThemeColors.CornerRadiusLarge, ThemeColors.CornerRadiusLarge));
            this.Invalidate(true);
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

            int paddingX = 16;    
            int itemH = 40;       
            int startY = 8;      
            int iconSize = 16;
            int valWidth = 32;   // 数值宽度
            int spacing = 12;    // 图标与轨道、轨道与文字的间距相等
            
            // 计算轨道位置：使图标、轨道、文字间距相等
            int iconX = paddingX;
            int barLeft = iconX + iconSize + spacing;
            int barRight = this.Width - paddingX - valWidth - spacing;
            int barWidth = barRight - barLeft;
            int barHeight = ThemeColors.TrackHeight;
            
            using (Font iconFont = new Font("Segoe MDL2 Assets", 11f))
            // 亮度值：缩小两个字号(11->9)并加粗
            using (Font valFont = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (Brush textBrush = new SolidBrush(_textColor))
            using (Brush valBrush = new SolidBrush(_textColor))
            using (Brush trackBrush = new SolidBrush(_trackColor))
            using (Brush fillBrush = new SolidBrush(_fillColor))
            using (StringFormat alignRight = new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Far })
            {
                int currentY = startY;

                foreach (var m in _monitors)
                {
                    int iconY = currentY + (itemH - iconSize) / 2;
                    int barTop = currentY + (itemH - barHeight) / 2;

                    // 绘制亮度图标 - 使用小太阳图标 \uE706 旁边的小图标 \uE9A1 (小太阳光线较短)
                    // 使用 \uE706 的替代图标 \uE9A1 或者保持 \uE706 但调整大小
                    g.DrawString("\uE706", iconFont, textBrush, iconX, iconY);

                    // 绘制进度条背景
                    FillRoundedRectangle(g, trackBrush, barLeft, barTop, barWidth, barHeight, barHeight / 2);

                    // 计算填充宽度
                    float percent = m.LastBrightness / 100.0f;
                    int fillW = (int)(barWidth * percent);
                    if (fillW < 0) fillW = 0; 
                    if (fillW > barWidth) fillW = barWidth;

                    // 绘制进度条填充
                    if (fillW > 0)
                    {
                        FillRoundedRectangle(g, fillBrush, barLeft, barTop, fillW, barHeight, barHeight / 2);
                    }

                    // 移除圆点滑块，只显示进度条

                    // 绘制数值 - 位置调整使间距相等
                    Rectangle valRect = new Rectangle(barRight + spacing, currentY, valWidth, itemH);
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
            btnClearHidden.Click += (s, e) => { config.HiddenMonitors.Clear(); MessageBox.Show("已重置，请重启软件。", "提示"); }; 
            
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
        private List<MonitorInfo> _monitors; private AppConfig _config; private MyCustomApplicationContext _context; private Dictionary<string, ModernSlider> _sliders = new Dictionary<string, ModernSlider>(); private Dictionary<string, Label> _valLabels = new Dictionary<string, Label>(); private FlowLayoutPanel _mainPanel;
        
        // 检查显示器是否被隐藏，支持新旧ID格式兼容
        private bool IsMonitorHidden(MonitorInfo m, int index) {
            // 先检查新ID
            if (_config.HiddenMonitors.Contains(m.UniqueId)) return true;
            
            if (m.UniqueId.StartsWith("DDC_") && m.UniqueId.Contains("_H")) {
                string[] parts = m.UniqueId.Split('_');
                if (parts.Length >= 4) {
                    string nameHash = parts[1];
                    
                    // 检查旧ID格式 (DDC_{hash}_IDX_{i})
                    string oldId = $"DDC_{nameHash}_IDX_{index}";
                    if (_config.HiddenMonitors.Contains(oldId)) {
                        // 迁移：将旧ID添加到新ID
                        _config.HiddenMonitors.Add(m.UniqueId);
                        return true;
                    }
                    
                    // 检查句柄变化后的ID (DDC_{hash}_H{oldHandle}_IDX_{i})
                    var hiddenList = _config.HiddenMonitors.ToList();
                    foreach (var hiddenId in hiddenList)
                    {
                        if (hiddenId.StartsWith($"DDC_{nameHash}_H") && hiddenId != m.UniqueId)
                        {
                            // 找到相同nameHash但不同句柄的隐藏配置，迁移到新ID
                            _config.HiddenMonitors.Remove(hiddenId);
                            _config.HiddenMonitors.Add(m.UniqueId);
                            return true;
                        }
                    }
                }
            }
            return false;
        }
        
        public BrightnessForm(List<MonitorInfo> monitors, AppConfig config, MyCustomApplicationContext context, ContextMenuStrip menu) {
            _monitors = monitors; _config = config; _context = context; 
            this.FormBorderStyle = FormBorderStyle.None; 
            this.ShowInTaskbar = false; 
            this.BackColor = ThemeColors.Background; 
            this.StartPosition = FormStartPosition.Manual; 
            this.TopMost = true; 
            this.AutoSize = true; 
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink; 
            this.Padding = new Padding(0);
            
            this.Deactivate += (s, e) => { if (Application.OpenForms.OfType<CurveEditorForm>().Any() || Application.OpenForms.OfType<SettingsForm>().Any() || Application.OpenForms.OfType<InputBox>().Any() || Application.OpenForms.OfType<HelpForm>().Any()) return; if (!this.Bounds.Contains(Cursor.Position)) this.Hide(); else this.Activate(); };
            
            _mainPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(24), BackColor = ThemeColors.Background, MaximumSize = new Size(500, 2000) }; 
            this.Controls.Add(_mainPanel);
            
            Panel header = new Panel { Size = new Size(400, 44), Margin = new Padding(0, 0, 0, 20) }; 
            Label title = new Label { Text = "Control Center", Location = new Point(0, 6), ForeColor = Color.White, Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true }; 
            Button btnMenu = new Button { Text = "\uE712", Location = new Point(368, 4), Size = new Size(32, 32), FlatStyle = FlatStyle.Flat, ForeColor = ThemeColors.TextSecondary, Cursor = Cursors.Hand, BackColor = Color.Transparent, Font = new Font("Segoe MDL2 Assets", 12) }; 
            btnMenu.FlatAppearance.BorderSize = 0; 
            btnMenu.Click += (s, e) => menu.Show(Cursor.Position); 
            header.Controls.Add(title); 
            header.Controls.Add(btnMenu); 
            _mainPanel.Controls.Add(header);
            
            var visibleMonitors = new List<MonitorInfo>();
            int idx = 0;
            foreach (var m in _monitors) {
                if (!IsMonitorHidden(m, idx)) visibleMonitors.Add(m);
                idx++;
            }
            if (visibleMonitors.Count == 0) visibleMonitors = _monitors; 
            
            foreach (var m in visibleMonitors) {
                // 显示器卡片容器
                FlowLayoutPanel card = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Width = 400, Padding = new Padding(0, 12, 0, 12), Margin = new Padding(0, 0, 0, 16) };
                
                // 第一行：图标 + 名称 + 亮度值
                Panel row1 = new Panel { Size = new Size(400, 28), Margin = new Padding(0, 0, 0, 10) };
                // 显示器图标
                Label lblIcon = new Label { Text = "\uE7F4", Location = new Point(0, 2), ForeColor = ThemeColors.TextColor, AutoSize = true, Font = new Font("Segoe MDL2 Assets", 14) };
                // 名称 - 缩小两个字号(11->9)并加粗
                Label lblName = new Label { Text = m.Name, Location = new Point(26, 4), ForeColor = ThemeColors.TextColor, AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand, MaximumSize = new Size(300, 30) }; 
                SetupNameMenu(lblName, m);
                // 亮度值 - 缩小两个字号(12->10)并加粗
                Label lblVal = new Label { Text = $"{m.LastBrightness}", Location = new Point(360, 4), ForeColor = ThemeColors.TextColor, AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) }; 
                _valLabels[m.UniqueId] = lblVal;
                row1.Controls.Add(lblIcon); 
                row1.Controls.Add(lblName); 
                row1.Controls.Add(lblVal); 
                card.Controls.Add(row1);
                
                // 第二行：亮度图标 + 滑块
                Panel row2 = new Panel { Size = new Size(400, 32), Margin = new Padding(0, 0, 0, 5) };
                Label brightnessIcon = new Label { Text = "\uE706", Location = new Point(0, 4), ForeColor = ThemeColors.TextSecondary, AutoSize = true, Font = new Font("Segoe MDL2 Assets", 12) };
                ModernSlider slider = new ModernSlider { Size = new Size(360, 28), Location = new Point(30, 0), Value = m.LastBrightness }; 
                _sliders[m.UniqueId] = slider;
                Action<int> updateLogic = (newVal) => { lblVal.Text = $"{newVal}"; _context.ApplyBrightness(m, newVal, false); };
                slider.ValueChanged += (s, e) => updateLogic(slider.Value);
                slider.UserChangedValue += (s, e) => { };
                row2.Controls.Add(brightnessIcon); 
                row2.Controls.Add(slider); 
                card.Controls.Add(row2);
                
                // 按钮行（仅DDC显示器）
                if (m.Type == MonitorType.DDC) { 
                    Panel btnRow = new Panel { Size = new Size(400, 36), Margin = new Padding(0, 8, 0, 0) }; 
                    // 编辑曲线按钮（图标按钮）
                    Button btnCurve = new Button { Text = "\uE70F", Location = new Point(320, 2), Size = new Size(32, 32), FlatStyle = FlatStyle.Flat, ForeColor = ThemeColors.TextSecondary, BackColor = Color.Transparent, Cursor = Cursors.Hand, Font = new Font("Segoe MDL2 Assets", 12) };
                    btnCurve.FlatAppearance.BorderSize = 0;
                    ToolTip tipCurve = new ToolTip(); 
                    tipCurve.SetToolTip(btnCurve, "编辑曲线");
                    btnCurve.Click += (s, e) => { var editor = new CurveEditorForm(m, _config); editor.Show(this); }; 
                    // 电源按钮（图标按钮）
                    Button btnPower = new Button { Text = "\uE7E8", Location = new Point(360, 2), Size = new Size(32, 32), FlatStyle = FlatStyle.Flat, ForeColor = ThemeColors.TextSecondary, BackColor = Color.Transparent, Cursor = Cursors.Hand, Font = new Font("Segoe MDL2 Assets", 12) };
                    btnPower.FlatAppearance.BorderSize = 0;
                    ToolTip tipPower = new ToolTip(); 
                    tipPower.SetToolTip(btnPower, "左键：开启\n右键：关闭");
                    btnPower.MouseDown += (s, e) => { Task.Run(() => BrightnessController.SetPowerState(m, e.Button == MouseButtons.Left, _config.UseSoftwarePower)); }; 
                    btnRow.Controls.Add(btnCurve); 
                    btnRow.Controls.Add(btnPower); 
                    card.Controls.Add(btnRow); 
                }
                
                if (m != visibleMonitors.Last()) { 
                    Panel div = new Panel { Size = new Size(400, 1), BackColor = Color.FromArgb(60, 60, 60), Margin = new Padding(0, 12, 0, 0) }; 
                    card.Controls.Add(div); 
                }
                _mainPanel.Controls.Add(card);
            }
        }
        
        protected override void OnLoad(EventArgs e) { 
            base.OnLoad(e); 
            var screen = Screen.FromPoint(Cursor.Position); 
            int x = screen.WorkingArea.Right - this.Width - 12; 
            int y = screen.WorkingArea.Bottom - this.Height - 12; 
            if (y < screen.WorkingArea.Top) y = screen.WorkingArea.Top + 50; 
            if (x < screen.WorkingArea.Left) x = screen.WorkingArea.Left + 10; 
            this.Location = new Point(x, y); 
            // 加大圆角
            this.Region = Region.FromHrgn(NativeMethods.CreateRoundRectRgn(0, 0, Width, Height, ThemeColors.CornerRadiusMedium, ThemeColors.CornerRadiusMedium)); 
        }
        private void SetupNameMenu(Label lbl, MonitorInfo m) { ContextMenuStrip menu = new ContextMenuStrip(); menu.Items.Add("重命名", null, (s, e) => { InputBox input = new InputBox("重命名", "输入新名称:", m.Name); if (input.ShowDialog() == DialogResult.OK) { _config.CustomNames[m.UniqueId] = input.ResultText; _config.Save(); _context.RefreshMonitors(); this.Close(); } }); menu.Items.Add("隐藏", null, (s, e) => { _config.HiddenMonitors.Add(m.UniqueId); _config.Save(); this.Close(); }); lbl.MouseClick += (s, e) => { if (e.Button == MouseButtons.Right) menu.Show(Cursor.Position); }; }
        public void UpdateSlider(string id, int val) { 
            // 先尝试通过ID匹配
            if (_sliders.ContainsKey(id)) { 
                _sliders[id].Value = val; 
                _valLabels[id].Text = val.ToString(); 
                return;
            }
            // 如果ID不匹配（可能是句柄变化导致的新ID），尝试通过索引匹配
            var allMonitors = _context.GetMonitors();
            for (int i = 0; i < allMonitors.Count; i++) {
                if (allMonitors[i].UniqueId == id) {
                    int visibleIndex = 0;
                    for (int j = 0; j < allMonitors.Count && j <= i; j++) {
                        if (!IsMonitorHidden(allMonitors[j], j)) {
                            if (j == i) {
                                var sliderKeys = _sliders.Keys.ToList();
                                if (visibleIndex < sliderKeys.Count) {
                                    string key = sliderKeys[visibleIndex];
                                    _sliders[key].Value = val;
                                    _valLabels[key].Text = val.ToString();
                                }
                                return;
                            }
                            visibleIndex++;
                        }
                    }
                    return;
                }
            }
        }
    }

    public class CurveEditorForm : Form
    {
        private MonitorInfo _monitor; private AppConfig _config; private Dictionary<int, int> _currentPoints; private FlowLayoutPanel _panel;
        
        // 获取曲线配置，支持新旧ID格式和句柄变化后的ID迁移
        private Dictionary<int, int> GetCurveWithFallback() {
            // 先尝试新ID
            if (_config.Curves.TryGetValue(_monitor.UniqueId, out var curve)) 
                return curve;
            
            if (_monitor.UniqueId.StartsWith("DDC_") && _monitor.UniqueId.Contains("_H")) {
                string[] parts = _monitor.UniqueId.Split('_');
                if (parts.Length >= 4) {
                    string nameHash = parts[1];
                    
                    // 尝试旧ID格式 (DDC_{hash}_IDX_{i})
                    string idxStr = parts[parts.Length - 1];
                    string oldId = $"DDC_{nameHash}_IDX_{idxStr}";
                    if (_config.Curves.TryGetValue(oldId, out var oldCurve)) {
                        // 迁移：复制到新ID
                        _config.Curves[_monitor.UniqueId] = oldCurve;
                        return oldCurve;
                    }
                    
                    // 尝试句柄变化后的ID (DDC_{hash}_H{oldHandle}_IDX_{i})
                    var curveKeys = _config.Curves.Keys.ToList();
                    foreach (var curveId in curveKeys)
                    {
                        if (curveId.StartsWith($"DDC_{nameHash}_H") && curveId != _monitor.UniqueId)
                        {
                            // 找到相同nameHash但不同句柄的曲线配置，迁移到新ID
                            var savedCurve = _config.Curves[curveId];
                            _config.Curves[_monitor.UniqueId] = savedCurve;
                            return savedCurve;
                        }
                    }
                }
            }
            // 返回默认曲线
            return new Dictionary<int, int> { { 0, 0 }, { 100, 100 } };
        }
        
        public CurveEditorForm(MonitorInfo monitor, AppConfig config) {
            _monitor = monitor; _config = config; _currentPoints = new Dictionary<int, int>(GetCurveWithFallback());
            var accentColor = ThemeColors.GetAccentColor();
            
            this.Size = new Size(800, 480); 
            this.BackColor = ThemeColors.Background; 
            this.StartPosition = FormStartPosition.CenterScreen; 
            this.Text = "曲线编辑器";
            this.FormBorderStyle = FormBorderStyle.None;
            // 加大圆角
            this.Region = Region.FromHrgn(NativeMethods.CreateRoundRectRgn(0, 0, Width, Height, ThemeColors.CornerRadiusMedium, ThemeColors.CornerRadiusMedium));
            
            // 顶部标题栏
            Panel top = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = ThemeColors.BackgroundDark };
            Label title = new Label { Text = $"编辑曲线: {monitor.Name}", Location = new Point(20, 16), AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 13, FontStyle.Bold) };
            
            // 关闭按钮（图标）
            Button btnClose = new Button { Text = "\uE711", Location = new Point(Width - 50, 12), Size = new Size(32, 32), FlatStyle = FlatStyle.Flat, ForeColor = ThemeColors.TextSecondary, BackColor = Color.Transparent, Cursor = Cursors.Hand, Font = new Font("Segoe MDL2 Assets", 12) };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => this.Close();
            
            top.Controls.AddRange(new Control[] { title, btnClose });
            this.Controls.Add(top);
            
            // 底部工具栏
            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 70, BackColor = ThemeColors.BackgroundDark };
            NumericUpDown num = new NumericUpDown { Value = 50, Width = 70, Location = new Point(20, 20), Font = new Font("Segoe UI", 11), BackColor = Color.FromArgb(50, 50, 50), ForeColor = Color.White, BorderStyle = BorderStyle.None };
            Button add = new Button { Text = "\uE710 添加", Width = 100, Height = 36, Location = new Point(100, 17), BackColor = Color.FromArgb(60, 60, 65), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10) };
            add.FlatAppearance.BorderSize = 0;
            Button save = new Button { Text = "\uE73E 保存", Width = 100, Height = 36, Location = new Point(Width - 130, 17), BackColor = accentColor, ForeColor = Color.White, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10) };
            save.FlatAppearance.BorderSize = 0;
            
            add.Click += (s, e) => { int x = (int)num.Value; if (!_currentPoints.ContainsKey(x)) { _currentPoints[x] = x; RefreshSliders(); }};
            save.Click += (s, e) => { 
                _config.Curves[_monitor.UniqueId] = new Dictionary<int, int>(_currentPoints); 
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
            bottom.Controls.AddRange(new Control[] { num, add, save });
            this.Controls.Add(bottom);
            
            _panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(20, 15, 0, 0), BackColor = ThemeColors.Background };
            this.Controls.Add(_panel); 
            _panel.BringToFront();
            RefreshSliders();
            
            // 支持拖动标题栏移动窗口
            top.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { this.Capture = false; NativeMethods.SendMessage(this.Handle, 0xA1, 0x2, 0); } };
        }
        private void RefreshSliders() { _panel.Controls.Clear(); if (!_currentPoints.ContainsKey(0)) _currentPoints[0]=0; if(!_currentPoints.ContainsKey(100)) _currentPoints[100]=100; foreach(var k in _currentPoints.Keys.OrderBy(x=>x)) _panel.Controls.Add(CreateItem(k, _currentPoints[k])); }
        private Control CreateItem(int x, int y) { 
             Panel p = new Panel { Width = 64, Height = 280, Margin = new Padding(6), BackColor = Color.FromArgb(50, 50, 55) };
             p.Region = Region.FromHrgn(NativeMethods.CreateRoundRectRgn(0, 0, p.Width, p.Height, 8, 8));
             
             // 实际亮度值（顶部）
             Label l = new Label { Text = y.ToString(), Top = 10, Width = 64, Height = 24, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(180, 180, 255), Font = new Font("Segoe UI", 12, FontStyle.Bold) };
             
             int panelH = 280; int labelH = 24; int btnH = 28; 
             // 软件亮度值（底部）
             Label k = new Label { Text = x + "%", Top = panelH - labelH - btnH - 8, Width = 64, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(200, 200, 200), Font = new Font("Segoe UI", 10) };
             
             Control bottomCtrl;
             if (x != 0 && x != 100) {
                 Button d = new Button { Text = "\uE74D", Top = panelH - btnH - 5, Left = 18, Width = 28, Height = 28, ForeColor = Color.FromArgb(255, 100, 100), FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent, Font = new Font("Segoe MDL2 Assets", 10) }; d.FlatAppearance.BorderSize = 0;
                 d.Click += (s, e) => { _currentPoints.Remove(x); RefreshSliders(); }; bottomCtrl = d;
             } else {
                 Label lockLbl = new Label { Text = "\uE72E", Top = panelH - btnH - 5, Width = 64, Height = 28, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(120, 120, 120), Font = new Font("Segoe MDL2 Assets", 10) };
                 bottomCtrl = lockLbl;
             }
             
             // 使用自定义垂直滑块
             int sliderTop = 40; int sliderH = (panelH - labelH - btnH - 8) - sliderTop - 8;
             ModernVerticalSlider t = new ModernVerticalSlider { Height = sliderH, Value = y, Top = sliderTop, Left = 22, Width = 20 };
             ToolTip tip = new ToolTip(); 
             t.ValueChanged += (s, e) => { _currentPoints[x] = t.Value; l.Text = t.Value.ToString(); };
             
             p.Controls.AddRange(new Control[]{l, t, k, bottomCtrl}); 
             return p;
        }
    }

    public class InputBox : Form { public string ResultText { get; private set; } = ""; public InputBox(string title, string prompt, string defaultText) { this.Size = new Size(300, 180); this.Text = title; this.StartPosition = FormStartPosition.CenterScreen; this.FormBorderStyle = FormBorderStyle.FixedDialog; Label l = new Label { Text = prompt, Top = 20, Left = 20, AutoSize = true }; TextBox t = new TextBox { Text = defaultText, Top = 50, Left = 20, Width = 240 }; Button b = new Button { Text = "确定", Top = 90, Left = 180, DialogResult = DialogResult.OK }; b.Click += (s, e) => { ResultText = t.Text; this.Close(); }; this.Controls.AddRange(new Control[] { l, t, b }); this.AcceptButton = b; } }
    
    // OsdForm (Unified Placeholder for old ref, actual logic is in UnifiedOsdForm above)
    public class OsdForm : Form { public OsdForm(string n){} public void UpdateName(string n){} public void ShowOSD(int v, bool d, int r, int x, int y){} }
}

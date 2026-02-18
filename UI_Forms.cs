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
    // ================== Theme Manager (Auto-detect System Theme) ==================
    public static class ThemeManager
    {
        private static bool _isDarkMode = true;
        
        public static bool IsDarkMode 
        { 
            get => _isDarkMode;
            set => _isDarkMode = value;
        }
        
        // Auto-detect system theme from Windows registry
        public static void AutoDetectTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        var value = key.GetValue("AppsUseLightTheme");
                        if (value is int lightTheme)
                        {
                            // AppsUseLightTheme = 0 means dark mode, 1 means light mode
                            _isDarkMode = lightTheme == 0;
                        }
                    }
                }
            }
            catch
            {
                // Default to dark mode if detection fails
                _isDarkMode = true;
            }
        }
        
        // Dark Mode Colors
        public static class Dark
        {
            public static readonly Color Background = Color.FromArgb(32, 32, 32);
            public static readonly Color Surface = Color.FromArgb(45, 45, 45);
            public static readonly Color Text = Color.FromArgb(255, 255, 255);
            public static readonly Color TextSecondary = Color.FromArgb(200, 200, 200);
            public static readonly Color Accent = Color.FromArgb(0, 120, 212);
            public static readonly Color Track = Color.FromArgb(80, 80, 80);
            public static readonly Color Border = Color.FromArgb(60, 60, 60);
            public static readonly Color Thumb = Color.FromArgb(200, 200, 200);
        }
        
        // Light Mode Colors
        public static class Light
        {
            public static readonly Color Background = Color.FromArgb(243, 243, 243);
            public static readonly Color Surface = Color.FromArgb(255, 255, 255);
            public static readonly Color Text = Color.FromArgb(0, 0, 0);
            public static readonly Color TextSecondary = Color.FromArgb(80, 80, 80);
            public static readonly Color Accent = Color.FromArgb(0, 95, 184);
            public static readonly Color Track = Color.FromArgb(200, 200, 200);
            public static readonly Color Border = Color.FromArgb(200, 200, 200);
            public static readonly Color Thumb = Color.FromArgb(100, 100, 100);
        }
        
        public static Color Background => IsDarkMode ? Dark.Background : Light.Background;
        public static Color Surface => IsDarkMode ? Dark.Surface : Light.Surface;
        public static Color Text => IsDarkMode ? Dark.Text : Light.Text;
        public static Color TextSecondary => IsDarkMode ? Dark.TextSecondary : Light.TextSecondary;
        public static Color Accent => IsDarkMode ? Dark.Accent : Light.Accent;
        public static Color Track => IsDarkMode ? Dark.Track : Light.Track;
        public static Color Border => IsDarkMode ? Dark.Border : Light.Border;
        public static Color Thumb => IsDarkMode ? Dark.Thumb : Light.Thumb;
    }

    // ================== Windows 11 Style Helper ==================
    public static class Windows11Style
    {
        public static bool IsWindows11() => Environment.OSVersion.Version.Build >= 22000;
        
        public static void ApplyWindowStyle(Form form, bool darkMode = true, bool mica = true)
        {
            if (!IsWindows11()) return;
            
            // Apply corner preference (rounded corners)
            int corner = 2; // DWMWCP_ROUND
            NativeMethods.DwmSetWindowAttribute(form.Handle, 33, ref corner, sizeof(int));
            
            // Apply dark mode
            int dark = darkMode ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
            
            // Apply Mica backdrop
            if (mica)
            {
                int backdrop = 2; // DWMSBT_MAINWINDOW
                NativeMethods.DwmSetWindowAttribute(form.Handle, 38, ref backdrop, sizeof(int));
            }
        }
        
        public static void ApplyAcrylic(Form form, bool darkMode = true)
        {
            if (!IsWindows11()) return;
            
            int corner = 2;
            NativeMethods.DwmSetWindowAttribute(form.Handle, 33, ref corner, sizeof(int));
            
            int dark = darkMode ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
            
            // Acrylic backdrop
            int backdrop = 3; // DWMSBT_TRANSIENTWINDOW
            NativeMethods.DwmSetWindowAttribute(form.Handle, 38, ref backdrop, sizeof(int));
        }
    }
    
    // ================== Native Windows Style TrackBar (Matching System) ==================
    public class Win11TrackBar : Control
    {
        private int _value = 50;
        private int _minimum = 0;
        private int _maximum = 100;
        private bool _isDragging = false;
        private Rectangle _trackRect;
        private Rectangle _thumbRect;
        private const int ThumbSize = 16; // Smaller thumb like Windows
        private const int TrackHeight = 3; // Thinner track
        
        public event EventHandler? ValueChanged;
        public event EventHandler? Scroll;
        
        public int Value
        {
            get => _value;
            set
            {
                int newValue = Math.Clamp(value, _minimum, _maximum);
                if (_value != newValue)
                {
                    _value = newValue;
                    UpdateThumbPosition();
                    Invalidate();
                    ValueChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        
        public int Minimum
        {
            get => _minimum;
            set { _minimum = value; Invalidate(); }
        }
        
        public int Maximum
        {
            get => _maximum;
            set { _maximum = value; Invalidate(); }
        }
        
        public Win11TrackBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | 
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 28;
            Cursor = Cursors.Hand;
        }
        
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateThumbPosition();
        }
        
        private void UpdateThumbPosition()
        {
            int trackWidth = Width - ThumbSize - 8;
            float ratio = _maximum > _minimum ? (float)(_value - _minimum) / (_maximum - _minimum) : 0;
            int thumbX = (int)(4 + ratio * trackWidth);
            _thumbRect = new Rectangle(thumbX, (Height - ThumbSize) / 2, ThumbSize, ThumbSize);
            _trackRect = new Rectangle(4, (Height - TrackHeight) / 2, Width - 8, TrackHeight);
        }
        
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            
            UpdateThumbPosition();
            
            // Draw track background (gray)
            using (var trackBrush = new SolidBrush(ThemeManager.Track))
            using (var trackPath = GetRoundedRect(_trackRect, TrackHeight / 2))
            {
                g.FillPath(trackBrush, trackPath);
            }
            
            // Draw track fill (accent color)
            if (_value > _minimum)
            {
                int fillWidth = (int)((float)(_value - _minimum) / (_maximum - _minimum) * _trackRect.Width);
                var fillRect = new Rectangle(_trackRect.X, _trackRect.Y, fillWidth, _trackRect.Height);
                using (var fillBrush = new SolidBrush(ThemeManager.Accent))
                using (var fillPath = GetRoundedRect(fillRect, TrackHeight / 2))
                {
                    g.FillPath(fillBrush, fillPath);
                }
            }
            
            // Draw thumb (circle with border)
            bool isHovering = ClientRectangle.Contains(PointToClient(Cursor.Position));
            Color thumbColor = _isDragging ? ThemeManager.Accent : 
                              isHovering ? ThemeManager.Thumb : ThemeManager.Thumb;
            
            // Draw thumb shadow
            using (var shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
            {
                var shadowRect = new Rectangle(_thumbRect.X + 1, _thumbRect.Y + 2, _thumbRect.Width, _thumbRect.Height);
                using (var shadowPath = GetCirclePath(shadowRect))
                {
                    g.FillPath(shadowBrush, shadowPath);
                }
            }
            
            // Draw thumb
            using (var thumbBrush = new SolidBrush(thumbColor))
            using (var thumbPath = GetCirclePath(_thumbRect))
            {
                g.FillPath(thumbBrush, thumbPath);
            }
            
            // Draw thumb border
            using (var borderPen = new Pen(Color.FromArgb(100, 100, 100), 1))
            using (var borderPath = GetCirclePath(_thumbRect))
            {
                g.DrawPath(borderPen, borderPath);
            }
        }
        
        private GraphicsPath GetRoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
        
        private GraphicsPath GetCirclePath(Rectangle rect)
        {
            var path = new GraphicsPath();
            path.AddEllipse(rect);
            return path;
        }
        
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                UpdateValueFromPosition(e.X);
                Scroll?.Invoke(this, EventArgs.Empty);
            }
        }
        
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isDragging)
            {
                UpdateValueFromPosition(e.X);
                Scroll?.Invoke(this, EventArgs.Empty);
            }
            Invalidate();
        }
        
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _isDragging = false;
            Invalidate();
        }
        
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Invalidate();
        }
        
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int change = e.Delta > 0 ? 1 : -1;
            Value = Math.Clamp(Value + change, Minimum, Maximum);
            Scroll?.Invoke(this, EventArgs.Empty);
        }
        
        private void UpdateValueFromPosition(int x)
        {
            int trackWidth = Width - ThumbSize - 8;
            float ratio = (float)(x - 4) / trackWidth;
            ratio = Math.Clamp(ratio, 0, 1);
            Value = (int)(Minimum + ratio * (Maximum - Minimum));
        }
        
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateThumbPosition();
            Invalidate();
        }
    }
    
    // ================== Windows 11 Style Button ==================
    public class Win11Button : Button
    {
        private bool _isHovering = false;
        private bool _isPressed = false;
        
        public Win11Button()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = ThemeManager.Accent;
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | 
                     ControlStyles.OptimizedDoubleBuffer, true);
        }
        
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            
            Color backColor = _isPressed ? Color.FromArgb(0, 100, 180) :
                             _isHovering ? Color.FromArgb(0, 130, 230) : BackColor;
            
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var brush = new SolidBrush(backColor))
            using (var path = GetRoundedRect(rect, 4))
            {
                g.FillPath(brush, path);
            }
            
            TextRenderer.DrawText(g, Text, Font, rect, ForeColor, 
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        
        private GraphicsPath GetRoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
        
        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovering = true;
            Invalidate();
        }
        
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovering = false;
            Invalidate();
        }
        
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _isPressed = true;
            Invalidate();
        }
        
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _isPressed = false;
            Invalidate();
        }
    }
    
    // ================== Icon ==================
    public static class IconDrawer {
        public static Icon DrawNativeIcon() {
            int iconSize = 48;
            int fontSize = 24;

            using (Bitmap bmp = new Bitmap(iconSize, iconSize))
            using (Graphics g = Graphics.FromImage(bmp)) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.Transparent);

                Font iconFont = new Font("Segoe MDL2 Assets", fontSize, FontStyle.Regular);
                Size textSize = TextRenderer.MeasureText("\uE706", iconFont);
                int x = (iconSize - textSize.Width) / 2;
                int y = (iconSize - textSize.Height) / 2 + 2;
                TextRenderer.DrawText(g, "\uE706", iconFont, new Point(x, y), Color.White);
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    // ================== Native Windows Style OSD ==================
    public class NativeOsdForm : Form
    {
        private System.Windows.Forms.Timer _timer;
        private List<MonitorInfo> _monitors;
        private int _displayIndex = 0;
        
        // Windows native OSD style colors (always light)
        private readonly Color _bgColor = Color.FromArgb(240, 240, 240);
        private readonly Color _textColor = Color.FromArgb(0, 0, 0);
        private readonly Color _trackColor = Color.FromArgb(200, 200, 200);
        private readonly Color _fillColor = Color.FromArgb(0, 120, 212);
        
        public NativeOsdForm(List<MonitorInfo> monitors)
        {
            _monitors = monitors;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.BackColor = _bgColor;
            this.DoubleBuffered = true;
            this.StartPosition = FormStartPosition.Manual;
            
            // Fixed size like Windows native OSD
            this.Size = new Size(340, 80);
            
            _timer = new System.Windows.Forms.Timer { Interval = 2000 };
            _timer.Tick += (s, e) => this.Hide();
        }

        protected override bool ShowWithoutActivation => true;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Apply rounded corners
            if (Windows11Style.IsWindows11())
            {
                int corner = 2;
                NativeMethods.DwmSetWindowAttribute(this.Handle, 33, ref corner, sizeof(int));
            }
            this.Region = Region.FromHrgn(NativeMethods.CreateRoundRectRgn(0, 0, Width, Height, 8, 8));
        }

        public void ShowForMonitor(int monitorIndex)
        {
            _displayIndex = monitorIndex;
            var screen = Screen.FromPoint(Cursor.Position);
            
            // Position at bottom center of screen like Windows OSD
            this.Location = new Point(
                screen.Bounds.X + (screen.Bounds.Width - Width) / 2,
                screen.Bounds.Bottom - Height - 100
            );

            if (!this.Visible) this.Show();
            this.Refresh();
            _timer.Stop();
            _timer.Start();
        }

        public void UpdateDisplay()
        {
            if (!this.Visible) this.Show();
            this.Refresh();
            _timer.Stop();
            _timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // Clear background
            g.Clear(_bgColor);

            // Draw subtle shadow/border
            using (var borderPen = new Pen(Color.FromArgb(180, 180, 180), 1))
            {
                Rectangle rect = this.ClientRectangle;
                rect.Width -= 1; rect.Height -= 1;
                g.DrawRectangle(borderPen, rect);
            }

            if (_monitors.Count == 0 || _displayIndex >= _monitors.Count) return;
            
            var monitor = _monitors[_displayIndex];
            int brightness = monitor.LastBrightness;

            // Icon on the left (brightness icon)
            Rectangle iconRect = new Rectangle(20, 20, 40, 40);
            using (Font iconFont = new Font("Segoe MDL2 Assets", 24))
            {
                TextRenderer.DrawText(g, "\uE706", iconFont, iconRect, _textColor, 
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // Monitor name
            using (Font nameFont = new Font("Segoe UI", 11))
            using (Brush nameBrush = new SolidBrush(_textColor))
            {
                Rectangle nameRect = new Rectangle(70, 15, 250, 25);
                g.DrawString(monitor.Name, nameFont, nameBrush, nameRect);
            }

            // Progress bar
            int barLeft = 70;
            int barTop = 45;
            int barWidth = 200;
            int barHeight = 5;
            
            // Track background
            using (Brush trackBrush = new SolidBrush(_trackColor))
            {
                FillRoundedRectangle(g, trackBrush, barLeft, barTop, barWidth, barHeight, barHeight / 2);
            }
            
            // Fill
            int fillW = (int)(barWidth * (brightness / 100.0f));
            if (fillW > 0)
            {
                using (Brush fillBrush = new SolidBrush(_fillColor))
                {
                    FillRoundedRectangle(g, fillBrush, barLeft, barTop, fillW, barHeight, barHeight / 2);
                }
            }

            // Percentage text
            using (Font valFont = new Font("Segoe UI", 11, FontStyle.Regular))
            using (Brush valBrush = new SolidBrush(_textColor))
            {
                Rectangle valRect = new Rectangle(280, 38, 40, 25);
                g.DrawString(brightness + "%", valFont, valBrush, valRect);
            }
        }
        
        private void FillRoundedRectangle(Graphics g, Brush brush, float x, float y, float w, float h, float r)
        {
            if (r > h / 2) r = h / 2;
            if (r > w / 2) r = w / 2;
            using (GraphicsPath path = new GraphicsPath())
            {
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

    // ================== HelpForm ==================
    public class HelpForm : Form {
        public HelpForm() {
            this.Text = "使用说明";
            this.Size = new Size(520, 440); 
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog; 
            this.MaximizeBox = false; 
            this.MinimizeBox = false;
            this.BackColor = ThemeManager.Background;
            this.ForeColor = ThemeManager.Text;
            
            this.HandleCreated += (s, e) => {
                Windows11Style.ApplyAcrylic(this, ThemeManager.IsDarkMode);
            };
            
            Label title = new Label { 
                Text = "HM's Simple Brightness Tool", 
                Top = 24, 
                Left = 24, 
                AutoSize = true, 
                Font = new Font("Segoe UI Variable Display", 18, FontStyle.Regular),
                ForeColor = ThemeManager.Text
            };
            
            TextBox info = new TextBox { 
                Multiline = true, 
                ReadOnly = true, 
                ScrollBars = ScrollBars.Vertical,
                Top = 70, 
                Left = 24, 
                Width = 456, 
                Height = 280,
                BackColor = ThemeManager.Surface, 
                ForeColor = ThemeManager.TextSecondary, 
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI Variable Text", 10),
                Padding = new Padding(12),
                Text = @"【快捷操作】
- 滚轮调节: 鼠标悬停托盘图标, 滚动调整亮度。
- 中键同步: 对着图标按中键, 强制同步所有屏幕。

【特色功能】
- 曲线编辑: 在控制中心自定义亮度映射, 解决副屏太暗/太亮的非线性问题。
- 电源模式: 支持 DDC/CI (硬关机) 与 Windows API (软黑屏)。

【常见问题】
- 调节卡顿: 请在设置中调高响应延迟 (推荐 200ms+)。
- 无法控制: 部分显示器需在 OSD 菜单开启 DDC/CI 支持。"
            };
            info.SelectionLength = 0;
            
            Win11Button btnOk = new Win11Button { 
                Text = "明白", 
                Top = 365, 
                Left = 380, 
                Width = 100, 
                Height = 36,
                DialogResult = DialogResult.OK
            };
            
            this.Controls.Add(title); 
            this.Controls.Add(info); 
            this.Controls.Add(btnOk);
        }
    }

    // ================== SettingsForm ==================
    public class SettingsForm : Form { 
        public SettingsForm(AppConfig config) { 
            this.Text = "设置"; 
            this.Size = new Size(400, 520); 
            this.StartPosition = FormStartPosition.CenterScreen; 
            this.FormBorderStyle = FormBorderStyle.FixedDialog; 
            this.MaximizeBox = false;
            this.BackColor = ThemeManager.Background;
            this.ForeColor = ThemeManager.Text;
            
            this.HandleCreated += (s, e) => {
                Windows11Style.ApplyAcrylic(this, ThemeManager.IsDarkMode);
            };
            
            FlowLayoutPanel panel = new FlowLayoutPanel { 
                FlowDirection = FlowDirection.TopDown, 
                WrapContents = false, 
                AutoSize = true, 
                AutoSizeMode = AutoSizeMode.GrowAndShrink, 
                Padding = new Padding(24), 
                Width = 400,
                BackColor = Color.Transparent
            };
            this.Controls.Add(panel);
            
            Label lblTitle = new Label { 
                Text = "设置", 
                AutoSize = true, 
                Font = new Font("Segoe UI Variable Display", 18, FontStyle.Regular),
                ForeColor = ThemeManager.Text,
                Margin = new Padding(0, 0, 0, 20)
            };
            panel.Controls.Add(lblTitle);
            
            // Show current theme (read-only, auto-detected)
            Label lblTheme = new Label { 
                Text = $"当前主题: {(ThemeManager.IsDarkMode ? "深色" : "浅色")} (自动)", 
                AutoSize = true, 
                Font = new Font("Segoe UI Variable Text", 10),
                ForeColor = ThemeManager.TextSecondary,
                Margin = new Padding(0, 0, 0, 20) 
            };
            panel.Controls.Add(lblTheme);
            
            CheckBox chkAuto = new CheckBox { 
                Text = "开机自动启动", 
                AutoSize = true, 
                Checked = IsAutoStart(), 
                Font = new Font("Segoe UI Variable Text", 10), 
                ForeColor = ThemeManager.TextSecondary,
                Margin = new Padding(0, 0, 0, 20) 
            }; 
            
            Label lblStep = new Label { 
                Text = "滚轮步长 (1-20):", 
                AutoSize = true, 
                Font = new Font("Segoe UI Variable Text", 10),
                ForeColor = ThemeManager.TextSecondary
            }; 
            NumericUpDown numStep = new NumericUpDown { 
                Minimum = 1, 
                Maximum = 20, 
                Width = 120, 
                Value = Math.Clamp(config.ScrollStep, 1, 20), 
                Margin = new Padding(0, 5, 0, 20),
                BackColor = ThemeManager.Surface,
                ForeColor = ThemeManager.Text,
                BorderStyle = BorderStyle.FixedSingle
            }; 
            
            Label lblDelay = new Label { 
                Text = "调节响应延迟 (防卡顿 ms):", 
                AutoSize = true, 
                Font = new Font("Segoe UI Variable Text", 10),
                ForeColor = ThemeManager.TextSecondary
            }; 
            NumericUpDown numDelay = new NumericUpDown { 
                Minimum = 0, 
                Maximum = 2000, 
                Width = 120, 
                Value = Math.Clamp(config.DebounceTime, 0, 2000), 
                Margin = new Padding(0, 5, 0, 20),
                BackColor = ThemeManager.Surface,
                ForeColor = ThemeManager.Text,
                BorderStyle = BorderStyle.FixedSingle
            }; 

            Label lblPower = new Label { 
                Text = "电源按钮模式:", 
                AutoSize = true, 
                Font = new Font("Segoe UI Variable Text", 10),
                ForeColor = ThemeManager.TextSecondary
            };
            ComboBox cmbPower = new ComboBox { 
                Width = 280, 
                DropDownStyle = ComboBoxStyle.DropDownList, 
                Margin = new Padding(0, 5, 0, 20),
                BackColor = ThemeManager.Surface,
                ForeColor = ThemeManager.Text,
                FlatStyle = FlatStyle.Flat
            };
            cmbPower.Items.Add("DDC/CI (硬件指令 - 推荐)");
            cmbPower.Items.Add("Windows API (软件信号 - 兼容)");
            cmbPower.SelectedIndex = config.UseSoftwarePower ? 1 : 0;

            Win11Button btnClearHidden = new Win11Button { 
                Text = $"重置隐藏显示器 ({config.HiddenMonitors.Count})", 
                Width = 320, 
                Height = 40, 
                Margin = new Padding(0, 10, 0, 10),
                BackColor = Color.FromArgb(60, 60, 60)
            }; 
            btnClearHidden.Click += (s, e) => { 
                config.HiddenMonitors.Clear(); 
                MessageBox.Show("已重置，请重启软件。", "提示"); 
            }; 
            
            Win11Button btnOk = new Win11Button { 
                Text = "保存设置", 
                Width = 140, 
                Height = 40, 
                DialogResult = DialogResult.OK, 
                Margin = new Padding(180, 20, 0, 0)
            }; 
            btnOk.Click += (s, e) => { 
                config.ScrollStep = (int)numStep.Value; 
                config.DebounceTime = (int)numDelay.Value; 
                config.UseSoftwarePower = (cmbPower.SelectedIndex == 1);
                SetAutoStart(chkAuto.Checked); 
                this.Close(); 
            }; 
            
            panel.Controls.AddRange(new Control[] { lblTitle, lblTheme, chkAuto, lblStep, numStep, lblDelay, numDelay, lblPower, cmbPower, btnClearHidden, btnOk });
        } 
        private bool IsAutoStart() { using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false)) return key?.GetValue("SimpleBrightness") != null; } 
        private void SetAutoStart(bool enable) { using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)) { if (enable) key?.SetValue("SimpleBrightness", Application.ExecutablePath); else key?.DeleteValue("SimpleBrightness", false); } } 
    }

    // ================== BrightnessForm (Control Center) ==================
    public class BrightnessForm : Form
    {
        private List<MonitorInfo> _monitors; 
        private AppConfig _config; 
        private MyCustomApplicationContext _context; 
        private Dictionary<string, Win11TrackBar> _sliders = new Dictionary<string, Win11TrackBar>(); 
        private Dictionary<string, Label> _valLabels = new Dictionary<string, Label>(); 
        private FlowLayoutPanel _mainPanel;
        
        private bool IsMonitorHidden(MonitorInfo m, int index) {
            if (_config.HiddenMonitors.Contains(m.UniqueId)) return true;
            
            if (m.UniqueId.StartsWith("DDC_") && m.UniqueId.Contains("_H")) {
                string[] parts = m.UniqueId.Split('_');
                if (parts.Length >= 4) {
                    string nameHash = parts[1];
                    string oldId = $"DDC_{nameHash}_IDX_{index}";
                    if (_config.HiddenMonitors.Contains(oldId)) {
                        _config.HiddenMonitors.Add(m.UniqueId);
                        return true;
                    }
                    
                    var hiddenList = _config.HiddenMonitors.ToList();
                    foreach (var hiddenId in hiddenList)
                    {
                        if (hiddenId.StartsWith($"DDC_{nameHash}_H") && hiddenId != m.UniqueId)
                        {
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
            _monitors = monitors; 
            _config = config; 
            _context = context; 
            
            this.Text = "控制中心";
            this.FormBorderStyle = FormBorderStyle.FixedDialog; 
            this.ShowInTaskbar = false; 
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = ThemeManager.Background; 
            this.StartPosition = FormStartPosition.Manual; 
            this.TopMost = true; 
            this.AutoSize = true; 
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink; 
            this.Padding = new Padding(0);
            
            this.HandleCreated += (s, e) => {
                Windows11Style.ApplyAcrylic(this, ThemeManager.IsDarkMode);
            };
            
            this.Deactivate += (s, e) => { 
                if (Application.OpenForms.OfType<CurveEditorForm>().Any() || 
                    Application.OpenForms.OfType<SettingsForm>().Any() || 
                    Application.OpenForms.OfType<InputBox>().Any() || 
                    Application.OpenForms.OfType<HelpForm>().Any()) return; 
                if (!this.Bounds.Contains(Cursor.Position)) this.Hide(); 
                else this.Activate(); 
            };
            
            _mainPanel = new FlowLayoutPanel { 
                FlowDirection = FlowDirection.TopDown, 
                WrapContents = false, 
                AutoSize = true, 
                AutoSizeMode = AutoSizeMode.GrowAndShrink, 
                Padding = new Padding(16, 12, 16, 12), 
                BackColor = Color.Transparent, 
                MaximumSize = new Size(520, 2000) 
            }; 
            this.Controls.Add(_mainPanel);
            
            // Header with title
            Panel header = new Panel { Size = new Size(460, 50), Margin = new Padding(0, 0, 0, 10) }; 
            Label title = new Label { 
                Text = "控制中心", 
                Location = new Point(0, 10), 
                ForeColor = ThemeManager.Text, 
                Font = new Font("Segoe UI Variable Display", 16, FontStyle.Regular), 
                AutoSize = true 
            }; 
            Button btnMenu = new Button { 
                Text = "☰", 
                Location = new Point(420, 5), 
                Size = new Size(40, 40), 
                FlatStyle = FlatStyle.Flat, 
                ForeColor = ThemeManager.Text, 
                Cursor = Cursors.Hand,
                BackColor = ThemeManager.Surface,
                Font = new Font("Segoe UI", 12)
            }; 
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
                FlowLayoutPanel card = new FlowLayoutPanel { 
                    FlowDirection = FlowDirection.TopDown, 
                    WrapContents = false, 
                    AutoSize = true, 
                    AutoSizeMode = AutoSizeMode.GrowAndShrink, 
                    Width = 460, 
                    Padding = new Padding(0, 8, 0, 12), 
                    Margin = new Padding(0, 0, 0, 12),
                    BackColor = Color.Transparent
                };
                
                Panel row1 = new Panel { Size = new Size(450, 32), Margin = new Padding(4, 0, 4, 8) }; 
                Label lblName = new Label { 
                    Text = m.Name, 
                    Location = new Point(0, 4), 
                    ForeColor = ThemeManager.TextSecondary, 
                    AutoSize = true, 
                    Font = new Font("Segoe UI Variable Text", 11), 
                    Cursor = Cursors.Hand, 
                    MaximumSize = new Size(360, 30) 
                }; 
                SetupNameMenu(lblName, m); 
                Label lblVal = new Label { 
                    Text = $"{m.LastBrightness}%", 
                    Location = new Point(390, 4), 
                    ForeColor = ThemeManager.Accent, 
                    AutoSize = true, 
                    Font = new Font("Segoe UI Variable Text", 12, FontStyle.Bold) 
                }; 
                _valLabels[m.UniqueId] = lblVal; 
                row1.Controls.Add(lblName); 
                row1.Controls.Add(lblVal); 
                card.Controls.Add(row1);
                
                // Use custom Win11 style TrackBar
                Win11TrackBar slider = new Win11TrackBar { 
                    Size = new Size(450, 32), 
                    Maximum = 100, 
                    Minimum = 0, 
                    Value = m.LastBrightness, 
                    Margin = new Padding(4, 4, 4, 8)
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
                    updateLogic(slider.Value); 
                    ((HandledMouseEventArgs)e).Handled = true; 
                }; 
                card.Controls.Add(slider);
                
                if (m.Type == MonitorType.DDC) { 
                    FlowLayoutPanel btnRow = new FlowLayoutPanel { 
                        AutoSize = true, 
                        AutoSizeMode = AutoSizeMode.GrowAndShrink, 
                        FlowDirection = FlowDirection.LeftToRight, 
                        Margin = new Padding(4, 8, 0, 0),
                        BackColor = Color.Transparent
                    }; 
                    
                    Win11Button btnCurve = new Win11Button { 
                        Text = "编辑曲线", 
                        Size = new Size(110, 36)
                    }; 
                    btnCurve.Click += (s, e) => { var editor = new CurveEditorForm(m, _config); editor.Show(this); }; 
                    btnRow.Controls.Add(btnCurve); 
                    
                    Win11Button btnPower = new Win11Button { 
                        Text = "⏻ 电源", 
                        Size = new Size(90, 36),
                        BackColor = Color.FromArgb(50, 50, 50)
                    }; 
                    btnPower.MouseDown += (s, e) => { 
                        Task.Run(() => BrightnessController.SetPowerState(m, e.Button == MouseButtons.Left, _config.UseSoftwarePower)); 
                    }; 
                    ToolTip tip = new ToolTip(); 
                    tip.SetToolTip(btnPower, "左键：开启 (On)\n右键：关闭 (Off)");
                    btnRow.Controls.Add(btnPower); 
                    card.Controls.Add(btnRow); 
                }
                
                if (m != visibleMonitors.Last()) { 
                    Panel div = new Panel { 
                        Size = new Size(450, 1), 
                        BackColor = ThemeManager.Border, 
                        Margin = new Padding(4, 16, 4, 0) 
                    }; 
                    card.Controls.Add(div); 
                } 
                _mainPanel.Controls.Add(card);
            }
        }
        
        protected override void OnLoad(EventArgs e) { 
            base.OnLoad(e); 
            var screen = Screen.FromPoint(Cursor.Position); 
            int x = screen.WorkingArea.Right - this.Width - 10; 
            int y = screen.WorkingArea.Bottom - this.Height - 10; 
            if (y < screen.WorkingArea.Top) y = screen.WorkingArea.Top + 50; 
            if (x < screen.WorkingArea.Left) x = screen.WorkingArea.Left + 10; 
            this.Location = new Point(x, y); 
        }
        
        private void SetupNameMenu(Label lbl, MonitorInfo m) { 
            ContextMenuStrip menu = new ContextMenuStrip(); 
            menu.Items.Add("重命名", null, (s, e) => { 
                InputBox input = new InputBox("重命名", "输入新名称:", m.Name); 
                if (input.ShowDialog() == DialogResult.OK) { 
                    _config.CustomNames[m.UniqueId] = input.ResultText; 
                    _config.Save(); 
                    _context.RefreshMonitors(); 
                    this.Close(); 
                } 
            }); 
            menu.Items.Add("隐藏", null, (s, e) => { 
                _config.HiddenMonitors.Add(m.UniqueId); 
                _config.Save(); 
                this.Close(); 
            }); 
            lbl.MouseClick += (s, e) => { if (e.Button == MouseButtons.Right) menu.Show(Cursor.Position); }; 
        }
        
        public void UpdateSlider(string id, int val) { 
            if (_sliders.ContainsKey(id)) { 
                _sliders[id].Value = val; 
                _valLabels[id].Text = val + "%"; 
                return;
            }
            
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
                                    _valLabels[key].Text = val + "%";
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

    // ================== CurveEditorForm ==================
    public class CurveEditorForm : Form
    {
        private MonitorInfo _monitor; 
        private AppConfig _config; 
        private Dictionary<int, int> _currentPoints; 
        private FlowLayoutPanel _panel;
        
        private Dictionary<int, int> GetCurveWithFallback() {
            if (_config.Curves.TryGetValue(_monitor.UniqueId, out var curve)) 
                return curve;
            
            if (_monitor.UniqueId.StartsWith("DDC_") && _monitor.UniqueId.Contains("_H")) {
                string[] parts = _monitor.UniqueId.Split('_');
                if (parts.Length >= 4) {
                    string nameHash = parts[1];
                    string idxStr = parts[parts.Length - 1];
                    string oldId = $"DDC_{nameHash}_IDX_{idxStr}";
                    if (_config.Curves.TryGetValue(oldId, out var oldCurve)) {
                        _config.Curves[_monitor.UniqueId] = oldCurve;
                        return oldCurve;
                    }
                    
                    var curveKeys = _config.Curves.Keys.ToList();
                    foreach (var curveId in curveKeys)
                    {
                        if (curveId.StartsWith($"DDC_{nameHash}_H") && curveId != _monitor.UniqueId)
                        {
                            var savedCurve = _config.Curves[curveId];
                            _config.Curves[_monitor.UniqueId] = savedCurve;
                            return savedCurve;
                        }
                    }
                }
            }
            return new Dictionary<int, int> { { 0, 0 }, { 100, 100 } };
        }
        
        public CurveEditorForm(MonitorInfo monitor, AppConfig config) {
            _monitor = monitor; 
            _config = config; 
            _currentPoints = new Dictionary<int, int>(GetCurveWithFallback());
            
            this.Size = new Size(850, 500); 
            this.BackColor = ThemeManager.Background; 
            this.StartPosition = FormStartPosition.CenterScreen; 
            this.Text = $"曲线编辑器 - {monitor.Name}";
            this.MaximizeBox = false;
            
            this.HandleCreated += (s, e) => {
                Windows11Style.ApplyAcrylic(this, ThemeManager.IsDarkMode);
            };
            
            Panel top = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.Transparent };
            Label title = new Label { 
                Text = $"编辑: {monitor.Name}", 
                Location = new Point(20, 18), 
                AutoSize = true, 
                ForeColor = ThemeManager.Text, 
                Font = new Font("Segoe UI Variable Display", 14, FontStyle.Regular) 
            };
            NumericUpDown num = new NumericUpDown { 
                Value = 50, 
                Width = 80, 
                Location = new Point(350, 13), 
                Font = new Font("Segoe UI Variable Text", 10),
                BackColor = ThemeManager.Surface,
                ForeColor = ThemeManager.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            Win11Button add = new Win11Button { 
                Text = "添加节点", 
                Width = 110, 
                Height = 34, 
                Location = new Point(440, 13),
                BackColor = Color.FromArgb(60, 60, 60)
            };
            Win11Button save = new Win11Button { 
                Text = "保存并生效", 
                Width = 120, 
                Height = 34, 
                Location = new Point(700, 13)
            };
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
            top.Controls.AddRange(new Control[] { title, num, add, save });
            this.Controls.Add(top);
            _panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(20, 10, 0, 0), BackColor = Color.Transparent };
            this.Controls.Add(_panel); 
            _panel.BringToFront();
            RefreshSliders();
        }
        
        private void RefreshSliders() { 
            _panel.Controls.Clear(); 
            if (!_currentPoints.ContainsKey(0)) _currentPoints[0]=0; 
            if(!_currentPoints.ContainsKey(100)) _currentPoints[100]=100; 
            foreach(var k in _currentPoints.Keys.OrderBy(x=>x)) _panel.Controls.Add(CreateItem(k, _currentPoints[k])); 
        }
        
        private Control CreateItem(int x, int y) { 
             Panel p = new Panel { Width = 70, Height = 320, Margin = new Padding(8), BackColor = ThemeManager.Surface };
             Label l = new Label { Text = y.ToString(), Top = 5, Width = 70, Height = 25, TextAlign = ContentAlignment.MiddleCenter, ForeColor = ThemeManager.Accent, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
             int panelH = 320; int labelH = 20; int btnH = 20; 
             Label k = new Label { Text = x + "%", Top = panelH - labelH - btnH - 5, Width = 70, TextAlign = ContentAlignment.MiddleCenter, ForeColor = ThemeManager.Text, Font = new Font("Segoe UI", 9) };
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

    // ================== InputBox ==================
    public class InputBox : Form { 
        public string ResultText { get; private set; } = ""; 
        public InputBox(string title, string prompt, string defaultText) { 
            this.Size = new Size(360, 200); 
            this.Text = title; 
            this.StartPosition = FormStartPosition.CenterScreen; 
            this.FormBorderStyle = FormBorderStyle.FixedDialog; 
            this.BackColor = ThemeManager.Background;
            this.ForeColor = ThemeManager.Text;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            
            this.HandleCreated += (s, e) => {
                Windows11Style.ApplyAcrylic(this, ThemeManager.IsDarkMode);
            };
            
            Label l = new Label { 
                Text = prompt, 
                Top = 24, 
                Left = 24, 
                AutoSize = true,
                Font = new Font("Segoe UI Variable Text", 11),
                ForeColor = ThemeManager.TextSecondary
            }; 
            TextBox t = new TextBox { 
                Text = defaultText, 
                Top = 55, 
                Left = 24, 
                Width = 300,
                BackColor = ThemeManager.Surface,
                ForeColor = ThemeManager.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI Variable Text", 10)
            }; 
            Win11Button b = new Win11Button { 
                Text = "确定", 
                Top = 110, 
                Left = 240, 
                Width = 84,
                Height = 36,
                DialogResult = DialogResult.OK 
            }; 
            b.Click += (s, e) => { ResultText = t.Text; this.Close(); }; 
            this.Controls.AddRange(new Control[] { l, t, b }); 
            this.AcceptButton = b; 
        } 
    }
    
    // Legacy OSD placeholders
    public class OsdForm : Form { public OsdForm(string n){} public void UpdateName(string n){} public void ShowOSD(int v, bool d, int r, int x, int y){} }
    public class UnifiedOsdForm : Form { 
        public UnifiedOsdForm(List<MonitorInfo> monitors) {}
        public void UpdateDisplay() {}
        public void UpdateMonitors(List<MonitorInfo> monitors) {}
    }
}


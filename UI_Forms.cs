using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using SimpleBrightness;
using Microsoft.Win32;

namespace SimpleBrightness
{
    // ================== Theme Mode Enum ==================
    public enum ThemeMode
    {
        Light = 0,
        Dark = 1,
        System = 2
    }

    // ================== Theme Manager (Auto-detect System Theme) ==================
    public static class ThemeManager
    {
        private static ThemeMode _themeMode = ThemeMode.System;
        private static bool _isDarkMode = true;
        
        public static ThemeMode ThemeMode
        {
            get => _themeMode;
            set
            {
                _themeMode = value;
                ApplyThemeMode();
            }
        }
        
        public static bool IsDarkMode 
        { 
            get => _isDarkMode;
            set => _isDarkMode = value;
        }
        
        // Apply theme mode
        private static void ApplyThemeMode()
        {
            switch (_themeMode)
            {
                case ThemeMode.Light:
                    _isDarkMode = false;
                    break;
                case ThemeMode.Dark:
                    _isDarkMode = true;
                    break;
                case ThemeMode.System:
                default:
                    AutoDetectTheme();
                    break;
            }
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
        
        // Get system accent color
        public static Color GetSystemAccentColor()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    if (key != null)
                    {
                        var value = key.GetValue("AccentColor");
                        if (value is int accentColor)
                        {
                            // Convert from ABGR to RGB
                            int r = accentColor & 0xFF;
                            int g = (accentColor >> 8) & 0xFF;
                            int b = (accentColor >> 16) & 0xFF;
                            return Color.FromArgb(r, g, b);
                        }
                    }
                }
            }
            catch { }
            // Default accent color
            return Color.FromArgb(0, 120, 212);
        }
        
        // Dark Mode Colors
        public static class Dark
        {
            public static readonly Color Background = Color.FromArgb(32, 32, 32);
            public static readonly Color Surface = Color.FromArgb(45, 45, 45);
            public static readonly Color Text = Color.FromArgb(255, 255, 255);
            public static readonly Color TextSecondary = Color.FromArgb(200, 200, 200);
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
            public static readonly Color Track = Color.FromArgb(200, 200, 200);
            public static readonly Color Border = Color.FromArgb(200, 200, 200);
            public static readonly Color Thumb = Color.FromArgb(100, 100, 100);
        }
        
        public static Color Background => IsDarkMode ? Dark.Background : Light.Background;
        public static Color Surface => IsDarkMode ? Dark.Surface : Light.Surface;
        public static Color Text => IsDarkMode ? Dark.Text : Light.Text;
        public static Color TextSecondary => IsDarkMode ? Dark.TextSecondary : Light.TextSecondary;
        public static Color Accent => GetSystemAccentColor();
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
    
    // ================== Dark Mode Scrollable Panel ==================
    public class DarkScrollPanel : Panel
    {
        private VScrollBar _customScrollBar;
        private Control? _contentControl;

        public DarkScrollPanel()
        {
            this.BackColor = ThemeManager.Background;
            this.AutoScroll = false;
            
            // Create custom scrollbar
            _customScrollBar = new VScrollBar
            {
                Dock = DockStyle.Right,
                Visible = false,
                Width = 12,
                BackColor = ThemeManager.Background
            };
            _customScrollBar.ValueChanged += (s, e) => {
                UpdateContentPosition();
            };
            this.Controls.Add(_customScrollBar);
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (e.Control != _customScrollBar && _contentControl == null && e.Control != null)
            {
                _contentControl = e.Control;
                _contentControl.LocationChanged += (s, ev) => UpdateScrollBar();
                _contentControl.SizeChanged += (s, ev) => UpdateScrollBar();
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateScrollBar();
        }

        private void UpdateContentPosition()
        {
            if (_contentControl != null)
            {
                _contentControl.Top = -_customScrollBar.Value;
            }
        }

        private void UpdateScrollBar()
        {
            if (_contentControl == null) return;

            int contentHeight = _contentControl.Height;
            int viewHeight = this.ClientSize.Height;
            bool needScrollBar = contentHeight > viewHeight;
            
            if (needScrollBar)
            {
                int maxScroll = contentHeight - viewHeight;
                _customScrollBar.Maximum = maxScroll + _customScrollBar.LargeChange - 1;
                _customScrollBar.LargeChange = Math.Max(20, viewHeight / 5);
                _customScrollBar.SmallChange = 30;
                _customScrollBar.Visible = true;
                
                // Style the scrollbar
                _customScrollBar.BackColor = ThemeManager.IsDarkMode ? Color.FromArgb(45, 45, 45) : Color.FromArgb(230, 230, 230);
                
                // Adjust content width to make room for scrollbar
                _contentControl.Width = this.ClientSize.Width - _customScrollBar.Width;
            }
            else
            {
                _customScrollBar.Visible = false;
                _contentControl.Top = 0;
                _contentControl.Width = this.ClientSize.Width;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_customScrollBar.Visible)
            {
                int delta = e.Delta / 3;
                int newValue = _customScrollBar.Value - delta;
                newValue = Math.Max(0, Math.Min(newValue, _customScrollBar.Maximum - _customScrollBar.LargeChange + 1));
                _customScrollBar.Value = newValue;
            }
        }
    }
    
    // ================== Windows 11 Style TrackBar (6px with Double Circle Thumb) ==================
    public class Win11TrackBar : Control
    {
        private int _value = 50;
        private int _minimum = 0;
        private int _maximum = 100;
        private bool _isDragging = false;
        private Rectangle _trackRect;
        private Rectangle _thumbRect;
        // Increased by 4px as requested: 20+4=24, 10+4=14
        private const int ThumbOuterSize = 24;  // Outer circle size (was 20, now 24)
        private const int ThumbInnerSize = 14;  // Inner circle size (was 10, now 14)
        private const int TrackHeight = 6;      // 6px track like reference image
        
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
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | 
                     ControlStyles.SupportsTransparentBackColor, true);
            Height = 36; // Increased to accommodate larger thumb
            Cursor = Cursors.Hand;
            // Make background transparent to blend with parent
            this.BackColor = Color.Transparent;
        }
        
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateThumbPosition();
        }
        
        private void UpdateThumbPosition()
        {
            int trackWidth = Width - ThumbOuterSize - 8;
            float ratio = _maximum > _minimum ? (float)(_value - _minimum) / (_maximum - _minimum) : 0;
            int thumbX = (int)(4 + ratio * trackWidth);
            _thumbRect = new Rectangle(thumbX, (Height - ThumbOuterSize) / 2, ThumbOuterSize, ThumbOuterSize);
            _trackRect = new Rectangle(4, (Height - TrackHeight) / 2, Width - 8, TrackHeight);
        }
        
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            
            UpdateThumbPosition();
            
            // Draw track background (darker in dark mode, lighter in light mode)
            Color trackColor = ThemeManager.IsDarkMode ? Color.FromArgb(60, 60, 60) : Color.FromArgb(200, 200, 200);
            using (var trackBrush = new SolidBrush(trackColor))
            using (var trackPath = GetRoundedRect(_trackRect, TrackHeight / 2))
            {
                g.FillPath(trackBrush, trackPath);
            }
            
            // Draw track fill (system accent color)
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
            
            // Draw double circle thumb (like reference image)
            // Outer circle (white in dark mode, gray in light mode)
            Color outerColor = ThemeManager.IsDarkMode ? Color.FromArgb(200, 200, 200) : Color.White;
            using (var outerBrush = new SolidBrush(outerColor))
            using (var outerPath = GetCirclePath(_thumbRect))
            {
                g.FillPath(outerBrush, outerPath);
            }
            
            // Outer circle border
            Color borderColor = ThemeManager.IsDarkMode ? Color.FromArgb(100, 100, 100) : Color.FromArgb(150, 150, 150);
            using (var borderPen = new Pen(borderColor, 1))
            using (var borderPath = GetCirclePath(_thumbRect))
            {
                g.DrawPath(borderPen, borderPath);
            }
            
            // Inner circle (system accent color)
            int innerX = _thumbRect.X + (_thumbRect.Width - ThumbInnerSize) / 2;
            int innerY = _thumbRect.Y + (_thumbRect.Height - ThumbInnerSize) / 2;
            var innerRect = new Rectangle(innerX, innerY, ThumbInnerSize, ThumbInnerSize);
            using (var innerBrush = new SolidBrush(ThemeManager.Accent))
            using (var innerPath = GetCirclePath(innerRect))
            {
                g.FillPath(innerBrush, innerPath);
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
            int trackWidth = Width - ThumbOuterSize - 8;
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
        // 缓存图标
        private static Dictionary<string, Icon> _iconCache = new Dictionary<string, Icon>();
        
        /// <summary>
        /// 从嵌入资源加载任务栏图标
        /// </summary>
        /// <param name="style">图标样式: Hybrid, Minimalist, Transparent</param>
        public static Icon LoadTrayIcon(string style) {
            string iconName = style switch {
                "Minimalist" => "Icon_Tray_Minimalist.ico",
                "Transparent" => "Icon_Tray_Transparent.ico",
                _ => "Icon_Tray_Hybrid.ico"  // 默认 Hybrid
            };
            
            // 检查缓存
            if (_iconCache.ContainsKey(iconName)) {
                return _iconCache[iconName];
            }
            
            // 从嵌入资源加载图标
            string resourceName = $"SimpleBrightness.app.ico.{iconName}";
            try {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (Stream? stream = assembly.GetManifestResourceStream(resourceName)) {
                    if (stream != null) {
                        Icon icon = new Icon(stream);
                        _iconCache[iconName] = icon;
                        return icon;
                    }
                }
            }
            catch { }
            
            // 如果资源加载失败，使用程序生成的图标作为后备
            return DrawNativeIconFallback();
        }
        
        /// <summary>
        /// 加载应用程序图标
        /// </summary>
        public static Icon LoadAppIcon() {
            // 应用程序图标已通过 ApplicationIcon 属性嵌入
            // 返回任务栏默认图标作为后备
            return LoadTrayIcon("Hybrid");
        }
        
        public static void ClearCache() {
            foreach (var icon in _iconCache.Values) {
                icon.Dispose();
            }
            _iconCache.Clear();
        }
        
        // 后备图标生成（当ICO文件不存在时使用）
        private static Icon DrawNativeIconFallback() {
            int iconSize = GetTrayIconSize();
            int fontSize = Math.Max(12, iconSize);
            
            using (Bitmap bmp = new Bitmap(iconSize, iconSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb)) {
                using (Graphics g = Graphics.FromImage(bmp)) {
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    
                    using (Font iconFont = new Font("Segoe MDL2 Assets", fontSize, GraphicsUnit.Pixel)) {
                        SizeF textSize = g.MeasureString("\uE706", iconFont);
                        float x = (float)Math.Round((iconSize - textSize.Width) / 2);
                        float y = (float)Math.Round((iconSize - textSize.Height) / 2) + 2;
                        
                        using (Brush brush = new SolidBrush(Color.White)) {
                            g.DrawString("\uE706", iconFont, brush, x, y);
                        }
                    }
                }
                
                IntPtr hIcon = bmp.GetHicon();
                Icon icon = Icon.FromHandle(hIcon);
                return (Icon)icon.Clone();
            }
        }

        private static int GetTrayIconSize() {
            float dpiScale = GetSystemDpiScale();
            int systemIconSize = GetSystemMetrics(SM_CXSMICON);
            if (systemIconSize > 0) {
                return systemIconSize;
            }
            
            if (dpiScale >= 2.0f) return 32;
            if (dpiScale >= 1.5f) return 24;
            if (dpiScale >= 1.25f) return 20;
            return 16;
        }

        private static float GetSystemDpiScale() {
            try {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) {
                    return g.DpiX / 96.0f;
                }
            }
            catch {
                return 1.0f;
            }
        }
        
        private const int SM_CXSMICON = 49;
        
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
    }

    // ================== Native Windows Style OSD (Multiple Monitors Support) ==================
    public class NativeOsdForm : Form
    {
        private System.Windows.Forms.Timer _timer;
        private List<MonitorInfo> _monitors;
        private int _displayIndex = 0;
        
        // Colors that adapt to theme
        private Color _bgColor => ThemeManager.IsDarkMode ? Color.FromArgb(40, 40, 40) : Color.FromArgb(240, 240, 240);
        private Color _textColor => ThemeManager.IsDarkMode ? Color.FromArgb(255, 255, 255) : Color.FromArgb(0, 0, 0);
        private Color _trackColor => ThemeManager.IsDarkMode ? Color.FromArgb(80, 80, 80) : Color.FromArgb(200, 200, 200);
        
        public NativeOsdForm(List<MonitorInfo> monitors)
        {
            _monitors = monitors;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.DoubleBuffered = true;
            this.StartPosition = FormStartPosition.Manual;
            
            // Dynamic size based on number of monitors with proper spacing
            int itemHeight = 50;  // Increased for better spacing
            int padding = 20;
            int totalHeight = padding * 2 + (_monitors.Count * itemHeight);
            this.Size = new Size(290, Math.Max(100, totalHeight));
            
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
            Color borderColor = ThemeManager.IsDarkMode ? Color.FromArgb(80, 80, 80) : Color.FromArgb(180, 180, 180);
            using (var borderPen = new Pen(borderColor, 1))
            {
                Rectangle rect = this.ClientRectangle;
                rect.Width -= 1; rect.Height -= 1;
                g.DrawRectangle(borderPen, rect);
            }

            if (_monitors.Count == 0) return;
            
            // Auto-calculate layout with equal margins
            int barHeight = 6;           // Progress bar height
            int margin = 20;             // Equal margin on left and right
            int leftIconPadding = 6;     // Extra padding on left of icon
            int elementGap = 12;         // Gap between icon/bar/value
            int iconWidth = 24;          // Width allocated for icon
            int monitorGap = 16;         // Gap between monitors
            int valWidth = 36;           // Width for value text
            
            // Calculate available width with equal left/right margins
            int availableContentWidth = this.Width - (margin * 2);
            int totalGaps = elementGap * 2;
            int barWidth = availableContentWidth - leftIconPadding - iconWidth - totalGaps - valWidth;
            
            // Calculate positions - equal left/right margins
            int iconX = margin + leftIconPadding - 4;  // 图标左移4px
            int barLeft = iconX + iconWidth + elementGap + 3;  // 滑块中心点右移3px
            int valX = barLeft + barWidth + elementGap + 10;  // 数值右移10px
            barWidth += 6;  // 滑块延长6px
            
            // Auto-calculate item height with generous padding
            int itemHeight = barHeight + 24;  // 12px padding top and bottom
            
            // Auto-calculate OSD height based on content with monitor gaps
            int totalContentHeight = (_monitors.Count * itemHeight) + ((_monitors.Count - 1) * monitorGap);
            int osdHeight = totalContentHeight + (margin * 2);
            
            // Resize OSD if needed (only if significantly different)
            if (Math.Abs(this.Height - osdHeight) > 10)
            {
                this.Height = osdHeight;
                // Recalculate region for rounded corners
                this.Region = Region.FromHrgn(NativeMethods.CreateRoundRectRgn(0, 0, Width, Height, 8, 8));
            }
            
            // Starting Y with equal margin
            int startY = margin;
            
            // Icon font for brightness symbol - use EC8A icon
            using (Font iconFont = new Font("Segoe MDL2 Assets", 13, FontStyle.Regular))
            using (Brush iconBrush = new SolidBrush(_textColor))
            {
                // Draw ALL monitors
                for (int i = 0; i < _monitors.Count; i++)
                {
                    var monitor = _monitors[i];
                    int brightness = monitor.LastBrightness;
                    // Add gap between monitors
                    int currentY = startY + (i * (itemHeight + monitorGap));
                    
                    // Vertical center of the item - all elements align to this center line
                    int centerY = currentY + itemHeight / 2;
                    int barTop = centerY - barHeight / 2;

                    // Draw brightness icon - use EC8A, vertically centered (moved down 1px)
                    Size iconTextSize = TextRenderer.MeasureText("\uEC8A", iconFont);
                    int iconDrawX = iconX + (iconWidth - iconTextSize.Width) / 2;  // Center in iconWidth space
                    int iconDrawY = centerY - iconTextSize.Height / 2 + 1;  // Vertically center with bar, +1px down
                    TextRenderer.DrawText(g, "\uEC8A", iconFont, new Point(iconDrawX, iconDrawY), _textColor);

                    // Progress bar - 6px height with rounded corners
                    using (Brush trackBrush = new SolidBrush(_trackColor))
                    {
                        FillRoundedRectangle(g, trackBrush, barLeft, barTop, barWidth, barHeight, barHeight / 2);
                    }
                    
                    // Fill (system accent color)
                    int fillW = (int)(barWidth * (brightness / 100.0f));
                    if (fillW > 0)
                    {
                        using (Brush fillBrush = new SolidBrush(ThemeManager.Accent))
                        {
                            FillRoundedRectangle(g, fillBrush, barLeft, barTop, fillW, barHeight, barHeight / 2);
                        }
                    }

                    // Value text - vertically centered with the bar using TextRenderer
                    using (Font valFont = new Font("Segoe UI", 10.5f, FontStyle.Regular))
                    {
                        string valText = brightness.ToString();
                        Size valTextSize = TextRenderer.MeasureText(valText, valFont);
                        int valDrawX = valX + (valWidth - valTextSize.Width) / 2;  // Center in valWidth
                        int valDrawY = centerY - valTextSize.Height / 2;  // Vertically center with bar
                        TextRenderer.DrawText(g, valText, valFont, new Point(valDrawX, valDrawY), _textColor);
                    }
                }
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

    // ================== SettingsForm (with Scroll Support and Theme Selection) ==================
    public class SettingsForm : Form {
        public bool HotkeysChanged { get; private set; } = false;
        public string NewHotkeyIncrease { get; private set; } = "";
        public string NewHotkeyDecrease { get; private set; } = "";
        public bool TrayIconChanged { get; private set; } = false;
        public string NewTrayIconStyle { get; private set; } = "";
        
        public SettingsForm(AppConfig config) { 
            this.Text = "设置"; 
            // Increased default size
            this.Size = new Size(450, 580); 
            this.StartPosition = FormStartPosition.CenterScreen; 
            this.FormBorderStyle = FormBorderStyle.FixedDialog; 
            this.MaximizeBox = false;
            this.BackColor = ThemeManager.Background;
            this.ForeColor = ThemeManager.Text;
            
            this.HandleCreated += (s, e) => {
                Windows11Style.ApplyAcrylic(this, ThemeManager.IsDarkMode);
            };
            
            // Create scrollable container with custom scrollbar
            DarkScrollPanel scrollContainer = new DarkScrollPanel {
                Dock = DockStyle.Fill,
                Padding = new Padding(0)
            };
            this.Controls.Add(scrollContainer);
            
            FlowLayoutPanel panel = new FlowLayoutPanel { 
                FlowDirection = FlowDirection.TopDown, 
                WrapContents = false, 
                AutoSize = true, 
                AutoSizeMode = AutoSizeMode.GrowAndShrink, 
                Padding = new Padding(24), 
                Width = 400,
                BackColor = ThemeManager.Background
            };
            scrollContainer.Controls.Add(panel);
            
            Label lblTitle = new Label { 
                Text = "设置", 
                AutoSize = true, 
                Font = new Font("Segoe UI Variable Display", 18, FontStyle.Regular),
                ForeColor = ThemeManager.Text,
                Margin = new Padding(0, 0, 0, 20)
            };
            panel.Controls.Add(lblTitle);
            
            // Theme Mode Selection
            Label lblTheme = new Label { 
                Text = "主题颜色:", 
                AutoSize = true, 
                Font = new Font("Segoe UI Variable Text", 10),
                ForeColor = ThemeManager.TextSecondary,
                Margin = new Padding(0, 0, 0, 5)
            };
            panel.Controls.Add(lblTheme);
            
            ComboBox cmbTheme = new ComboBox { 
                Width = 320, 
                DropDownStyle = ComboBoxStyle.DropDownList, 
                Margin = new Padding(0, 0, 0, 20),
                BackColor = ThemeManager.Surface,
                ForeColor = ThemeManager.Text,
                FlatStyle = FlatStyle.Flat
            };
            cmbTheme.Items.Add("浅色");
            cmbTheme.Items.Add("深色");
            cmbTheme.Items.Add("跟随系统");
            // Map config theme mode to combo box index
            cmbTheme.SelectedIndex = config.ThemeMode switch {
                ThemeMode.Light => 0,
                ThemeMode.Dark => 1,
                ThemeMode.System => 2,
                _ => 2
            };
            panel.Controls.Add(cmbTheme);
            
            CheckBox chkAuto = new CheckBox { 
                Text = "开机自动启动", 
                AutoSize = true, 
                Checked = IsAutoStart(), 
                Font = new Font("Segoe UI Variable Text", 10), 
                ForeColor = ThemeManager.TextSecondary,
                Margin = new Padding(0, 0, 0, 20) 
            }; 
            panel.Controls.Add(chkAuto);
            
            Label lblStep = new Label { 
                Text = "滚轮步长 (1-20):", 
                AutoSize = true, 
                Font = new Font("Segoe UI Variable Text", 10),
                ForeColor = ThemeManager.TextSecondary
            }; 
            panel.Controls.Add(lblStep);
            
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
            panel.Controls.Add(numStep);
            
            Label lblDelay = new Label { 
                Text = "调节响应延迟 (防卡顿 ms):", 
                AutoSize = true, 
                Font = new Font("Segoe UI Variable Text", 10),
                ForeColor = ThemeManager.TextSecondary
            }; 
            panel.Controls.Add(lblDelay);
            
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
            panel.Controls.Add(numDelay);

            Label lblPower = new Label { 
                Text = "关闭显示器模式:", 
                AutoSize = true, 
                Font = new Font("Segoe UI Variable Text", 10),
                ForeColor = ThemeManager.TextSecondary
            };
            panel.Controls.Add(lblPower);
            
            ComboBox cmbPower = new ComboBox { 
                Width = 320, 
                DropDownStyle = ComboBoxStyle.DropDownList, 
                Margin = new Padding(0, 5, 0, 5),
                BackColor = ThemeManager.Surface,
                ForeColor = ThemeManager.Text,
                FlatStyle = FlatStyle.Flat
            };
            cmbPower.Items.Add("待机 (0x04)");
            cmbPower.Items.Add("关机 (0x05)");
            cmbPower.SelectedIndex = config.PowerOffMode == 5 ? 1 : 0;
            panel.Controls.Add(cmbPower);

            // Tray Icon Style Selection
            Label lblTrayIcon = new Label { 
                Text = "任务栏图标样式:", 
                AutoSize = true, 
                Font = new Font("Segoe UI Variable Text", 10),
                ForeColor = ThemeManager.TextSecondary,
                Margin = new Padding(0, 0, 0, 5)
            };
            panel.Controls.Add(lblTrayIcon);
            
            ComboBox cmbTrayIcon = new ComboBox { 
                Width = 320, 
                DropDownStyle = ComboBoxStyle.DropDownList, 
                Margin = new Padding(0, 5, 0, 20),
                BackColor = ThemeManager.Surface,
                ForeColor = ThemeManager.Text,
                FlatStyle = FlatStyle.Flat
            };
            cmbTrayIcon.Items.Add("Hybrid (默认)");
            cmbTrayIcon.Items.Add("Minimalist (简约)");
            cmbTrayIcon.Items.Add("Transparent (透明)");
            cmbTrayIcon.SelectedIndex = config.TrayIconStyle switch {
                "Minimalist" => 1,
                "Transparent" => 2,
                _ => 0
            };
            panel.Controls.Add(cmbTrayIcon);

            // Hotkey Configuration
            Label lblHotkeyTitle = new Label { 
                Text = "快捷键设置:", 
                AutoSize = true, 
                Font = new Font("Segoe UI Variable Text", 10, FontStyle.Bold),
                ForeColor = ThemeManager.Text,
                Margin = new Padding(0, 10, 0, 10)
            };
            panel.Controls.Add(lblHotkeyTitle);
            
            // Hotkey capture controls
            string capturedIncrease = config.HotkeyIncrease;
            string capturedDecrease = config.HotkeyDecrease;
            
            Label lblHotkeyDecrease = new Label { 
                Text = "降低亮度快捷键 (点击输入框后按快捷键):", 
                AutoSize = true, 
                Font = new Font("Segoe UI Variable Text", 9),
                ForeColor = ThemeManager.TextSecondary,
                Margin = new Padding(0, 0, 0, 5)
            };
            panel.Controls.Add(lblHotkeyDecrease);
            
            HotkeyCaptureBox txtHotkeyDecrease = new HotkeyCaptureBox { 
                Width = 320, 
                Hotkey = config.HotkeyDecrease,
                Margin = new Padding(0, 0, 0, 15),
                BackColor = ThemeManager.Surface,
                ForeColor = ThemeManager.Text,
                Font = new Font("Segoe UI", 10)
            };
            txtHotkeyDecrease.HotkeyChanged += (hk) => capturedDecrease = hk;
            panel.Controls.Add(txtHotkeyDecrease);
            
            Label lblHotkeyIncrease = new Label { 
                Text = "增加亮度快捷键 (点击输入框后按快捷键):", 
                AutoSize = true, 
                Font = new Font("Segoe UI Variable Text", 9),
                ForeColor = ThemeManager.TextSecondary,
                Margin = new Padding(0, 0, 0, 5)
            };
            panel.Controls.Add(lblHotkeyIncrease);
            
            HotkeyCaptureBox txtHotkeyIncrease = new HotkeyCaptureBox { 
                Width = 320, 
                Hotkey = config.HotkeyIncrease,
                Margin = new Padding(0, 0, 0, 15),
                BackColor = ThemeManager.Surface,
                ForeColor = ThemeManager.Text,
                Font = new Font("Segoe UI", 10)
            };
            txtHotkeyIncrease.HotkeyChanged += (hk) => capturedIncrease = hk;
            panel.Controls.Add(txtHotkeyIncrease);

            Win11Button btnClearHidden = new Win11Button { 
                Text = $"重置隐藏显示器 ({config.HiddenMonitors.Count})", 
                Width = 350, 
                Height = 40, 
                Margin = new Padding(0, 10, 0, 60),
                BackColor = Color.FromArgb(60, 60, 60)
            }; 
            btnClearHidden.Click += (s, e) => { 
                config.HiddenMonitors.Clear(); 
                MessageBox.Show("已重置，请重启软件。", "提示"); 
            }; 
            panel.Controls.Add(btnClearHidden);
            
            // Floating save button at bottom right
            Win11Button btnOk = new Win11Button { 
                Text = "保存设置", 
                Width = 120, 
                Height = 44, 
                DialogResult = DialogResult.OK
            }; 
            btnOk.Click += (s, e) => { 
                config.ScrollStep = (int)numStep.Value; 
                config.DebounceTime = (int)numDelay.Value; 
                config.PowerOffMode = cmbPower.SelectedIndex == 1 ? 5 : 4;
                // Save hotkeys
                string newIncrease = capturedIncrease;
                string newDecrease = capturedDecrease;
                if (newIncrease != config.HotkeyIncrease || newDecrease != config.HotkeyDecrease)
                {
                    this.HotkeysChanged = true;
                    this.NewHotkeyIncrease = newIncrease;
                    this.NewHotkeyDecrease = newDecrease;
                }
                config.HotkeyIncrease = newIncrease;
                config.HotkeyDecrease = newDecrease;
                // Save theme mode
                config.ThemeMode = cmbTheme.SelectedIndex switch {
                    0 => ThemeMode.Light,
                    1 => ThemeMode.Dark,
                    2 => ThemeMode.System,
                    _ => ThemeMode.System
                };
                // Apply theme mode immediately
                ThemeManager.ThemeMode = config.ThemeMode;
                // Save tray icon style
                string newTrayIconStyle = cmbTrayIcon.SelectedIndex switch {
                    1 => "Minimalist",
                    2 => "Transparent",
                    _ => "Hybrid"
                };
                if (newTrayIconStyle != config.TrayIconStyle) {
                    this.TrayIconChanged = true;
                    this.NewTrayIconStyle = newTrayIconStyle;
                }
                config.TrayIconStyle = newTrayIconStyle;
                SetAutoStart(chkAuto.Checked); 
                this.Close(); 
            }; 
            
            // Add floating button to form directly (not to scroll panel)
            btnOk.Location = new Point(this.ClientSize.Width - 140, this.ClientSize.Height - 64);
            btnOk.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            this.Controls.Add(btnOk);
            btnOk.BringToFront();
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
            
            this.Text = "HM's Simple Brightness Tool";
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
                
                // Use custom Win11 style TrackBar (6px with double circle thumb, larger thumb)
                Win11TrackBar slider = new Win11TrackBar { 
                    Size = new Size(450, 36), // Increased height for larger thumb
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
                        Task.Run(() => BrightnessController.SetPowerState(m, e.Button == MouseButtons.Left, _config.PowerOffMode)); 
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

    // ================== CurveEditorForm (Visual Graph Version) ==================
    public class CurveEditorForm : Form {
        private MonitorInfo _monitor;
        private AppConfig _config;
        private Dictionary<int, int> _points;
        private CurveGraphControl _graph;
        private ToolTip _tooltip;
        private bool _showMinMaxUnlock = false;
        
        private Dictionary<int, int> GetCurveWithFallback() {
            if (_config.Curves.TryGetValue(_monitor.UniqueId, out var curve)) return curve;
            
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
                    foreach (var curveId in curveKeys) {
                        if (curveId.StartsWith($"DDC_{nameHash}_H") && curveId != _monitor.UniqueId) {
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
            _points = new Dictionary<int, int>(GetCurveWithFallback());
            _tooltip = new ToolTip();
            
            this.Size = new Size(1050, 850);
            this.BackColor = ThemeManager.Background;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "曲线编辑器";
            this.ForeColor = ThemeManager.Text;
            this.KeyPreview = true;
            
            this.HandleCreated += (s, e) => {
                Windows11Style.ApplyAcrylic(this, ThemeManager.IsDarkMode);
            };
            
            // Main TableLayoutPanel
            TableLayoutPanel mainTable = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                BackColor = ThemeManager.Background
            };
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 130)); // Top panel with controls (increased from 120)
            mainTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // Graph
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));  // Bottom panel with description
            this.Controls.Add(mainTable);
            
            // Top panel - title and controls
            Panel topPanel = new Panel {
                Dock = DockStyle.Fill,
                BackColor = ThemeManager.Surface,
                Padding = new Padding(20, 15, 20, 10)  // Increased top padding from 10 to 15
            };
            mainTable.Controls.Add(topPanel, 0, 0);
            
            // Top panel layout
            TableLayoutPanel topTable = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 3,
                BackColor = ThemeManager.Surface
            };
            topTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); // Title row (increased from 30)
            topTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 60)); // Controls row
            topTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));    // Checkboxes
            topTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // Spacer
            topTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));    // Selected point + Save
            topPanel.Controls.Add(topTable);
            
            // Row 0: Title (spans all columns)
            Label title = new Label { 
                Text = $"编辑: {monitor.Name}", 
                AutoSize = true, 
                ForeColor = ThemeManager.Text,
                Font = new Font("Segoe UI Variable Display", 14),
                Dock = DockStyle.Left
            };
            topTable.Controls.Add(title, 0, 0);
            topTable.SetColumnSpan(title, 3);
            
            // Row 1: Controls - Left side (Checkboxes)
            FlowLayoutPanel leftControls = new FlowLayoutPanel {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                BackColor = ThemeManager.Surface,
                Dock = DockStyle.Left,
                Margin = new Padding(0, 15, 0, 0)
            };
            topTable.Controls.Add(leftControls, 0, 1);
            
            CheckBox chkPreview = new CheckBox {
                Text = "实时预览",
                AutoSize = true,
                ForeColor = ThemeManager.TextSecondary,
                Checked = false,
                Margin = new Padding(0, 5, 20, 0)
            };
            leftControls.Controls.Add(chkPreview);
            
            CheckBox chkUnlock = new CheckBox {
                Text = "解锁 0%/100%",
                AutoSize = true,
                ForeColor = ThemeManager.TextSecondary,
                Checked = false,
                Margin = new Padding(0, 5, 20, 0)
            };
            leftControls.Controls.Add(chkUnlock);
            
            // Row 1: Controls - Right side (Selected point + Save)
            FlowLayoutPanel rightControls = new FlowLayoutPanel {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                BackColor = ThemeManager.Surface,
                Dock = DockStyle.Right,
                Margin = new Padding(0, 10, 0, 0)
            };
            topTable.Controls.Add(rightControls, 2, 1);
            
            Label lblSelected = new Label {
                Text = "选中节点:",
                AutoSize = true,
                ForeColor = ThemeManager.TextSecondary,
                Margin = new Padding(0, 8, 5, 0)
            };
            rightControls.Controls.Add(lblSelected);
            
            Label lblSelectedValue = new Label {
                Text = "无",
                AutoSize = true,
                ForeColor = ThemeManager.Text,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Margin = new Padding(0, 8, 20, 0)
            };
            rightControls.Controls.Add(lblSelectedValue);
            
            Win11Button saveBtn = new Win11Button { 
                Text = "保存并生效", 
                Size = new Size(120, 36),
                BackColor = ThemeManager.Accent
            };
            rightControls.Controls.Add(saveBtn);
            
            // Graph control
            _graph = new CurveGraphControl(_points, monitor, config) {
                Dock = DockStyle.Fill,
                BackColor = ThemeManager.Background
            };
            mainTable.Controls.Add(_graph, 0, 1);
            
            // Bottom panel - description and help
            Panel bottomPanel = new Panel {
                Dock = DockStyle.Fill,
                BackColor = ThemeManager.Surface,
                Padding = new Padding(20, 10, 20, 10)
            };
            mainTable.Controls.Add(bottomPanel, 0, 2);
            
            // Bottom layout
            TableLayoutPanel bottomTable = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                BackColor = ThemeManager.Surface
            };
            bottomTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            bottomTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            bottomPanel.Controls.Add(bottomTable);
            
            // Description
            Label lblDesc = new Label {
                Text = "💡 输入亮度 = 软件界面显示值  |  输出亮度 = 显示器实际亮度",
                AutoSize = true,
                ForeColor = ThemeManager.TextSecondary,
                Font = new Font("Segoe UI", 9),
                Dock = DockStyle.Left
            };
            bottomTable.Controls.Add(lblDesc, 0, 0);
            
            // Help text
            Label lblInfo = new Label {
                Text = "🖱️ 点击选中/添加 | 拖拽移动 | Ctrl+↑↓调整输出 | Ctrl+←→调整输入 | Ctrl+Z撤销 | 右键删除",
                AutoSize = true,
                ForeColor = ThemeManager.Accent,
                Font = new Font("Segoe UI", 9),
                Dock = DockStyle.Left
            };
            bottomTable.Controls.Add(lblInfo, 0, 1);
            
            // Key event handler
            this.KeyPreview = true;
            this.KeyDown += (s, e) => {
                // Ctrl+Z for undo
                if (e.Control && e.KeyCode == Keys.Z) {
                    _graph.Undo();
                    // Update display after undo - check if selected point still exists
                    if (_graph.SelectedPoint.HasValue) {
                        int sx = _graph.SelectedPoint.Value;
                        if (_points.ContainsKey(sx)) {
                            lblSelectedValue.Text = $"输入{sx}% → 输出{_points[sx]}%";
                        } else {
                            lblSelectedValue.Text = "无";
                        }
                    } else {
                        lblSelectedValue.Text = "无";
                    }
                    e.Handled = true;
                    return;
                }
                
                // Arrow keys for adjusting selected point
                if (_graph.SelectedPoint.HasValue) {
                    int x = _graph.SelectedPoint.Value;
                    if (!_points.ContainsKey(x)) return;
                    
                    int y = _points[x];
                    bool isFixed = (x == 0 || x == 100);
                    
                    switch (e.KeyCode) {
                        case Keys.Up:
                            if (!isFixed || _showMinMaxUnlock) {
                                _graph.SaveStateForUndo();
                                y = Math.Min(100, y + 1);
                                _points[x] = y;
                                lblSelectedValue.Text = $"输入{x}% → 输出{y}%";
                                _graph.Invalidate();
                                if (_graph.EnablePreview) {
                                    _graph.ApplyPreview(x, y);
                                }
                            }
                            e.Handled = true;
                            break;
                        case Keys.Down:
                            if (!isFixed || _showMinMaxUnlock) {
                                _graph.SaveStateForUndo();
                                y = Math.Max(0, y - 1);
                                _points[x] = y;
                                lblSelectedValue.Text = $"输入{x}% → 输出{y}%";
                                _graph.Invalidate();
                                if (_graph.EnablePreview) {
                                    _graph.ApplyPreview(x, y);
                                }
                            }
                            e.Handled = true;
                            break;
                        case Keys.Left:
                            if (!isFixed) {
                                int newX = Math.Max(1, x - 1);
                                if (!_points.ContainsKey(newX)) {
                                    _graph.SaveStateForUndo();
                                    _points.Remove(x);
                                    _points[newX] = y;
                                    _graph.SetSelectedPoint(newX);
                                    lblSelectedValue.Text = $"输入{newX}% → 输出{y}%";
                                    _graph.Invalidate();
                                }
                            }
                            e.Handled = true;
                            break;
                        case Keys.Right:
                            if (!isFixed) {
                                int newX = Math.Min(99, x + 1);
                                if (!_points.ContainsKey(newX)) {
                                    _graph.SaveStateForUndo();
                                    _points.Remove(x);
                                    _points[newX] = y;
                                    _graph.SetSelectedPoint(newX);
                                    lblSelectedValue.Text = $"输入{newX}% → 输出{y}%";
                                    _graph.Invalidate();
                                }
                            }
                            e.Handled = true;
                            break;
                    }
                }
            };
            
            saveBtn.Click += (s, e) => {
                _config.Curves[_monitor.UniqueId] = new Dictionary<int, int>(_points);
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
            
            chkUnlock.CheckedChanged += (s, e) => {
                _showMinMaxUnlock = chkUnlock.Checked;
                _graph.ShowMinMaxEdit = _showMinMaxUnlock;
                _graph.Invalidate();
            };
            
            chkPreview.CheckedChanged += (s, e) => {
                _graph.EnablePreview = chkPreview.Checked;
            };
            
            _graph.PointSelected += (x, y) => {
                lblSelectedValue.Text = $"输入{x}% → 输出{y}%";
            };
            
            _graph.PointDeleted += (x) => {
                if (x != 0 && x != 100) {
                    _points.Remove(x);
                    lblSelectedValue.Text = "无";
                    _graph.Invalidate();
                }
            };
            
            _graph.PointAdded += (x, y) => {
                _points[x] = y;
                lblSelectedValue.Text = $"输入{x}% → 输出{y}%";
                _graph.Invalidate();
            };
            
            _graph.ActionUndone += () => {
                if (_graph.SelectedPoint.HasValue) {
                    lblSelectedValue.Text = $"输入{_graph.SelectedPoint.Value}% → 输出{_points[_graph.SelectedPoint.Value]}%";
                } else {
                    lblSelectedValue.Text = "无";
                }
            };
            
            if (!_points.ContainsKey(0)) _points[0] = 0;
            if (!_points.ContainsKey(100)) _points[100] = 100;
        }
    }
    
    // ================== Curve Graph Control ==================
    public class CurveGraphControl : Control {
        private Dictionary<int, int> _points;
        private MonitorInfo _monitor;
        private AppConfig _config;
        private int? _hoveredPoint = null;
        private int? _selectedPoint = null;
        private bool _isDragging = false;
        private Point _dragStartPos;
        private int _lastDragX = 0; // Store last drag position for preview
        private int _lastDragY = 0;
        private const int PointRadius = 6;
        private const int HitRadius = 12;
        private const int DragThreshold = 5; // Pixels to start dragging
        private int _padding = 66; // Increased from 60 to fix Y-axis label overlap
        
        // Undo stack
        private Stack<Dictionary<int, int>> _undoStack = new Stack<Dictionary<int, int>>();
        private const int MaxUndoDepth = 20;
        
        public bool ShowMinMaxEdit { get; set; } = false;
        public bool EnablePreview { get; set; } = false;
        public int? SelectedPoint => _selectedPoint;
        public bool IsCapturingKey => _isDragging;
        
        public void SetSelectedPoint(int x)
        {
            if (_points.ContainsKey(x))
            {
                _selectedPoint = x;
                this.Invalidate();
            }
        }
        
        public event Action<int, int>? PointSelected;
        public event Action<int>? PointDeleted;
        public event Action<int, int>? PointAdded;
        public event Action? ActionUndone;
        
        public CurveGraphControl(Dictionary<int, int> points, MonitorInfo monitor, AppConfig config) {
            _points = points;
            _monitor = monitor;
            _config = config;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | 
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.TabStop = true; // Enable keyboard focus
        }
        
        public void SaveStateForUndo() {
            // Save current state
            var stateCopy = new Dictionary<int, int>(_points);
            _undoStack.Push(stateCopy);
            
            // Limit stack size
            while (_undoStack.Count > MaxUndoDepth) {
                var temp = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = 1; i < temp.Length; i++) {
                    _undoStack.Push(temp[i]);
                }
            }
        }
        
        public void Undo() {
            if (_undoStack.Count > 0) {
                var previousState = _undoStack.Pop();
                _points.Clear();
                foreach (var kvp in previousState) {
                    _points[kvp.Key] = kvp.Value;
                }
                ActionUndone?.Invoke();
                this.Invalidate();
            }
        }
        
        public void ApplyPreview(int inputBrightness, int outputBrightness) {
            // Apply the brightness to show real-time preview
            if (_monitor.Type == MonitorType.WMI) {
                Task.Run(() => BrightnessController.SetBrightnessImmediate(_monitor, outputBrightness));
            } else {
                BrightnessController.SetBrightnessDebounced(_monitor, outputBrightness, 50);
            }
        }
        
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            
            // Background
            g.Clear(BackColor);
            
            int width = this.Width - _padding * 2;
            int height = this.Height - _padding * 2;
            int left = _padding;
            int top = _padding;
            int right = left + width;
            int bottom = top + height;
            
            // Draw grid
            using (Pen gridPen = new Pen(ThemeManager.IsDarkMode ? Color.FromArgb(60, 60, 60) : Color.FromArgb(220, 220, 220), 1))
            using (Pen axisPen = new Pen(ThemeManager.TextSecondary, 1))
            {
                // Grid lines every 10%
                for (int i = 0; i <= 10; i++) {
                    int x = left + (width * i / 10);
                    int y = bottom - (height * i / 10); // Y from bottom to top
                    
                    // Vertical grid
                    g.DrawLine(gridPen, x, top, x, bottom);
                    // Horizontal grid
                    g.DrawLine(gridPen, left, y, right, y);
                    
                    // Labels
                    string label = (i * 10).ToString();
                    using (Font font = new Font("Segoe UI", 8))
                    using (Brush brush = new SolidBrush(ThemeManager.TextSecondary)) {
                        // X axis labels (0-100 from left to right)
                        SizeF size = g.MeasureString(label, font);
                        g.DrawString(label, font, brush, x - size.Width / 2, bottom + 5);
                        
                        // Y axis labels (0-100 from bottom to top)
                        size = g.MeasureString(label, font);
                        g.DrawString(label, font, brush, left - size.Width - 5, y - size.Height / 2);
                    }
                }
                
                // Axes
                g.DrawLine(axisPen, left, top, left, bottom);
                g.DrawLine(axisPen, left, bottom, right, bottom);
            }
            
            // Draw axis titles
            using (Font titleFont = new Font("Segoe UI", 9, FontStyle.Bold))
            using (Brush titleBrush = new SolidBrush(ThemeManager.Text)) {
                // X axis title
                g.DrawString("输入亮度 (%)", titleFont, titleBrush, left + width / 2 - 40, bottom + 25);
                
                // Y axis title (rotated)
                g.TranslateTransform(15, top + height / 2);
                g.RotateTransform(-90);
                g.DrawString("输出亮度 (%)", titleFont, titleBrush, -40, 0);
                g.ResetTransform();
            }
            
            // Sort points for line drawing
            var sortedPoints = _points.OrderBy(p => p.Key).ToList();
            
            // Draw curve line
            if (sortedPoints.Count >= 2) {
                using (Pen curvePen = new Pen(ThemeManager.Accent, 2)) {
                    Point[] linePoints = sortedPoints.Select(p => {
                        int x = left + (width * p.Key / 100);
                        int y = bottom - (height * p.Value / 100);
                        return new Point(x, y);
                    }).ToArray();
                    
                    g.DrawCurve(curvePen, linePoints, 0.3f);
                }
            }
            
            // Draw points
            foreach (var point in sortedPoints) {
                int px = left + (width * point.Key / 100);
                int py = bottom - (height * point.Value / 100);
                bool isFixed = (point.Key == 0 || point.Key == 100);
                bool isHovered = (_hoveredPoint == point.Key);
                bool isSelected = (_selectedPoint == point.Key);
                
                // Point circle
                Color pointColor = isFixed && !ShowMinMaxEdit ? Color.Gray : ThemeManager.Accent;
                if (isSelected) pointColor = Color.Orange;
                
                using (Brush brush = new SolidBrush(pointColor))
                using (Pen pen = new Pen(isHovered ? Color.White : pointColor, isHovered ? 2 : 1)) {
                    int r = isHovered ? PointRadius + 2 : PointRadius;
                    g.FillEllipse(brush, px - r, py - r, r * 2, r * 2);
                    g.DrawEllipse(pen, px - r, py - r, r * 2, r * 2);
                }
                
                // Value tooltip on hover
                if (isHovered || isSelected) {
                    using (Font font = new Font("Segoe UI", 9, FontStyle.Bold))
                    using (Brush brush = new SolidBrush(ThemeManager.Text)) {
                        string text = $"({point.Key}, {point.Value})";
                        SizeF size = g.MeasureString(text, font);
                        g.DrawString(text, font, brush, px - size.Width / 2, py - 25);
                    }
                }
            }
            
            // Draw diagonal reference line (y=x)
            using (Pen refPen = new Pen(Color.FromArgb(100, ThemeManager.TextSecondary), 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) {
                g.DrawLine(refPen, left, bottom, right, top);
            }
        }
        
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);
            
            int width = this.Width - _padding * 2;
            int height = this.Height - _padding * 2;
            int left = _padding;
            int top = _padding;
            int bottom = top + height;
            
            int? newHovered = null;
            
            foreach (var point in _points) {
                int px = left + (width * point.Key / 100);
                int py = bottom - (height * point.Value / 100);
                
                double dist = Math.Sqrt(Math.Pow(e.X - px, 2) + Math.Pow(e.Y - py, 2));
                if (dist <= HitRadius) {
                    newHovered = point.Key;
                    break;
                }
            }
            
            if (_hoveredPoint != newHovered) {
                _hoveredPoint = newHovered;
                this.Cursor = _hoveredPoint.HasValue ? Cursors.Hand : Cursors.Default;
                this.Invalidate();
            }
            
            // Handle dragging
            if (_isDragging && _selectedPoint.HasValue && e.Button == MouseButtons.Left) {
                // Check if moved enough to start dragging (prevent accidental drags)
                double moveDist = Math.Sqrt(Math.Pow(e.X - _dragStartPos.X, 2) + Math.Pow(e.Y - _dragStartPos.Y, 2));
                if (moveDist < DragThreshold) return;
                
                bool isFixed = (_selectedPoint == 0 || _selectedPoint == 100);
                if (!isFixed || ShowMinMaxEdit) {
                    // Calculate new position
                    int newX = Math.Max(0, Math.Min(100, (e.X - left) * 100 / width));
                    int newY = Math.Max(0, Math.Min(100, (bottom - e.Y) * 100 / height));
                    
                    // Store last position for preview on mouse up
                    _lastDragX = newX;
                    _lastDragY = newY;
                    
                    // Remove old point and add new one
                    int oldX = _selectedPoint.Value;
                    _points.Remove(oldX);
                    _points[newX] = newY;
                    _selectedPoint = newX;
                    
                    PointSelected?.Invoke(newX, newY);
                    this.Invalidate();
                }
            }
        }
        
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            this.Focus(); // Take focus for keyboard events
            
            if (_hoveredPoint.HasValue) {
                // Select the point and enable dragging immediately
                _selectedPoint = _hoveredPoint;
                _isDragging = true;
                _dragStartPos = e.Location;
                // Save state for undo when starting to drag
                SaveStateForUndo();
                PointSelected?.Invoke(_selectedPoint.Value, _points[_selectedPoint.Value]);
                this.Invalidate();
            } else {
                _selectedPoint = null;
                _isDragging = false;
                this.Invalidate();
            }
        }
        
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);
            
            // If we were dragging, save state for undo and apply preview
            if (_isDragging) {
                SaveStateForUndo();
                
                // Apply preview on mouse up if enabled
                if (EnablePreview) {
                    ApplyPreview(_lastDragX, _lastDragY);
                }
            }
            
            _isDragging = false;
        }
        
        protected override void OnMouseClick(MouseEventArgs e) {
            base.OnMouseClick(e);
            
            if (e.Button == MouseButtons.Right && _hoveredPoint.HasValue) {
                bool isFixed = (_hoveredPoint == 0 || _hoveredPoint == 100);
                if (!isFixed) {
                    SaveStateForUndo();
                    PointDeleted?.Invoke(_hoveredPoint.Value);
                }
            }
            else if (e.Button == MouseButtons.Left && !_hoveredPoint.HasValue && !_isDragging) {
                // Left click on empty space - add new point
                int width = this.Width - _padding * 2;
                int height = this.Height - _padding * 2;
                int left = _padding;
                int top = _padding;
                int bottom = top + height;
                
                // Calculate grid position
                int gridX = Math.Max(0, Math.Min(100, (e.X - left) * 100 / width));
                int gridY = Math.Max(0, Math.Min(100, (bottom - e.Y) * 100 / height));
                
                // Snap to nearest 5 for easier use
                gridX = (gridX / 5) * 5;
                gridY = (gridY / 5) * 5;
                
                // Don't add if too close to existing point or at boundaries (unless unlocked)
                if (gridX > 0 && gridX < 100) {
                    bool tooClose = false;
                    foreach (var pt in _points) {
                        if (Math.Abs(pt.Key - gridX) < 5) {
                            tooClose = true;
                            break;
                        }
                    }
                    
                    if (!tooClose) {
                        SaveStateForUndo();
                        PointAdded?.Invoke(gridX, gridY);
                    }
                }
            }
        }
    }

    // ================== InputBox ==================
    public class InputBox : Form {
        public string ResultText { get; private set; } = "";
        
        public InputBox(string title, string prompt, string defaultText) {
            this.Size = new Size(380, 200);
            this.Text = title;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.BackColor = ThemeManager.Background;
            this.ForeColor = ThemeManager.Text;
            
            this.HandleCreated += (s, e) => {
                Windows11Style.ApplyAcrylic(this, ThemeManager.IsDarkMode);
            };
            
            Label l = new Label { 
                Text = prompt, 
                Top = 20, 
                Left = 24, 
                AutoSize = true,
                ForeColor = ThemeManager.Text
            };
            
            TextBox t = new TextBox { 
                Text = defaultText, 
                Top = 50, 
                Left = 24, 
                Width = 320,
                BackColor = ThemeManager.Surface,
                ForeColor = ThemeManager.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            
            Win11Button b = new Win11Button { 
                Text = "确定", 
                Top = 100, 
                Left = 250, 
                Size = new Size(100, 36),
                DialogResult = DialogResult.OK
            };
            b.Click += (s, e) => { ResultText = t.Text; this.Close(); };
            
            this.Controls.AddRange(new Control[] { l, t, b });
            this.AcceptButton = b;
        }
    }
    
    // OsdForm (Placeholder)
    public class OsdForm : Form { public OsdForm(string n) { } public void UpdateName(string n) { } public void ShowOSD(int v, bool d, int r, int x, int y) { } }

    // ================== Hotkey Capture Box ==================
    public class HotkeyCaptureBox : TextBox
    {
        private bool _isCapturing = false;
        private string _hotkey = "";
        
        public string Hotkey
        {
            get => _hotkey;
            set
            {
                _hotkey = value;
                this.Text = string.IsNullOrEmpty(value) ? "点击此处按快捷键..." : value;
            }
        }
        
        public event Action<string>? HotkeyChanged;
        
        public HotkeyCaptureBox()
        {
            this.ReadOnly = true;
            this.Text = "点击此处按快捷键...";
            this.BackColor = ThemeManager.Surface;
            this.ForeColor = ThemeManager.Text;
            this.BorderStyle = BorderStyle.FixedSingle;
            this.Cursor = Cursors.Hand;
            
            this.Enter += (s, e) => StartCapture();
            this.Leave += (s, e) => StopCapture();
            this.KeyDown += HotkeyCaptureBox_KeyDown;
            this.KeyUp += HotkeyCaptureBox_KeyUp;
        }
        
        private void StartCapture()
        {
            _isCapturing = true;
            this.Text = "请按下快捷键...";
            this.BackColor = Color.FromArgb(60, 60, 80);
        }
        
        private void StopCapture()
        {
            _isCapturing = false;
            this.Text = string.IsNullOrEmpty(_hotkey) ? "点击此处按快捷键..." : _hotkey;
            this.BackColor = ThemeManager.Surface;
        }
        
        private void HotkeyCaptureBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (!_isCapturing) return;
            
            e.Handled = true;
            e.SuppressKeyPress = true;
            
            // Build hotkey string
            List<string> parts = new List<string>();
            
            if (e.Control) parts.Add("Ctrl");
            if (e.Alt) parts.Add("Alt");
            if (e.Shift) parts.Add("Shift");
            
            // Get the key (ignore modifiers)
            Keys keyCode = e.KeyCode;
            if (keyCode != Keys.ControlKey && keyCode != Keys.ShiftKey && keyCode != Keys.Menu &&
                keyCode != Keys.LControlKey && keyCode != Keys.RControlKey &&
                keyCode != Keys.LShiftKey && keyCode != Keys.RShiftKey &&
                keyCode != Keys.LMenu && keyCode != Keys.RMenu)
            {
                string keyName = keyCode.ToString();
                
                // Convert function keys
                if (keyCode >= Keys.F1 && keyCode <= Keys.F24)
                {
                    parts.Add(keyName);
                }
                // Convert number keys
                else if (keyCode >= Keys.D0 && keyCode <= Keys.D9)
                {
                    parts.Add(keyName.Replace("D", ""));
                }
                // Convert letter keys
                else if (keyCode >= Keys.A && keyCode <= Keys.Z)
                {
                    parts.Add(keyName);
                }
                // Other allowed keys
                else if (keyCode == Keys.Space)
                {
                    parts.Add("Space");
                }
                else if (keyCode == Keys.Up || keyCode == Keys.Down || keyCode == Keys.Left || keyCode == Keys.Right)
                {
                    parts.Add(keyName);
                }
                else if (keyCode == Keys.PageUp || keyCode == Keys.PageDown)
                {
                    parts.Add(keyName);
                }
                else if (keyCode == Keys.Home || keyCode == Keys.End)
                {
                    parts.Add(keyName);
                }
                else if (keyCode == Keys.Insert || keyCode == Keys.Delete)
                {
                    parts.Add(keyName);
                }
                else if (keyCode == Keys.Oemtilde)
                {
                    parts.Add("`");
                }
                else if (keyCode == Keys.OemMinus)
                {
                    parts.Add("-");
                }
                else if (keyCode == Keys.Oemplus)
                {
                    parts.Add("=");
                }
                else if (keyCode == Keys.OemOpenBrackets)
                {
                    parts.Add("[");
                }
                else if (keyCode == Keys.OemCloseBrackets)
                {
                    parts.Add("]");
                }
                else if (keyCode == Keys.OemPipe)
                {
                    parts.Add("\\");
                }
                else if (keyCode == Keys.OemSemicolon)
                {
                    parts.Add(";");
                }
                else if (keyCode == Keys.OemQuotes)
                {
                    parts.Add("'");
                }
                else if (keyCode == Keys.Oemcomma)
                {
                    parts.Add(",");
                }
                else if (keyCode == Keys.OemPeriod)
                {
                    parts.Add(".");
                }
                else if (keyCode == Keys.OemQuestion)
                {
                    parts.Add("/");
                }
                
                if (parts.Count > 0)
                {
                    _hotkey = string.Join("+", parts);
                    this.Text = _hotkey;
                    HotkeyChanged?.Invoke(_hotkey);
                    
                    // Stop capturing after valid key
                    this.Parent?.Focus();
                }
            }
        }
        
        private void HotkeyCaptureBox_KeyUp(object? sender, KeyEventArgs e)
        {
            if (!_isCapturing) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }
}


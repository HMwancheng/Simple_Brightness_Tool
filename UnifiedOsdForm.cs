using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SimpleBrightness
{
    public class UnifiedOsdForm : Form
    {
        private System.Windows.Forms.Timer _timer;
        private List<MonitorInfo> _monitors;
        private string _activeId = "";

        public UnifiedOsdForm(List<MonitorInfo> monitors)
        {
            _monitors = monitors;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.BackColor = Color.FromArgb(32, 32, 32); 
            this.DoubleBuffered = true;
            this.StartPosition = FormStartPosition.Manual;
            this.Padding = new Padding(15);
            
            int rowHeight = 50; 
            int totalHeight = 30 + (monitors.Count * rowHeight);
            this.Size = new Size(360, totalHeight);
            
            this.Region = Region.FromHrgn(NativeMethods.CreateRoundRectRgn(0, 0, Width, Height, 16, 16));
            
            _timer = new System.Windows.Forms.Timer { Interval = 1500 };
            _timer.Tick += (s, e) => this.Hide();
        }

        protected override bool ShowWithoutActivation => true;

        public void UpdateDisplay(string activeId = "")
        {
            _activeId = activeId; // This is now only used for potential future use, not for highlighting
            var screen = Screen.FromPoint(Cursor.Position);
            this.Location = new Point(
                screen.Bounds.X + (screen.Bounds.Width - Width) / 2, 
                screen.Bounds.Bottom - this.Height - 120
            );
            
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

            using (var borderPen = new Pen(Color.FromArgb(60, 60, 60), 1)) {
                e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }

            int y = 15;
            using(var stringFormat = new StringFormat { LineAlignment = StringAlignment.Center })
            foreach (var m in _monitors)
            {
                // 修复：取消高亮逻辑
                Color barColor = Color.DeepSkyBlue;
                Color fontColor = Color.White;

                string name = m.Name.Length > 15 ? m.Name.Substring(0, 15) + "..." : m.Name;
                e.Graphics.DrawString(name, new Font("Segoe UI", 10), new SolidBrush(fontColor), new Rectangle(20, y, 120, 30), stringFormat);
                
                int barX = 150;
                int barY = y + 13;
                int barW = 140;
                int barH = 4;
                FillRoundedRectangle(e.Graphics, new SolidBrush(Color.FromArgb(60, 60, 60)), barX, barY, barW, barH, 2);

                int w = (int)(barW * (m.LastBrightness / 100.0));
                if (w < 4) w = 4;
                FillRoundedRectangle(e.Graphics, new SolidBrush(barColor), barX, barY, w, barH, 2);

                e.Graphics.DrawString($"{m.LastBrightness}%", new Font("Segoe UI", 10, FontStyle.Bold), new SolidBrush(fontColor), new Rectangle(300, y, 50, 30), stringFormat);

                y += 50;
            }
        }
        
        private void FillRoundedRectangle(Graphics g, Brush brush, int x, int y, int w, int h, int r) {
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
}

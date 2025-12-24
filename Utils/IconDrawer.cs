using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SimpleBrightness.Utils
{
    public static class IconDrawer 
    { 
        public static Icon DrawSunIcon() 
        { 
            using (Bitmap bmp = new Bitmap(32, 32)) using (Graphics g = Graphics.FromImage(bmp)) { 
                g.SmoothingMode = SmoothingMode.AntiAlias; 
                // 绘制实心圆
                g.FillEllipse(Brushes.Gold, 10, 10, 12, 12); 
                
                // 绘制光芒
                Pen rayPen = new Pen(Color.Gold, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round }; 
                for (int i = 0; i < 360; i += 45) { 
                    double rad = i * Math.PI / 180; 
                    float x1 = 16 + (float)(8 * Math.Cos(rad)); float y1 = 16 + (float)(8 * Math.Sin(rad)); 
                    float x2 = 16 + (float)(14 * Math.Cos(rad)); float y2 = 16 + (float)(14 * Math.Sin(rad)); 
                    g.DrawLine(rayPen, x1, y1, x2, y2); 
                } 
                return Icon.FromHandle(bmp.GetHicon()); 
            } 
        } 
    }
}

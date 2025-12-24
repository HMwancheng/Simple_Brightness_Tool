using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace SimpleBrightness.Utils
{
    public static class IconDrawer { 
        public static Icon DrawNativeIcon() { 
            using (Bitmap bmp = new Bitmap(32, 32)) using (Graphics g = Graphics.FromImage(bmp)) { 
                g.SmoothingMode = SmoothingMode.AntiAlias; 
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                Font iconFont = new Font("Segoe MDL2 Assets", 18, FontStyle.Regular);
                // X:-7, Y:-1
                TextRenderer.DrawText(g, "\uE706", iconFont, new Point(-7, -1), Color.White);
                return Icon.FromHandle(bmp.GetHicon()); 
            } 
        } 
    }
}

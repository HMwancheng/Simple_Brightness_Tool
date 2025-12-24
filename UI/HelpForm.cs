using System.Drawing;
using System.Windows.Forms;

namespace SimpleBrightness.UI
{
    public class HelpForm : Form {
        public HelpForm() {
            this.Text = "关于 & 说明"; this.Size = new Size(520, 480); this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog; this.MaximizeBox = false; this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(31, 31, 31); this.ForeColor = Color.White;
            Label title = new Label { Text = "HM's Simple Brightness Tool", Top = 20, Left = 20, AutoSize = true, Font = new Font("Segoe UI", 14, FontStyle.Bold) };
            TextBox info = new TextBox { 
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Top = 60, Left = 20, Width = 460, Height = 320,
                BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.LightGray, BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9),
                Text = 
@"HM's Simple Brightness Tool

一个极致轻量、便携、现代化的 Windows 屏幕亮度控制工具。

【核心功能】
1. 极速调节：鼠标悬停任务栏托盘图标，滚动滚轮即可调节。
2. 中键同步：对着托盘图标点击【中键】，强制将所有屏幕亮度同步为主屏数值。

【独家：非线性曲线】
在控制中心点击“编辑曲线”，可自定义亮度映射。
解决副屏“调到10%太暗，调到20%又太亮”的问题。

【设置说明】
- 调节响应延迟：针对老旧显示器，调高此值可防止卡顿丢包（防抖动）。
- 电源按钮模式：
  DDC/CI：硬件指令，彻底断电（部分显示器唤不醒）。
  Windows API：软件黑屏信号，兼容性最好（推荐笔记本外接使用）。"
            };
            info.SelectionLength = 0;
            Button btnOk = new Button { Text = "关闭", Top = 400, Left = 380, Width = 100, Height = 35, BackColor = Color.Teal, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.OK };
            this.Controls.Add(title); this.Controls.Add(info); this.Controls.Add(btnOk);
        }
    }
}

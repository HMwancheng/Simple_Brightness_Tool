using System.Drawing;
using System.Windows.Forms;

namespace SimpleBrightness.UI
{
    public class InputBox : Form { public string ResultText { get; private set; } = ""; public InputBox(string title, string prompt, string defaultText) { this.Size = new Size(300, 180); this.Text = title; this.StartPosition = FormStartPosition.CenterScreen; this.FormBorderStyle = FormBorderStyle.FixedDialog; Label l = new Label { Text = prompt, Top = 20, Left = 20, AutoSize = true }; TextBox t = new TextBox { Text = defaultText, Top = 50, Left = 20, Width = 240 }; Button b = new Button { Text = "确定", Top = 90, Left = 180, DialogResult = DialogResult.OK }; b.Click += (s, e) => { ResultText = t.Text; this.Close(); }; this.Controls.AddRange(new Control[] { l, t, b }); this.AcceptButton = b; } }
}

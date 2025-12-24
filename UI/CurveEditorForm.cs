using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SimpleBrightness.Core;
using SimpleBrightness.Model;

namespace SimpleBrightness.UI
{
    public class CurveEditorForm : Form
    {
        private MonitorInfo _monitor; private AppConfig _config; private Dictionary<int, int> _currentPoints; private FlowLayoutPanel _panel;
        
        public CurveEditorForm(MonitorInfo monitor, AppConfig config) {
            _monitor = monitor; _config = config; _currentPoints = new Dictionary<int, int>(config.GetCurveForMonitor(monitor.UniqueId));
            this.Size = new Size(850, 500); this.BackColor = Color.FromArgb(31, 31, 31); this.StartPosition = FormStartPosition.CenterScreen; this.Text = "Curve Editor";
            
            Panel top = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(25, 25, 25) };
            Label title = new Label { Text = $"编辑: {monitor.Name}", Location = new Point(15, 18), AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
            NumericUpDown num = new NumericUpDown { Value = 50, Width = 80, Location = new Point(350, 13), Font = new Font("Segoe UI", 10) };
            Button add = new Button { Text = "添加节点", Width = 110, Height = 34, Location = new Point(440, 13), BackColor = Color.Gray, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            Button save = new Button { Text = "保存并生效", Width = 120, Height = 34, Location = new Point(700, 13), BackColor = Color.Teal, ForeColor = Color.White, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat };
            
            add.Click += (s, e) => { int x = (int)num.Value; if (!_currentPoints.ContainsKey(x)) { _currentPoints[x] = x; RefreshSliders(); }};
            save.Click += (s, e) => { _config.Curves[_monitor.UniqueId] = new Dictionary<int, int>(_currentPoints); _config.Save(); this.Close(); };
            
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
                 Button d = new Button { Text = "×", Top = panelH - btnH - 5, Left = 20, Width = 30, Height = 20, ForeColor = Color.Red, FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent }; 
                 d.FlatAppearance.BorderSize = 0; d.BringToFront();
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
}

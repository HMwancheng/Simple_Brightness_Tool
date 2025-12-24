using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SimpleBrightness
{
    public class BrightnessForm : Form
    {
        private List<MonitorInfo> _monitors; private AppConfig _config; private MyCustomApplicationContext _context; private Dictionary<string, TrackBar> _sliders = new Dictionary<string, TrackBar>(); private Dictionary<string, Label> _valLabels = new Dictionary<string, Label>(); private FlowLayoutPanel _mainPanel;
        public BrightnessForm(List<MonitorInfo> monitors, AppConfig config, MyCustomApplicationContext context, ContextMenuStrip menu) {
            _monitors = monitors; _config = config; _context = context; this.FormBorderStyle = FormBorderStyle.None; this.ShowInTaskbar = false; this.BackColor = Color.FromArgb(31, 31, 31); this.StartPosition = FormStartPosition.Manual; this.TopMost = true; this.AutoSize = true; this.AutoSizeMode = AutoSizeMode.GrowAndShrink; this.Padding = new Padding(2);
            this.Deactivate += (s, e) => { if (Application.OpenForms.OfType<CurveEditorForm>().Any() || Application.OpenForms.OfType<SettingsForm>().Any() || Application.OpenForms.OfType<InputBox>().Any() || Application.OpenForms.OfType<HelpForm>().Any()) return; if (!this.Bounds.Contains(Cursor.Position)) this.Hide(); else this.Activate(); };
            _mainPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10), BackColor = Color.FromArgb(31, 31, 31), MaximumSize = new Size(500, 2000) }; this.Controls.Add(_mainPanel);
            Panel header = new Panel { Size = new Size(450, 50), Margin = new Padding(0, 0, 0, 5) }; Label title = new Label { Text = "Control Center", Location = new Point(5, 10), ForeColor = Color.White, Font = new Font("Segoe UI", 14, FontStyle.Bold), AutoSize = true }; Button btnMenu = new Button { Text = "☰", Location = new Point(410, 5), Size = new Size(35, 35), FlatStyle = FlatStyle.Flat, ForeColor = Color.White, Cursor = Cursors.Hand }; btnMenu.FlatAppearance.BorderSize = 0; btnMenu.Click += (s, e) => menu.Show(Cursor.Position); header.Controls.Add(title); header.Controls.Add(btnMenu); _mainPanel.Controls.Add(header);
            var visibleMonitors = _monitors.Where(m => !_config.HiddenMonitors.Contains(m.UniqueId)).ToList(); if (visibleMonitors.Count == 0) visibleMonitors = _monitors; 
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
}

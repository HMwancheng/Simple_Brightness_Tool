using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32; // <--- 关键修复：添加这行
using SimpleBrightness.Core;

namespace SimpleBrightness.UI
{
    public class SettingsForm : Form
    {
        public SettingsForm(AppConfig config)
        {
            this.Text = "设置";
            this.Size = new Size(350, 480);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            FlowLayoutPanel panel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(20),
                Width = 400
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
            btnClearHidden.Click += (s, e) => { config.HiddenMonitors.Clear(); MessageBox.Show("已重置，请重新扫描或重启软件。", "提示"); };

            Button btnOk = new Button { Text = "保存设置", Width = 120, Height = 40, DialogResult = DialogResult.OK, BackColor = Color.Teal, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(120, 10, 0, 0) };
            btnOk.Click += (s, e) =>
            {
                config.ScrollStep = (int)numStep.Value;
                config.DebounceTime = (int)numDelay.Value;
                config.UseSoftwarePower = (cmbPower.SelectedIndex == 1);
                SetAutoStart(chkAuto.Checked);
                this.Close();
            };

            panel.Controls.AddRange(new Control[] { chkAuto, lblStep, numStep, lblDelay, numDelay, lblPower, cmbPower, btnClearHidden, btnOk });
        }

        private bool IsAutoStart()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
                return key?.GetValue("SimpleBrightness") != null;
        }

        private void SetAutoStart(bool enable)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (enable) key?.SetValue("SimpleBrightness", Application.ExecutablePath);
                else key?.DeleteValue("SimpleBrightness", false);
            }
        }
    }
}

using System.Windows;
using BrightnessWpf.Models;

namespace BrightnessWpf;

/// <summary>
/// 设置窗口：主题 / 步长 / 去抖 / 热键 / 电源模式。直接修改共享的 AppConfig。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;

    public bool Saved { get; private set; }

    public SettingsWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;

        // 选项顺序与 ThemeMode 枚举一致：0=浅色, 1=深色, 2=跟随系统
        ThemeCombo.ItemsSource = new[] { "浅色", "深色", "跟随系统" };
        ThemeCombo.SelectedIndex = (int)config.ThemeMode;

        StepBox.Text = config.ScrollStep.ToString();
        DebounceBox.Text = config.DebounceTime.ToString();
        HotkeyIncBox.Text = config.HotkeyIncrease;
        HotkeyDecBox.Text = config.HotkeyDecrease;

        PowerModeCombo.ItemsSource = new[] { "待机", "关机" };
        PowerModeCombo.SelectedIndex = config.PowerOffMode == 5 ? 1 : 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(StepBox.Text, out int step) || step < 1 || step > 50)
        {
            System.Windows.MessageBox.Show("步长需为 1-50 的整数。", "输入无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(DebounceBox.Text, out int debounce) || debounce < 0 || debounce > 2000)
        {
            System.Windows.MessageBox.Show("去抖时间需为 0-2000 的整数。", "输入无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _config.ThemeMode = (ThemeMode)ThemeCombo.SelectedIndex;
        _config.ScrollStep = step;
        _config.DebounceTime = debounce;
        _config.HotkeyIncrease = HotkeyIncBox.Text.Trim();
        _config.HotkeyDecrease = HotkeyDecBox.Text.Trim();
        _config.PowerOffMode = PowerModeCombo.SelectedIndex == 1 ? 5 : 4;
        _config.Save();

        Saved = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}

using System.Windows;
using System.Windows.Input;
using BrightnessWpf.Models;

namespace BrightnessWpf;

/// <summary>
/// 设置窗口：主题 / 步长 / 去抖 / 热键（点击后按键录入）/ 电源模式。直接修改共享的 AppConfig。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private int _capturing; // 0=无 1=增加 2=降低
    private string _hotkeyIncrease = "Ctrl+F5";
    private string _hotkeyDecrease = "Ctrl+F6";

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

        _hotkeyIncrease = config.HotkeyIncrease;
        _hotkeyDecrease = config.HotkeyDecrease;
        HotkeyIncButton.Content = _hotkeyIncrease;
        HotkeyDecButton.Content = _hotkeyDecrease;

        PowerModeCombo.ItemsSource = new[] { "待机", "关机" };
        PowerModeCombo.SelectedIndex = config.PowerOffMode == 5 ? 1 : 0;
    }

    private void HotkeyIncButton_Click(object sender, RoutedEventArgs e)
    {
        _capturing = 1;
        HotkeyIncButton.Content = "请按组合键...";
    }

    private void HotkeyDecButton_Click(object sender, RoutedEventArgs e)
    {
        _capturing = 2;
        HotkeyDecButton.Content = "请按组合键...";
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_capturing != 0)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            // 忽略纯修饰键
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            {
                e.Handled = true;
                return;
            }

            var mods = new List<string>();
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods.Add("Ctrl");
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) mods.Add("Alt");
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) mods.Add("Shift");
            if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) mods.Add("Win");

            string combo = string.Join("+", mods.Concat(new[] { key.ToString() }));
            if (_capturing == 1)
            {
                _hotkeyIncrease = combo;
                HotkeyIncButton.Content = combo;
            }
            else
            {
                _hotkeyDecrease = combo;
                HotkeyDecButton.Content = combo;
            }
            _capturing = 0;
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
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
        _config.HotkeyIncrease = _hotkeyIncrease;
        _config.HotkeyDecrease = _hotkeyDecrease;
        _config.PowerOffMode = PowerModeCombo.SelectedIndex == 1 ? 5 : 4;
        _config.Save();

        Saved = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}

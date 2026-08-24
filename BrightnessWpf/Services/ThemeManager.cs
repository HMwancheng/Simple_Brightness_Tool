using System.Windows;
using BrightnessWpf.Models;
using Microsoft.Win32;

namespace BrightnessWpf.Services;

/// <summary>
/// 主题管理：深色/浅色/跟随系统。通过替换 Application.Resources 中的颜色字典实现运行时切换。
/// </summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; } = true;

    /// <summary>根据配置应用主题（浅色/深色/跟随系统）。</summary>
    public static void Apply(ThemeMode mode)
    {
        IsDark = mode switch
        {
            ThemeMode.Light => false,
            ThemeMode.Dark => true,
            _ => !IsSystemLightTheme(),
        };

        var dict = new ResourceDictionary
        {
            Source = new Uri($"Resources/Colors.{(IsDark ? "Dark" : "Light")}.xaml", UriKind.Relative)
        };

        var merged = System.Windows.Application.Current.Resources.MergedDictionaries;
        int colorIdx = -1;
        for (int i = 0; i < merged.Count; i++)
        {
            if (merged[i].Source != null &&
                merged[i].Source.OriginalString.IndexOf("Colors.", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                colorIdx = i;
                break;
            }
        }
        if (colorIdx >= 0)
            merged[colorIdx] = dict;
        else
            merged.Insert(0, dict);
    }

    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 1;
        }
        catch
        {
            return false;
        }
    }
}

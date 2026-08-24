using System.IO;
using System.Text.Json;

namespace BrightnessWpf.Models;

public enum ThemeMode
{
    Light = 0,
    Dark = 1,
    System = 2
}

/// <summary>
/// 应用配置。迁移第 2 步：复用原版字段，路径改用 BaseDirectory（WPF 单文件发布适用）。
/// </summary>
public class AppConfig
{
    public int ScrollStep { get; set; } = 5;
    public int DebounceTime { get; set; } = 200;
    public int PowerOffMode { get; set; } = 4; // 4=待机, 5=关机
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;
    public string HotkeyIncrease { get; set; } = "Ctrl+F5";
    public string HotkeyDecrease { get; set; } = "Ctrl+F6";
    public string TrayIconStyle { get; set; } = "Hybrid";
    public List<string> HiddenMonitors { get; set; } = new();
    public Dictionary<string, string> CustomNames { get; set; } = new();
    public Dictionary<string, Dictionary<int, int>> Curves { get; set; } = new();
    public Dictionary<string, int> SavedBrightness { get; set; } = new();

    private static string ConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "HMSimpleBrightness_Config.json");

    public static AppConfig Load()
    {
        if (File.Exists(ConfigPath))
        {
            try
            {
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig();
            }
            catch { }
        }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this));
        }
        catch { }
    }

    public Dictionary<int, int> GetCurveForMonitor(string id)
    {
        if (Curves.TryGetValue(id, out var curve))
            return curve;
        return new Dictionary<int, int> { { 0, 0 }, { 100, 100 } };
    }
}

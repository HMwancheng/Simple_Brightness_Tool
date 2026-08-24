using System.Collections.Concurrent;
using System.Management;
using BrightnessWpf.Models;
using BrightnessWpf.Native;

namespace BrightnessWpf.Services;

/// <summary>
/// 亮度控制。迁移第 2 步：复用原版 DDC/WMI 设置 + 曲线插值逻辑。
/// </summary>
public static class BrightnessService
{
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceTokens = new();

    public static void SetBrightness(MonitorInfo monitor, int softwareLevel, int debounceMs, Dictionary<int, int> curve)
    {
        int finalLevel = Interpolate(softwareLevel, curve);
        monitor.LastBrightness = softwareLevel;

        if (monitor.Type == MonitorType.WMI)
        {
            SetBrightnessImmediate(monitor, finalLevel);
        }
        else
        {
            SetBrightnessDebounced(monitor, finalLevel, debounceMs);
        }
    }

    private static void SetBrightnessDebounced(MonitorInfo monitor, int level, int debounceMs)
    {
        if (_debounceTokens.TryGetValue(monitor.UniqueId, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }
        var cts = new CancellationTokenSource();
        _debounceTokens[monitor.UniqueId] = cts;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(debounceMs, cts.Token);
                SetBrightnessImmediate(monitor, level);
            }
            catch (TaskCanceledException) { }
        });
    }

    public static void SetBrightnessImmediate(MonitorInfo monitor, int level)
    {
        try
        {
            if (monitor.Type == MonitorType.WMI)
            {
                var searcher = new ManagementObjectSearcher("root\\Wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
                foreach (ManagementObject m in searcher.Get())
                    m.InvokeMethod("WmiSetBrightness", new object[] { 1, level });
            }
            else if (monitor.Type == MonitorType.DDC)
            {
                NativeMethods.SetVCPFeature(monitor.Handle, 0x10, (uint)level);
            }
        }
        catch { }
    }

    // 正向插值：软件 -> 硬件
    public static int Interpolate(int input, Dictionary<int, int> points)
    {
        var sorted = points.OrderBy(k => k.Key).ToList();
        if (input <= sorted.First().Key) return sorted.First().Value;
        if (input >= sorted.Last().Key) return sorted.Last().Value;
        for (int i = 0; i < sorted.Count - 1; i++)
        {
            if (input >= sorted[i].Key && input <= sorted[i + 1].Key)
            {
                return sorted[i].Value + (input - sorted[i].Key) *
                    (sorted[i + 1].Value - sorted[i].Value) / (sorted[i + 1].Key - sorted[i].Key);
            }
        }
        return input;
    }

    // 反向插值：硬件 -> 软件
    public static int ReverseInterpolate(int hardwareVal, Dictionary<int, int> points)
    {
        var sorted = points.OrderBy(k => k.Key).ToList();
        if (hardwareVal <= sorted.First().Value) return sorted.First().Key;
        if (hardwareVal >= sorted.Last().Value) return sorted.Last().Key;
        for (int i = 0; i < sorted.Count - 1; i++)
        {
            int y1 = sorted[i].Value;
            int y2 = sorted[i + 1].Value;
            if ((hardwareVal >= y1 && hardwareVal <= y2) || (hardwareVal <= y1 && hardwareVal >= y2))
            {
                int x1 = sorted[i].Key;
                int x2 = sorted[i + 1].Key;
                if (y1 == y2) return x1;
                return x1 + (hardwareVal - y1) * (x2 - x1) / (y2 - y1);
            }
        }
        return hardwareVal;
    }

    // 读取 DDC 硬件亮度
    public static int GetVCPBrightness(IntPtr hMonitor)
    {
        uint current = 50, max = 100;
        try
        {
            if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(hMonitor, 0x10, IntPtr.Zero, ref current, ref max))
                return (int)current;
        }
        catch { }
        return -1;
    }
}

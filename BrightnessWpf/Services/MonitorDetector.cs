using System.Management;
using BrightnessWpf.Models;
using BrightnessWpf.Native;

namespace BrightnessWpf.Services;

/// <summary>
/// 显示器检测。迁移第 2 步：复用原版 WMI + DDC 枚举逻辑，全部在后台线程执行，避免阻塞 UI。
/// </summary>
public class MonitorDetector
{
    private readonly AppConfig _config;

    public MonitorDetector(AppConfig config)
    {
        _config = config;
    }

    public async Task<List<MonitorInfo>> DetectMonitorsAsync()
    {
        var monitors = new List<MonitorInfo>();
        var addedHandles = new HashSet<IntPtr>();

        // 1. WMI 内置屏幕
        await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\Wmi", "SELECT * FROM WmiMonitorBrightness");
                foreach (ManagementObject queryObj in searcher.Get())
                {
                    string id = queryObj["InstanceName"]?.ToString() ?? "UnknownWmi";
                    if (!monitors.Any(m => m.InstanceId == id))
                    {
                        string uniqueId = "WMI_" + NativeMethods.GetStableHash(id);
                        string name = _config.CustomNames.TryGetValue(uniqueId, out var cn) ? cn : "内置屏幕";
                        monitors.Add(new MonitorInfo
                        {
                            Type = MonitorType.WMI,
                            Name = name,
                            InstanceId = id,
                            UniqueId = uniqueId,
                            MethodName = "WMI"
                        });
                    }
                }
            }
            catch { }
        });

        // 2. DDC/CI 外接显示器
        await Task.Run(() =>
        {
            try
            {
                NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                    (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.Rect lprcMonitor, IntPtr dwData) =>
                    {
                        int count = 0;
                        NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, ref count);
                        if (count > 0)
                        {
                            var pMs = new NativeMethods.PHYSICAL_MONITOR[count];
                            if (NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, pMs))
                            {
                                for (int i = 0; i < pMs.Length; i++)
                                {
                                    if (addedHandles.Contains(pMs[i].hPhysicalMonitor))
                                        continue;
                                    addedHandles.Add(pMs[i].hPhysicalMonitor);

                                    string originalName = new string(pMs[i].szPhysicalMonitorDescription).Trim('\0');
                                    string uniqueId = NativeMethods.GetMonitorUniqueId(originalName, pMs[i].hPhysicalMonitor, i);

                                    string name = originalName;
                                    if (_config.CustomNames.TryGetValue(uniqueId, out var customName))
                                    {
                                        name = customName;
                                    }
                                    else
                                    {
                                        // 旧版ID迁移
                                        string oldUniqueId = "DDC_" + NativeMethods.GetStableHash(originalName) + "_IDX_" + i;
                                        if (_config.CustomNames.TryGetValue(oldUniqueId, out var oldName))
                                        {
                                            name = oldName;
                                            _config.CustomNames[uniqueId] = name;
                                            _config.Save();
                                        }
                                    }

                                    monitors.Add(new MonitorInfo
                                    {
                                        Type = MonitorType.DDC,
                                        Name = name,
                                        Handle = pMs[i].hPhysicalMonitor,
                                        UniqueId = uniqueId,
                                        MethodName = "DDC/CI"
                                    });
                                }
                            }
                        }
                        return true;
                    }, IntPtr.Zero);
            }
            catch { }
        });

        return monitors;
    }
}

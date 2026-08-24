namespace BrightnessWpf.Models;

public enum MonitorType
{
    WMI,
    DDC
}

public class MonitorInfo
{
    public string Name { get; set; } = "Unknown";
    public MonitorType Type { get; set; }
    public IntPtr Handle { get; set; }
    public string InstanceId { get; set; } = "";
    public string UniqueId { get; set; } = "";
    public int LastBrightness { get; set; } = 50;
    public bool IsAdjustable { get; set; } = true;
    public string MethodName { get; set; } = "Unknown";
}

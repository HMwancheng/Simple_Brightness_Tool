using System;

namespace SimpleBrightness.Model
{
    public enum MonitorType { WMI, DDC }

    public class MonitorInfo
    {
        public string Name { get; set; } = "Unknown";
        public MonitorType Type { get; set; }
        public IntPtr Handle { get; set; }
        public string InstanceId { get; set; } = "";
        public string UniqueId { get; set; } = "";
        public int LastBrightness { get; set; } = 50;
    }
}

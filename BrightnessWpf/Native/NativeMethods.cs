using System.Runtime.InteropServices;

namespace BrightnessWpf.Native;

/// <summary>
/// 系统级 P/Invoke。迁移第 2 步：DDC/CI 显示器亮度控制所需的 Win32 调用。
/// </summary>
public static class NativeMethods
{
    // ================== DDC/CI ==================

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

    public delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

    [DllImport("dxva2.dll")]
    public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref int pdwNumberOfPhysicalMonitors);

    [DllImport("dxva2.dll", EntryPoint = "GetPhysicalMonitorsFromHMONITOR")]
    public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, int dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll")]
    public static extern bool SetVCPFeature(IntPtr hMonitor, byte bVCPCode, uint dwNewValue);

    [DllImport("dxva2.dll")]
    public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte bVCPCode, IntPtr pvct, ref uint pdwCurrentValue, ref uint pdwMaximumValue);

    [DllImport("dxva2.dll")]
    public static extern bool DestroyPhysicalMonitors(int dwPhysicalMonitorArraySize, [In] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    // ================== 托盘图标 ==================

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONIDENTIFIER
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out Rect iconLocation);

    // ================== Helpers ==================

    public static string GetStableHash(string str)
    {
        ulong hash = 5381;
        foreach (char c in str)
            hash = ((hash << 5) + hash) + c;
        return hash.ToString();
    }

    // 生成唯一ID，包含显示器句柄避免同名冲突
    public static string GetMonitorUniqueId(string originalName, IntPtr hMonitor, int index)
    {
        string nameHash = GetStableHash(originalName);
        int handleId = hMonitor.ToInt32();
        return $"DDC_{nameHash}_H{handleId}_IDX_{index}";
    }
}

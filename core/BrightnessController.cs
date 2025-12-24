using System;
using System.Collections.Concurrent;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using SimpleBrightness.Model;
using SimpleBrightness.Utils;

namespace SimpleBrightness.Core
{
    public static class BrightnessController 
    {
        private static ConcurrentDictionary<string, CancellationTokenSource> _debounceTokens = new ConcurrentDictionary<string, CancellationTokenSource>();

        public static void SetBrightnessDebounced(MonitorInfo monitor, int level, int debounceMs) {
            if (_debounceTokens.TryGetValue(monitor.UniqueId, out CancellationTokenSource? oldCts)) { oldCts.Cancel(); oldCts.Dispose(); }
            var cts = new CancellationTokenSource(); _debounceTokens[monitor.UniqueId] = cts;
            Task.Run(async () => {
                try { await Task.Delay(debounceMs, cts.Token); SetBrightnessImmediate(monitor, level); } catch (TaskCanceledException) { }
            });
        }

        public static void SetBrightnessImmediate(MonitorInfo monitor, int level) {
            if (monitor.Type == MonitorType.WMI) { 
                try { 
                    var searcher = new ManagementObjectSearcher("root\\Wmi", "SELECT * FROM WmiMonitorBrightnessMethods"); 
                    foreach (ManagementObject m in searcher.Get()) m.InvokeMethod("WmiSetBrightness", new object[] { 1, level }); 
                } catch {} 
            } else if (monitor.Type == MonitorType.DDC) { 
                NativeMethods.SetVCPFeature(monitor.Handle, 0x10, (uint)level); 
            } 
        }

        public static void SetPowerState(MonitorInfo monitor, bool turnOn, bool useSoftwareMode) { 
            if (useSoftwareMode) {
                if (turnOn) {
                    NativeMethods.mouse_event(0x0001, 0, 1, 0, UIntPtr.Zero);
                    Thread.Sleep(10);
                    NativeMethods.mouse_event(0x0001, 0, -1, 0, UIntPtr.Zero);
                } else {
                    NativeMethods.SendMessage(new IntPtr(0xFFFF), 0x0112, 0xF170, 2);
                }
            } else if (monitor.Type == MonitorType.DDC) {
                uint code = turnOn ? 0x01u : 0x04u; 
                NativeMethods.SetVCPFeature(monitor.Handle, 0xD6, code); 
            }
        } 

        public static int GetVCPBrightness(IntPtr hMonitor) { 
            uint current = 50, max = 100; 
            if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(hMonitor, 0x10, IntPtr.Zero, ref current, ref max)) return (int)current; 
            return -1; 
        } 
    }
}

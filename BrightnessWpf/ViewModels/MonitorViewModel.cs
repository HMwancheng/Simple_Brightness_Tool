using CommunityToolkit.Mvvm.ComponentModel;
using BrightnessWpf.Models;

namespace BrightnessWpf.ViewModels;

public partial class MonitorViewModel : ObservableObject
{
    public MonitorInfo Monitor { get; }

    [ObservableProperty]
    private int _brightness;

    [ObservableProperty]
    private bool _isAdjustable = true;

    [ObservableProperty]
    private string _methodName = "Unknown";

    [ObservableProperty]
    private string _statusText = "";

    public string DisplayName => Monitor.Name;
    public string UniqueId => Monitor.UniqueId;
    public bool IsDdc => Monitor.Type == MonitorType.DDC;

    public MonitorViewModel(MonitorInfo monitor)
    {
        Monitor = monitor;
        _brightness = monitor.LastBrightness;
        _isAdjustable = monitor.IsAdjustable;
        _methodName = monitor.MethodName;

        if (!monitor.IsAdjustable)
            _statusText = "该显示器不支持硬件亮度调节";
    }

    partial void OnBrightnessChanged(int value)
    {
        Monitor.LastBrightness = value;
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BrightnessWpf.Models;
using BrightnessWpf.Services;

namespace BrightnessWpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly MonitorDetector _detector;

    [ObservableProperty]
    private ObservableCollection<MonitorViewModel> _monitors = new();

    [ObservableProperty]
    private bool _isLoading;

    public string HotkeyIncrease => _config.HotkeyIncrease;
    public string HotkeyDecrease => _config.HotkeyDecrease;

    public MainViewModel()
    {
        _config = AppConfig.Load();
        _detector = new MonitorDetector(_config);
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            var monitorInfos = await _detector.DetectMonitorsAsync();

            // DDC 显示器用 VCP 探测是否支持硬件调节（后台线程，避免 UI 卡顿）
            await Task.Run(() =>
            {
                foreach (var info in monitorInfos)
                {
                    if (info.Type == MonitorType.DDC)
                    {
                        int v = BrightnessService.GetVCPBrightness(info.Handle);
                        if (v < 0)
                        {
                            info.IsAdjustable = false;
                            info.MethodName = "不支持";
                        }
                    }
                }
            });

            Monitors.Clear();
            foreach (var info in monitorInfos)
            {
                Monitors.Add(new MonitorViewModel(info));
            }

            await RestoreBrightnessAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Init error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RestoreBrightnessAsync()
    {
        foreach (var vm in Monitors)
        {
            if (_config.SavedBrightness.TryGetValue(vm.UniqueId, out int saved))
            {
                vm.Brightness = saved;
            }
            else
            {
                // 无保存值，尝试读取硬件当前亮度
                await Task.Run(() =>
                {
                    if (vm.Monitor.Type == MonitorType.DDC)
                    {
                        int hw = BrightnessService.GetVCPBrightness(vm.Monitor.Handle);
                        if (hw >= 0)
                        {
                            var curve = _config.GetCurveForMonitor(vm.UniqueId);
                            int sw = BrightnessService.ReverseInterpolate(hw, curve);
                            App.Current.Dispatcher.Invoke(() => vm.Brightness = sw);
                        }
                    }
                });
            }
        }
    }

    [RelayCommand]
    public async Task SetBrightnessAsync(MonitorViewModel? vm)
    {
        if (vm == null || !vm.IsAdjustable) return;

        var curve = _config.GetCurveForMonitor(vm.UniqueId);
        var monitor = vm.Monitor;
        int level = vm.Brightness;

        // 硬件调用（WMI/DDC）放后台线程，避免阻塞 UI
        await Task.Run(() => BrightnessService.SetBrightness(monitor, level, _config.DebounceTime, curve));

        _config.SavedBrightness[vm.UniqueId] = level;
        _config.Save();
    }

    [RelayCommand]
    public async Task SyncAllAsync()
    {
        if (Monitors.Count == 0) return;
        int target = Monitors[0].Brightness;
        foreach (var vm in Monitors)
        {
            vm.Brightness = target;
            await SetBrightnessAsync(vm);
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await InitializeAsync();
    }

    /// <summary>
    /// 托盘滚轮：按步长调整所有可调显示器的亮度（硬件调用在后台线程）。
    /// </summary>
    public void AdjustAllByStep(int wheelDelta)
    {
        if (Monitors.Count == 0) return;

        int change = wheelDelta > 0 ? _config.ScrollStep : -_config.ScrollStep;
        var pairs = Monitors.Where(vm => vm.IsAdjustable)
                            .Select(vm => (vm, newVal: Math.Clamp(vm.Brightness + change, 0, 100)))
                            .ToList();
        foreach (var (vm, newVal) in pairs)
            vm.Brightness = newVal;

        Task.Run(() =>
        {
            foreach (var (vm, newVal) in pairs)
                BrightnessService.SetBrightness(vm.Monitor, newVal, _config.DebounceTime, _config.GetCurveForMonitor(vm.UniqueId));
        });
    }

    public void SaveSettings()
    {
        foreach (var vm in Monitors)
            _config.SavedBrightness[vm.UniqueId] = vm.Brightness;
        _config.Save();
    }
}

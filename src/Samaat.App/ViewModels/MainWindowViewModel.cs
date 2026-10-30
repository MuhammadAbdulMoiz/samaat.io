using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Samaat.Core.Battery;
using Samaat.Core.Devices;

namespace Samaat.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private const int MaxAlerts = 3;

    private readonly DeviceRegistry _registry;
    private readonly TimeProvider _time;
    private readonly LowBatteryDetector _alerts = new();
    private readonly DispatcherTimer _ageTimer;

    public MainWindowViewModel(AppOptions options, TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _registry = new DeviceRegistry(options.CreateSources(_time));
        _registry.DevicesChanged += (_, _) => Dispatcher.UIThread.Post(SyncDevices);
        _registry.SourceFailed += (_, failure) => Dispatcher.UIThread.Post(() =>
            StatusText = $"{failure.Source.Name} failed: {failure.Error.Message}");

        if (options.UsesSimulator)
        {
            ModeText = options.HasRealBluetooth
                ? "Simulator enabled alongside real Bluetooth devices."
                : "Simulation mode: real Bluetooth is only implemented for Windows so far.";
        }

        _ageTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            foreach (var device in Devices)
            {
                device.UpdateAges();
            }
        });
        _ageTimer.Start();
    }

    public ObservableCollection<DeviceViewModel> Devices { get; } = [];

    public ObservableCollection<string> Alerts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial DeviceViewModel? SelectedDevice { get; set; }

    public bool HasSelection => SelectedDevice is not null;

    [ObservableProperty]
    public partial string? ModeText { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Looking for devices…";

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [RelayCommand]
    public Task RefreshDevicesAsync() => _registry.RefreshAsync();

    [RelayCommand]
    private void DismissAlerts() => Alerts.Clear();

    private void SyncDevices()
    {
        var current = _registry.Devices;
        foreach (var stale in Devices.Where(vm => !current.Contains(vm.Device)).ToList())
        {
            stale.Dispose();
            Devices.Remove(stale);
        }

        foreach (var device in current.Where(d => Devices.All(vm => vm.Device != d)))
        {
            Devices.Add(new DeviceViewModel(device, _alerts, _time, OnAlert));
        }

        SelectedDevice ??= Devices.FirstOrDefault();
        if (SelectedDevice is not null && !Devices.Contains(SelectedDevice))
        {
            SelectedDevice = Devices.FirstOrDefault();
        }

        IsEmpty = Devices.Count == 0;
        StatusText = IsEmpty
            ? "No paired Bluetooth audio devices found. Pair your earbuds in system settings, then refresh."
            : $"{Devices.Count} device{(Devices.Count == 1 ? "" : "s")}";
    }

    // Called during DeviceViewModel construction too, before it is in Devices, so the VM is passed in.
    private void OnAlert(DeviceViewModel device, BatteryAlert alert)
    {
        var part = alert.Component switch
        {
            BatteryComponent.Left => "left bud",
            BatteryComponent.Right => "right bud",
            BatteryComponent.Case => "case",
            _ => "battery",
        };
        var level = alert.Level == BatteryAlertLevel.Critical ? "critically low" : "low";

        Alerts.Insert(0, $"{device.Name}: {part} {level} ({alert.Percent}%)");
        while (Alerts.Count > MaxAlerts)
        {
            Alerts.RemoveAt(Alerts.Count - 1);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _ageTimer.Stop();
        foreach (var device in Devices)
        {
            device.Dispose();
        }

        await _registry.DisposeAsync();
    }
}

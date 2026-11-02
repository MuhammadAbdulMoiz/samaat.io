using Samaat.App.Features.Overview;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Samaat.Core.Battery;
using Samaat.Core.Devices;

namespace Samaat.App.Shell;

public enum AppSection
{
    Overview,
    Sound,
    Connections,
    Info,
}

/// <summary>What the content area shows: one page of one device. Switching pages cross-fades.</summary>
public abstract record PageModel(DeviceViewModel Device);

public sealed record OverviewPageModel(DeviceViewModel Device) : PageModel(Device);

public sealed record SoundPageModel(DeviceViewModel Device) : PageModel(Device);

public sealed record ConnectionsPageModel(DeviceViewModel Device) : PageModel(Device);

public sealed record InfoPageModel(DeviceViewModel Device) : PageModel(Device);

public sealed partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private const int MaxAlerts = 3;

    /// <summary>Toasts disappear on their own; the alert counter in the top bar keeps the state.</summary>
    public static readonly TimeSpan ToastLifetime = TimeSpan.FromSeconds(8);

    private readonly List<ITimer> _toastTimers = [];

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

    [ObservableProperty]
    public partial PageModel? CurrentPage { get; set; }

    /// <summary>How many devices currently have a low or critical battery.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlertingDevices), nameof(AlertCountText))]
    public partial int AlertingDeviceCount { get; set; }

    public bool HasAlertingDevices => AlertingDeviceCount > 0;

    public string AlertCountText => AlertingDeviceCount == 1 ? "1 low battery" : $"{AlertingDeviceCount} low batteries";

    partial void OnSelectedDeviceChanged(DeviceViewModel? value) => UpdatePage();

    partial void OnSectionChanged(AppSection value) => UpdatePage();

    private void UpdatePage() => CurrentPage = SelectedDevice is not { } device ? null : Section switch
    {
        AppSection.Sound => new SoundPageModel(device),
        AppSection.Connections => new ConnectionsPageModel(device),
        AppSection.Info => new InfoPageModel(device),
        _ => new OverviewPageModel(device),
    };

    [RelayCommand]
    private void ShowAlertingDevice()
    {
        if (Devices.FirstOrDefault(d => d.HasAlert) is { } device)
        {
            SelectedDevice = device;
            Section = AppSection.Overview;
        }
    }

    private void UpdateAlertCount() => AlertingDeviceCount = Devices.Count(d => d.HasAlert);

    private void OnDevicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceViewModel.HasAlert))
        {
            UpdateAlertCount();
        }
    }

    public bool HasSelection => SelectedDevice is not null;

    [ObservableProperty]
    public partial string? ModeText { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Looking for devices…";

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverview), nameof(IsSound), nameof(IsConnections), nameof(IsInfo))]
    public partial AppSection Section { get; set; }

    public bool IsOverview => Section == AppSection.Overview;

    public bool IsSound => Section == AppSection.Sound;

    public bool IsConnections => Section == AppSection.Connections;

    public bool IsInfo => Section == AppSection.Info;

    [RelayCommand]
    private void Navigate(AppSection section) => Section = section;

    [RelayCommand]
    public Task RefreshDevicesAsync() => _registry.RefreshAsync();

    [RelayCommand]
    private void DismissAlerts() => Alerts.Clear();

    [RelayCommand]
    private void DismissAlert(string alert) => Alerts.Remove(alert);

    private void SyncDevices()
    {
        var current = _registry.Devices;
        foreach (var stale in Devices.Where(vm => !current.Contains(vm.Device)).ToList())
        {
            stale.PropertyChanged -= OnDevicePropertyChanged;
            stale.Dispose();
            Devices.Remove(stale);
        }

        foreach (var device in current.Where(d => Devices.All(vm => vm.Device != d)))
        {
            var vm = new DeviceViewModel(device, _alerts, _time, OnAlert);
            vm.PropertyChanged += OnDevicePropertyChanged;
            Devices.Add(vm);
        }

        SelectedDevice ??= Devices.FirstOrDefault();
        if (SelectedDevice is not null && !Devices.Contains(SelectedDevice))
        {
            SelectedDevice = Devices.FirstOrDefault();
        }

        UpdateAlertCount();
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

        var text = $"{device.Name}: {part} {level} ({alert.Percent}%)";
        Alerts.Insert(0, text);
        ITimer? timer = null;
        timer = _time.CreateTimer(_ => Dispatcher.UIThread.Post(() =>
        {
            Alerts.Remove(text);
            _toastTimers.Remove(timer!);
            timer!.Dispose();
        }), null, ToastLifetime, Timeout.InfiniteTimeSpan);
        _toastTimers.Add(timer);
        while (Alerts.Count > MaxAlerts)
        {
            Alerts.RemoveAt(Alerts.Count - 1);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _ageTimer.Stop();
        foreach (var timer in _toastTimers)
        {
            timer.Dispose();
        }

        _toastTimers.Clear();
        foreach (var device in Devices)
        {
            device.Dispose();
        }

        await _registry.DisposeAsync();
    }
}

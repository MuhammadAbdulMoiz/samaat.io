using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Devices;

namespace Samaat.App.ViewModels;

public sealed partial class DeviceViewModel : ObservableObject, IDisposable
{
    private readonly IEarbudDevice _device;
    private readonly IBatteryFeature? _battery;
    private readonly LowBatteryDetector _alerts;
    private readonly TimeProvider _time;
    private readonly Action<DeviceViewModel, BatteryAlert> _onAlert;

    public DeviceViewModel(IEarbudDevice device, LowBatteryDetector alerts, TimeProvider time, Action<DeviceViewModel, BatteryAlert> onAlert)
    {
        _device = device;
        _battery = device.GetFeature<IBatteryFeature>();
        _alerts = alerts;
        _time = time;
        _onAlert = onAlert;

        Tiles = new ObservableCollection<BatteryTileViewModel>(
            new[] { BatteryComponent.Combined, BatteryComponent.Left, BatteryComponent.Right, BatteryComponent.Case }
                .Select(c => new BatteryTileViewModel(c)));

        _device.ConnectionChanged += OnDeviceChanged;
        if (_battery is not null)
        {
            _battery.Changed += OnBatteryChanged;
        }

        Update();
    }

    public IEarbudDevice Device => _device;

    public string Name => _device.Identity.Name;

    public string Subtitle => string.Join(" · ", new[]
    {
        _device.Identity.Manufacturer,
        _device.Identity.Model,
        _device.Identity.FirmwareVersion is { } fw ? $"Firmware {fw}" : null,
    }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string TierText => _device.Identity.Tier switch
    {
        SupportTier.Full => "Full support",
        SupportTier.Standard => "Standard support",
        _ => "Basic support",
    };

    public ObservableCollection<BatteryTileViewModel> Tiles { get; }

    public ObservableCollection<CapabilityItemViewModel> Capabilities { get; } = [];

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial string ConnectionText { get; set; } = "";

    /// <summary>Short battery summary for the device list.</summary>
    [ObservableProperty]
    public partial string SummaryText { get; set; } = "";

    [ObservableProperty]
    public partial string SourceText { get; set; } = "";

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_battery is not null)
        {
            await _battery.RefreshAsync();
        }
    }

    public void UpdateAges()
    {
        var now = _time.GetUtcNow();
        foreach (var tile in Tiles)
        {
            tile.UpdateAge(now);
        }
    }

    private void OnDeviceChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Update);

    private void OnBatteryChanged(object? sender, BatteryState state) => Dispatcher.UIThread.Post(Update);

    private void Update()
    {
        var now = _time.GetUtcNow();
        var state = _battery?.Current ?? BatteryState.Empty;
        var capabilities = new CapabilitySet(BatteryCapabilities.For(_device));

        IsConnected = _device.Connection == ConnectionState.Connected;
        ConnectionText = _device.Connection switch
        {
            ConnectionState.Connected => "Connected",
            ConnectionState.Connecting => "Connecting…",
            _ => "Disconnected",
        };

        foreach (var tile in Tiles)
        {
            var capability = capabilities.Get(BatteryCapabilities.FeatureFor(tile.Component));
            tile.Update(capability, state.Get(tile.Component), _alerts.Policy, now);
        }

        // "Overall" only makes sense for devices that report a single level.
        var combined = Tiles.Single(t => t.Component == BatteryComponent.Combined);
        combined.IsVisible = capabilities.Get(FeatureId.BatteryCombined).Device == DeviceSupport.Supported;

        SummaryText = state.SummaryPercent is { } summary ? $"{summary}%" : IsConnected ? "Battery unknown" : "—";
        SourceText = _battery?.Source switch
        {
            CapabilitySource.OperatingSystem => "Battery reported by Windows (single level)",
            CapabilitySource.StandardService => "Battery from the Bluetooth Battery Service",
            CapabilitySource.Simulator => "Simulated device",
            null => "This device doesn't report battery to Samaat yet",
            var source => $"Battery from {source}",
        };

        Capabilities.Clear();
        foreach (var capability in capabilities.All.OrderBy(c => c.UiState))
        {
            if (capability.Feature == FeatureId.BatteryCombined && capability.UiState != CapabilityUiState.Enabled && !combined.IsVisible)
            {
                continue;
            }

            Capabilities.Add(new CapabilityItemViewModel(capability));
        }

        foreach (var alert in _alerts.Evaluate(_device.Identity.Id, state))
        {
            _onAlert(this, alert);
        }
    }

    public void Dispose()
    {
        _device.ConnectionChanged -= OnDeviceChanged;
        if (_battery is not null)
        {
            _battery.Changed -= OnBatteryChanged;
        }

        _alerts.Forget(_device.Identity.Id);
    }
}

public sealed class CapabilityItemViewModel(Capability capability)
{
    public string Name { get; } = capability.Feature.Value switch
    {
        "battery.left" => "Left bud battery",
        "battery.right" => "Right bud battery",
        "battery.case" => "Case battery",
        "battery.combined" => "Overall battery",
        var other => other,
    };

    public bool IsEnabled { get; } = capability.UiState == CapabilityUiState.Enabled;

    public string StatusText { get; } = capability.UiState == CapabilityUiState.Enabled
        ? capability.Verified == Verification.Verified ? "Available" : "Available · unverified"
        : capability.DisabledReason ?? "";
}

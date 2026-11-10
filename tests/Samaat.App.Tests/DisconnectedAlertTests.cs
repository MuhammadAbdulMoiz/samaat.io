using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Samaat.App.Features.Overview;
using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Devices;

namespace Samaat.App.Tests;

/// <summary>Windows keeps the last level of disconnected earbuds; that stale level must not raise alerts.</summary>
public sealed class DisconnectedAlertTests
{
    private sealed class Battery : IBatteryFeature
    {
        public IReadOnlySet<BatteryComponent> Components { get; } = new HashSet<BatteryComponent> { BatteryComponent.Combined };
        public bool ComponentsAreExhaustive => true;
        public CapabilitySource Source => CapabilitySource.OperatingSystem;
        public Verification Verification => Verification.Unverified;
        public BatteryState Current { get; } = BatteryState.Empty.With([new BatteryReading(BatteryComponent.Combined, 8, DateTimeOffset.UtcNow)]);
        public event EventHandler<BatteryState>? Changed { add { } remove { } }
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Device(IBatteryFeature battery, ConnectionState connection) : IEarbudDevice
    {
        public DeviceIdentity Identity { get; } = new("d", "Buds");
        public ConnectionState Connection { get; private set; } = connection;
        public IReadOnlyCollection<IFeature> Features { get; } = [battery];
        public event EventHandler? ConnectionChanged;

        public void Connect()
        {
            Connection = ConnectionState.Connected;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [AvaloniaFact]
    public void DisconnectedDevice_RaisesNoAlert_ThenOneOnceConnected()
    {
        var alerts = new List<BatteryAlert>();
        var device = new Device(new Battery(), ConnectionState.Disconnected);

        using var vm = new DeviceViewModel(device, new LowBatteryDetector(), TimeProvider.System, (_, a) => alerts.Add(a));
        Assert.Empty(alerts);
        Assert.Equal("8%", vm.SummaryShort);

        device.Connect();
        Dispatcher.UIThread.RunJobs();

        Assert.Single(alerts);
        Assert.Equal(BatteryAlertLevel.Critical, alerts[0].Level);
    }
}

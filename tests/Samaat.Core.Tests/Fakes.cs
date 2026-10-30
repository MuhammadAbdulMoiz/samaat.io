using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Devices;

namespace Samaat.Core.Tests;

internal sealed class FakeBattery(IEnumerable<BatteryComponent> components, bool exhaustive) : IBatteryFeature
{
    public IReadOnlySet<BatteryComponent> Components { get; } = components.ToHashSet();

    public bool ComponentsAreExhaustive { get; } = exhaustive;

    public CapabilitySource Source => CapabilitySource.RuntimeQuery;

    public Verification Verification => Verification.Unverified;

    public BatteryState Current { get; set; } = BatteryState.Empty;

    public event EventHandler<BatteryState>? Changed;

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Changed?.Invoke(this, Current);
        return Task.CompletedTask;
    }
}

internal sealed class FakeDevice(IBatteryFeature? battery, ConnectionState connection = ConnectionState.Connected) : IEarbudDevice
{
    public DeviceIdentity Identity { get; } = new("fake", "Fake Buds");

    public ConnectionState Connection { get; } = connection;

    public IReadOnlyCollection<IFeature> Features { get; } = battery is null ? [] : [battery];

    public event EventHandler? ConnectionChanged
    {
        add { }
        remove { }
    }
}

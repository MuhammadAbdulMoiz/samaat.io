using Samaat.Core.Battery;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;

namespace Samaat.Core.Tests;

public class DriverRetryTests
{
    private static FakeDevice Device(DriverResolution? resolution, SupportTier tier, bool battery = true)
    {
        var extra = resolution is null ? [] : new IFeature[] { new DeviceDiagnostics(new DeviceSignals("Buds"), resolution) };
        return new FakeDevice(battery ? new FakeBattery([BatteryComponent.Combined], true) : null, extraFeatures: extra)
        {
            Identity = new DeviceIdentity("fake", "Fake Buds", Tier: tier),
        };
    }

    private static DriverResolution Driver(bool fallback = false) => new(DriverResolutionKind.Driver, null, null, fallback);

    [Fact]
    public void NeedsDriverRetry_ModelDriverWithoutFullTier_True() =>
        Assert.True(DriverRetry.NeedsDriverRetry(Device(Driver(), SupportTier.Basic)));

    [Fact]
    public void NeedsDriverRetry_FullTier_False() =>
        Assert.False(DriverRetry.NeedsDriverRetry(Device(Driver(), SupportTier.Full)));

    [Fact]
    public void NeedsDriverRetry_ProtocolFallback_False() =>
        Assert.False(DriverRetry.NeedsDriverRetry(Device(Driver(fallback: true), SupportTier.Basic)));

    [Fact]
    public void NeedsDriverRetry_NoDiagnostics_False() =>
        Assert.False(DriverRetry.NeedsDriverRetry(Device(null, SupportTier.Basic)));

    [Theory]
    [InlineData(DriverResolutionKind.Generic)]
    [InlineData(DriverResolutionKind.KnownModelNoDriver)]
    public void NeedsDriverRetry_GenericOrKnownModelNoDriver_False(DriverResolutionKind kind) =>
        Assert.False(DriverRetry.NeedsDriverRetry(Device(new DriverResolution(kind, null, null), SupportTier.Basic)));

    [Fact]
    public void CanReuse_DriverFailed_False() =>
        Assert.False(DriverRetry.CanReuse(Device(Driver(), SupportTier.Basic)));

    [Fact]
    public void CanReuse_DriverWorked_True() =>
        Assert.True(DriverRetry.CanReuse(Device(Driver(), SupportTier.Full)));

    [Fact]
    public void CanReuse_NoBattery_False() =>
        Assert.False(DriverRetry.CanReuse(Device(null, SupportTier.Basic, battery: false)));

    [Fact]
    public void WatchForReconnect_OneTransition_OneRetry()
    {
        var device = new FakeDevice(null, ConnectionState.Disconnected);
        var retries = 0;
        using var _ = DriverRetry.WatchForReconnect(device, () => retries++);

        device.Connection = ConnectionState.Connected;
        device.RaiseConnectionChanged();

        Assert.Equal(1, retries);
    }

    [Fact]
    public void WatchForReconnect_RepeatedConnectedEvents_NoExtraRetry()
    {
        var device = new FakeDevice(null, ConnectionState.Disconnected);
        var retries = 0;
        using var _ = DriverRetry.WatchForReconnect(device, () => retries++);

        device.Connection = ConnectionState.Connected;
        device.RaiseConnectionChanged();
        device.RaiseConnectionChanged();
        Assert.Equal(1, retries);

        device.Connection = ConnectionState.Disconnected;
        device.RaiseConnectionChanged();
        device.Connection = ConnectionState.Connected;
        device.RaiseConnectionChanged();
        Assert.Equal(2, retries);
    }

    [Fact]
    public void WatchForReconnect_Disposed_NoRetry()
    {
        var device = new FakeDevice(null, ConnectionState.Disconnected);
        var retries = 0;
        DriverRetry.WatchForReconnect(device, () => retries++).Dispose();

        device.Connection = ConnectionState.Connected;
        device.RaiseConnectionChanged();

        Assert.Equal(0, retries);
    }

    [Fact]
    public void WatchForReconnect_StartsConnected_NoRetryUntilNextTransition()
    {
        var device = new FakeDevice(null, ConnectionState.Connected);
        var retries = 0;
        using var _ = DriverRetry.WatchForReconnect(device, () => retries++);

        device.RaiseConnectionChanged();
        Assert.Equal(0, retries);

        device.Connection = ConnectionState.Disconnected;
        device.RaiseConnectionChanged();
        device.Connection = ConnectionState.Connected;
        device.RaiseConnectionChanged();
        Assert.Equal(1, retries);
    }
}

using Samaat.App.Features.Connections;
using Samaat.App.Features.Overview;
using Samaat.App.Features.Sound;
using Samaat.App.Shell;
using Samaat.Core.Battery;
using Samaat.Core.Capabilities;

namespace Samaat.App.Tests;

public class BatteryTileViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly BatteryAlertPolicy Policy = new();

    private static Capability Cap(DeviceSupport support, Availability? runtime = null) => new(
        FeatureId.BatteryLeft, support,
        support == DeviceSupport.Supported ? ImplementationStatus.Implemented : ImplementationStatus.NotImplemented,
        runtime ?? Availability.Available, Verification.Verified, CapabilityAccess.ReadOnly, CapabilitySource.Simulator);

    [Fact]
    public void Supported_ShowsPercentAndAge()
    {
        var tile = new BatteryTileViewModel(BatteryComponent.Left);

        tile.Update(Cap(DeviceSupport.Supported), new BatteryReading(BatteryComponent.Left, 72, Now.AddSeconds(-30)), Policy, Now);

        Assert.True(tile.IsAvailable);
        Assert.Equal("72%", tile.PercentText);
        Assert.Equal("Updated 30s ago", tile.UpdatedText);
        Assert.False(tile.IsLow);
    }

    [Fact]
    public void Unsupported_IsGreyedWithReason()
    {
        var tile = new BatteryTileViewModel(BatteryComponent.Case);

        tile.Update(Cap(DeviceSupport.Unsupported), null, Policy, Now);

        Assert.False(tile.IsAvailable);
        Assert.Equal("—", tile.PercentText);
        Assert.Equal("Not supported by this model", tile.DetailText);
    }

    [Fact]
    public void Disconnected_KeepsLastValueButExplains()
    {
        var tile = new BatteryTileViewModel(BatteryComponent.Left);

        tile.Update(Cap(DeviceSupport.Supported, Availability.Unavailable("Device disconnected")),
            new BatteryReading(BatteryComponent.Left, 40, Now.AddMinutes(-10)), Policy, Now);

        Assert.False(tile.IsAvailable);
        Assert.Equal("40%", tile.PercentText);
        Assert.Equal("Device disconnected", tile.DetailText);
        Assert.True(tile.IsStale);
    }

    [Theory]
    [InlineData(20, true, false)]
    [InlineData(10, false, true)]
    [InlineData(50, false, false)]
    public void LowAndCriticalFlags(int percent, bool low, bool critical)
    {
        var tile = new BatteryTileViewModel(BatteryComponent.Left);

        tile.Update(Cap(DeviceSupport.Supported), new BatteryReading(BatteryComponent.Left, percent, Now), Policy, Now);

        Assert.Equal(low, tile.IsLow);
        Assert.Equal(critical, tile.IsCritical);
    }

    [Fact]
    public void ChargingIsNeverLow()
    {
        var tile = new BatteryTileViewModel(BatteryComponent.Left);

        tile.Update(Cap(DeviceSupport.Supported),
            new BatteryReading(BatteryComponent.Left, 5, Now, ChargingState.Charging, BudStatus.InCase), Policy, Now);

        Assert.False(tile.IsLow);
        Assert.False(tile.IsCritical);
        Assert.Equal("In case · Charging", tile.DetailText);
    }
}

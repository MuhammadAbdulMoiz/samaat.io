using Microsoft.Extensions.Time.Testing;
using Samaat.Core.Battery;
using Samaat.Core.Connections;
using Samaat.Core.NoiseControl;
using Samaat.Core.Devices;
using Samaat.Simulator;

namespace Samaat.Core.Tests;

public class SimulatorTests
{
    private static SimulatedEarbudDevice Create(SimulatedBatteryLayout layout, TimeProvider? time = null) =>
        new(new DeviceIdentity("sim", "Sim"), layout, time);

    [Theory]
    [InlineData(SimulatedBatteryLayout.BudsAndCase, new[] { BatteryComponent.Left, BatteryComponent.Right, BatteryComponent.Case })]
    [InlineData(SimulatedBatteryLayout.BudsOnly, new[] { BatteryComponent.Left, BatteryComponent.Right })]
    [InlineData(SimulatedBatteryLayout.CombinedOnly, new[] { BatteryComponent.Combined })]
    public void ReportsComponentsOfItsLayout(SimulatedBatteryLayout layout, BatteryComponent[] expected)
    {
        var battery = Create(layout).GetFeature<IBatteryFeature>()!;

        Assert.Equal(expected.ToHashSet(), battery.Components);
        Assert.True(battery.ComponentsAreExhaustive);
        Assert.Equal(expected.Length, battery.Current.Readings.Count);
    }

    [Fact]
    public void WornBudsDrainAndTimestampsAdvance()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
        var device = Create(SimulatedBatteryLayout.BudsAndCase, time);
        var battery = device.GetFeature<IBatteryFeature>()!;
        var before = battery.Current.Get(BatteryComponent.Left)!;
        BatteryState? published = null;
        battery.Changed += (_, s) => published = s;

        time.Advance(TimeSpan.FromSeconds(2));
        device.Tick();

        var after = battery.Current.Get(BatteryComponent.Left)!;
        Assert.Equal(before.Percent - 1, after.Percent);
        Assert.Equal(time.GetUtcNow(), after.UpdatedAt);
        Assert.Same(battery.Current, published);
    }

    [Fact]
    public void LowBudDocksChargesFromCaseAndReturns()
    {
        var device = Create(SimulatedBatteryLayout.BudsAndCase);
        var battery = device.GetFeature<IBatteryFeature>()!;
        var sawInCaseCharging = false;
        var caseStart = battery.Current.Get(BatteryComponent.Case)!.Percent;

        for (var i = 0; i < 200; i++)
        {
            device.Tick();
            var right = battery.Current.Get(BatteryComponent.Right)!;
            sawInCaseCharging |= right is { Status: BudStatus.InCase, Charging: ChargingState.Charging };
        }

        Assert.True(sawInCaseCharging);
        Assert.True(battery.Current.Get(BatteryComponent.Case)!.Percent < caseStart);
    }

    [Fact]
    public void DisconnectedDeviceStopsUpdating()
    {
        var device = Create(SimulatedBatteryLayout.CombinedOnly);
        var battery = device.GetFeature<IBatteryFeature>()!;
        var before = battery.Current;

        device.SetConnected(false);
        device.Tick();

        Assert.Same(before, battery.Current);
        Assert.Equal(ConnectionState.Disconnected, device.Connection);
    }

    [Fact]
    public async Task SourcePresets_CoverEachNoiseAndMultipointShape()
    {
        await using var source = new SimulatorDeviceSource(TimeSpan.FromHours(1), commandLatency: TimeSpan.Zero);
        var devices = source.Devices.ToDictionary(d => d.Identity.Id);

        var pro = devices["sim-buds-pro"];
        Assert.Equal(NoiseControlLayout.Selector, NoiseControlPresentation.LayoutFor(pro.GetFeature<INoiseControlFeature>()!.Options));
        var multipoint = pro.GetFeature<IMultipointFeature>()!;
        Assert.True(multipoint.Current.MultipointEnabled);
        Assert.Contains(multipoint.Current.Hosts, h => h.IsThisHost && h.State == HostLinkState.Active);

        var lite = devices["sim-buds-lite"];
        Assert.Equal(NoiseControlLayout.Toggle, NoiseControlPresentation.LayoutFor(lite.GetFeature<INoiseControlFeature>()!.Options));
        Assert.Null(lite.GetFeature<IMultipointFeature>());

        var headset = devices["sim-headset"];
        Assert.Null(headset.GetFeature<INoiseControlFeature>());
        Assert.Null(headset.GetFeature<IMultipointFeature>());
    }
}

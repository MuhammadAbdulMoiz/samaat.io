using Microsoft.Extensions.Time.Testing;
using Samaat.Core.Battery;
using Samaat.Core.Connections;
using Samaat.Core.Devices;
using Samaat.Simulator;

namespace Samaat.Core.Tests;

/// <summary>
/// The simulator's data sets: the demo devices, a worst case built from realistic values (long, short, non-Latin and
/// emoji names, missing fields, 0 % and 100 %, unreported readings, many linked hosts) and an empty one.
/// </summary>
public sealed class SimulatorScenarioTests
{
    private static async Task<IReadOnlyList<IEarbudDevice>> Devices(SimulatorScenario scenario)
    {
        await using var source = new SimulatorDeviceSource(TimeSpan.FromHours(1), new FakeTimeProvider(), scenario: scenario);
        return await source.DiscoverAsync();
    }

    [Fact]
    public async Task Demo_IsTheDefaultAndKeepsItsThreeDevices()
    {
        await using var source = new SimulatorDeviceSource(TimeSpan.FromHours(1), new FakeTimeProvider());

        var names = (await source.DiscoverAsync()).Select(d => d.Identity.Name);

        Assert.Equal(["Simulated Buds Pro", "Simulated Buds Lite", "Simulated Headset"], names);
    }

    [Fact]
    public async Task Empty_HasNoDevices()
    {
        Assert.Empty(await Devices(SimulatorScenario.Empty));
    }

    [Fact]
    public async Task WorstCase_SpreadsDifferentFailuresAcrossDevices()
    {
        var devices = await Devices(SimulatorScenario.WorstCase);

        Assert.Contains(devices, d => d.Identity.Name.Length > 45);                               // a long, renamed name
        Assert.Contains(devices, d => d.Identity.Name.Length <= 2);                               // a two-letter name
        Assert.Contains(devices, d => d.Identity.Name.Any(c => c is >= '؀' and <= 'ۿ')); // right-to-left script
        Assert.Contains(devices, d => d.Identity.Name.Any(char.IsSurrogate));                     // an emoji
        Assert.Contains(devices, d => d.Identity is { Manufacturer: null, Model: null, FirmwareVersion: null });
        Assert.Contains(devices, d => d.Connection == ConnectionState.Disconnected);
    }

    [Fact]
    public async Task WorstCase_ReportsTheBatteryExtremes()
    {
        var readings = (await Devices(SimulatorScenario.WorstCase))
            .SelectMany(d => d.GetFeature<IBatteryFeature>()!.Current.Readings)
            .ToList();

        Assert.Contains(readings, r => r.Percent == 100);
        Assert.Contains(readings, r => r.Percent == 0);
        Assert.Contains(readings, r => r.Percent is null);
    }

    [Fact]
    public async Task WorstCase_HasMoreLinkedHostsThanFitOnScreen_WithLongNames()
    {
        var hosts = (await Devices(SimulatorScenario.WorstCase))
            .Select(d => d.GetFeature<IMultipointFeature>())
            .OfType<IMultipointFeature>()
            .Single().Current.Hosts;

        Assert.True(hosts.Count >= 6);
        Assert.Contains(hosts, h => h.Name.Length > 40);
    }

    [Fact]
    public void AnUnreportedComponent_HasNoPercent_AndStaysUnreportedAfterATick()
    {
        var device = new SimulatedEarbudDevice(new DeviceIdentity("sim", "Sim"), SimulatedBatteryLayout.BudsAndCase,
            new FakeTimeProvider(), unreported: new HashSet<BatteryComponent> { BatteryComponent.Case });
        var battery = device.GetFeature<IBatteryFeature>()!;

        device.Tick();

        Assert.Null(battery.Current.Readings.Single(r => r.Component == BatteryComponent.Case).Percent);
        Assert.NotNull(battery.Current.Readings.Single(r => r.Component == BatteryComponent.Left).Percent);
    }

    [Fact]
    public void StartingLevels_ReplaceTheLayoutDefaults()
    {
        var device = new SimulatedEarbudDevice(new DeviceIdentity("sim", "Sim"), SimulatedBatteryLayout.CombinedOnly,
            new FakeTimeProvider(), levels: new Dictionary<BatteryComponent, int> { [BatteryComponent.Combined] = 100 });

        Assert.Equal(100, device.GetFeature<IBatteryFeature>()!.Current.Readings.Single().Percent);
    }
}

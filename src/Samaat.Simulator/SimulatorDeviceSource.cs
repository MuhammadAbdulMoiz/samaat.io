using Samaat.Core.Connections;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Core.Equalizer;
using Samaat.Core.NoiseControl;

namespace Samaat.Simulator;

/// <summary>
/// Provides one simulated device per capability shape and ticks their batteries on a timer:
/// Buds Pro has everything, Buds Lite a plain ANC switch, the Headset only a battery level.
/// </summary>
public sealed class SimulatorDeviceSource : IDeviceSource
{
    private readonly IReadOnlyList<SimulatedEarbudDevice> _devices;
    private readonly ITimer _timer;

    public static readonly TimeSpan DefaultCommandLatency = TimeSpan.FromMilliseconds(350);

    public SimulatorDeviceSource(TimeSpan tickInterval, TimeProvider? time = null, TimeSpan? commandLatency = null,
        SimulatorScenario scenario = SimulatorScenario.Demo)
    {
        time ??= TimeProvider.System;
        var latency = commandLatency ?? DefaultCommandLatency;
        _timer = time.CreateTimer(_ => Tick(), null, tickInterval, tickInterval);
        if (scenario != SimulatorScenario.Demo)
        {
            _devices = scenario == SimulatorScenario.WorstCase ? WorstCaseDevices.Create(time, latency) : [];
            return;
        }

        var proNoise = new SimulatedNoiseControl(
            new NoiseControlOptions(
                [NoiseMode.Off, NoiseMode.NoiseCancelling, NoiseMode.Transparency, NoiseMode.Adaptive],
                AncLevel: new LevelRange(0, 10),
                AmbientLevel: new LevelRange(0, 10),
                WindReduction: true,
                ConversationAwareness: true),
            latency, time);
        var proMultipoint = new SimulatedMultipoint(
            new MultipointOptions(MaxConnections: 2, CanSwitch: true, CanDisconnect: true, CanToggleMultipoint: true),
            new MultipointState(true,
            [
                new HostLink("this-pc", "This PC", HostKind.Computer, HostLinkState.Active, IsThisHost: true),
                new HostLink("phone", "Pixel phone", HostKind.Phone, HostLinkState.Connected, IsThisHost: false),
                new HostLink("laptop", "Work laptop", HostKind.Computer, HostLinkState.Paired, IsThisHost: false),
                new HostLink("tablet", "Living-room tablet", HostKind.Tablet, HostLinkState.Unavailable, IsThisHost: false),
            ]),
            latency, time);
        var proEq = new SimulatedEqualizer(
            [new EqPreset(0, "Standard"), new EqPreset(1, "Bass boost"), new EqPreset(2, "Voice"), new EqPreset(3, "Treble boost")],
            latency, time);
        var liteNoise = new SimulatedNoiseControl(new NoiseControlOptions([NoiseMode.Off, NoiseMode.NoiseCancelling]), latency, time);

        _devices =
        [
            new(new DeviceIdentity("sim-buds-pro", "Simulated Buds Pro", "Samaat", "Buds Pro (simulated)", "1.0.0", SupportTier.Full,
                new DeviceDetails { Address = "02:00:5E:10:00:01", VendorId = 0x00D7, ProductId = 0x1001, Services = ["A2DP", "AVRCP", "HFP", "Vendor (simulated)"] }),
                SimulatedBatteryLayout.BudsAndCase, time, [proNoise, proEq, proMultipoint, Diagnostics("Simulated Buds Pro")], latency),
            new(new DeviceIdentity("sim-buds-lite", "Simulated Buds Lite", "Samaat", "Buds Lite (simulated)", "1.0.0", SupportTier.Standard,
                new DeviceDetails { Address = "02:00:5E:10:00:02", VendorId = 0x00D7, ProductId = 0x1002, Services = ["A2DP", "AVRCP", "HFP"] }),
                SimulatedBatteryLayout.BudsOnly, time, [liteNoise, Diagnostics("Simulated Buds Lite")], latency),
            new(new DeviceIdentity("sim-headset", "Simulated Headset", "Samaat", "Headset (simulated)", null, SupportTier.Basic,
                new DeviceDetails { Address = "02:00:5E:10:00:03", VendorId = 0x005D, ProductId = 0x2001, Services = ["A2DP", "HSP"] }),
                SimulatedBatteryLayout.CombinedOnly, time, [Diagnostics("Simulated Headset")], latency),
        ];
    }

    public string Name => "Simulator";

    internal static DeviceDiagnostics Diagnostics(string name)
    {
        var entry = new CatalogEntry($"samaat.{name.ToLowerInvariant().Replace(' ', '-')}", name, "Samaat", SimulatorDriver.Instance.Id,
            new DeviceMatch(NamePattern: $"^{name}$"), Verified: true, VerifiedFirmware: ["1.0.0"], Features: new HashSet<Core.Capabilities.FeatureId>());
        return new DeviceDiagnostics(new DeviceSignals(name, Manufacturer: "Samaat"),
            new DriverResolution(DriverResolutionKind.Driver, entry, SimulatorDriver.Instance));
    }

    public IReadOnlyList<SimulatedEarbudDevice> Devices => _devices;

    // The simulated device list is fixed.
    public event EventHandler? DevicesChanged
    {
        add { }
        remove { }
    }

    public Task<IReadOnlyList<IEarbudDevice>> DiscoverAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IEarbudDevice>>(_devices);

    public void Tick()
    {
        foreach (var device in _devices)
        {
            device.Tick();
        }
    }

    public ValueTask DisposeAsync()
    {
        return _timer.DisposeAsync();
    }
}

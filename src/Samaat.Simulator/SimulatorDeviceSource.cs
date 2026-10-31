using Samaat.Core.Connections;
using Samaat.Core.Devices;
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

    public SimulatorDeviceSource(TimeSpan tickInterval, TimeProvider? time = null, TimeSpan? commandLatency = null)
    {
        time ??= TimeProvider.System;
        var latency = commandLatency ?? DefaultCommandLatency;

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
        var liteNoise = new SimulatedNoiseControl(new NoiseControlOptions([NoiseMode.Off, NoiseMode.NoiseCancelling]), latency, time);

        _devices =
        [
            new(new DeviceIdentity("sim-buds-pro", "Simulated Buds Pro", "Samaat", "Buds Pro (simulated)", "1.0.0", SupportTier.Full),
                SimulatedBatteryLayout.BudsAndCase, time, [proNoise, proMultipoint]),
            new(new DeviceIdentity("sim-buds-lite", "Simulated Buds Lite", "Samaat", "Buds Lite (simulated)", "1.0.0", SupportTier.Standard),
                SimulatedBatteryLayout.BudsOnly, time, [liteNoise]),
            new(new DeviceIdentity("sim-headset", "Simulated Headset", "Samaat", "Headset (simulated)", null, SupportTier.Basic),
                SimulatedBatteryLayout.CombinedOnly, time),
        ];
        _timer = time.CreateTimer(_ => Tick(), null, tickInterval, tickInterval);
    }

    public string Name => "Simulator";

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

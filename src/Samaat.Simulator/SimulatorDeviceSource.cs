using Samaat.Core.Devices;

namespace Samaat.Simulator;

/// <summary>Provides one simulated device per battery layout and ticks them on a timer.</summary>
public sealed class SimulatorDeviceSource : IDeviceSource
{
    private readonly IReadOnlyList<SimulatedEarbudDevice> _devices;
    private readonly ITimer _timer;

    public SimulatorDeviceSource(TimeSpan tickInterval, TimeProvider? time = null)
    {
        time ??= TimeProvider.System;
        _devices =
        [
            new(new DeviceIdentity("sim-buds-pro", "Simulated Buds Pro", "Samaat", "Buds Pro (simulated)", "1.0.0", SupportTier.Full),
                SimulatedBatteryLayout.BudsAndCase, time),
            new(new DeviceIdentity("sim-buds-lite", "Simulated Buds Lite", "Samaat", "Buds Lite (simulated)", "1.0.0", SupportTier.Standard),
                SimulatedBatteryLayout.BudsOnly, time),
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

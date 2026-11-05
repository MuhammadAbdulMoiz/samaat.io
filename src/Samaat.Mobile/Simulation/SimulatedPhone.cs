using Samaat.Core.Commands;
using Samaat.Core.Devices;
using Samaat.Core.Profiles;
using Samaat.Handoff.Discovery;
using Samaat.Handoff.Moving;
using Samaat.Handoff.Storage;
using Samaat.Mobile.Runtime;
using Samaat.Simulator;

namespace Samaat.Mobile.Simulation;

/// <summary>
/// The phone's Bluetooth audio over the simulator's devices: connecting one here marks it connected, as Android would
/// report after the user picks it. Used by tests and by running the mobile UI on a desktop.
/// </summary>
public sealed class SimulatedPhoneAudio : IPhoneAudioLink
{
    private readonly IReadOnlyList<SimulatedEarbudDevice> _devices;

    public SimulatedPhoneAudio(IReadOnlyList<SimulatedEarbudDevice> devices)
    {
        _devices = devices;
        foreach (var device in devices)
        {
            device.ConnectionChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsBluetoothOn { get; set; } = true;

    public string? Blocker { get; set; }

    /// <summary>When set, the next connect fails with this message (like the user ignoring the output picker).</summary>
    public string? FailNextConnect { get; set; }

    public IReadOnlyList<PhoneAudioDevice> Devices =>
        [.. _devices.Select(d => new PhoneAudioDevice(d.Identity.Details!.Address!, d.Identity.Name, d.Connection == ConnectionState.Connected))];

    public event EventHandler? Changed;

    public Task<CommandResult> ConnectAsync(string address, TimeSpan timeout, CancellationToken ct)
    {
        if (FailNextConnect is { } message)
        {
            FailNextConnect = null;
            return Task.FromResult(CommandResult.Rejected(message));
        }

        return Set(address, true);
    }

    public Task<CommandResult> DisconnectAsync(string address, TimeSpan timeout, CancellationToken ct) => Set(address, false);

    private Task<CommandResult> Set(string address, bool connected)
    {
        var device = _devices.FirstOrDefault(d => string.Equals(d.Identity.Details?.Address, address, StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            return Task.FromResult(CommandResult.Rejected("These earbuds aren't paired with the phone."));
        }

        device.SetConnected(connected);
        return Task.FromResult(CommandResult.Confirmed);
    }
}

public static class SimulatedMobile
{
    /// <summary>A runtime over the simulator's three devices, with an in-memory pairing store.</summary>
    public static MobileRuntime Create(IBeaconListener? beacons = null, PeerStore? store = null, TimeProvider? time = null,
        Samaat.Handoff.PhoneLinkClientOptions? clientOptions = null)
    {
        var source = new SimulatorDeviceSource(TimeSpan.FromSeconds(2), time);
        // The same devices/ folder as the desktop, copied next to the app: model names and artwork photos.
        var profiles = DeviceProfileDb.Load(Path.Combine(AppContext.BaseDirectory, "devices"));
        return new MobileRuntime([source], new SimulatedPhoneAudio(source.Devices), store ?? new PeerStore(null), "Simulated phone",
            beacons ?? new InMemoryBeaconBus().CreateListener(), new NoMobilePlatform(), time, clientOptions, profiles);
    }
}

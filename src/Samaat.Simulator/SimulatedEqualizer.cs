using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Equalizer;

namespace Samaat.Simulator;

public sealed class SimulatedEqualizer : IEqualizerFeature
{
    private readonly SimulatedCommandChannel _channel;
    private int _current;

    public SimulatedEqualizer(IReadOnlyList<EqPreset> presets, TimeSpan latency, TimeProvider? time = null)
    {
        Presets = presets;
        _channel = new SimulatedCommandChannel(latency, time);
        _current = presets.Count > 0 ? presets[0].Id : -1;
    }

    public IReadOnlyList<EqPreset> Presets { get; }

    public CapabilitySource Source => CapabilitySource.Simulator;

    public Verification Verification => Verification.Verified;

    public int? CurrentPresetId => Volatile.Read(ref _current) is var id and >= 0 ? id : null;

    public bool FailNextCommand
    {
        get => _channel.FailNextCommand;
        set => _channel.FailNextCommand = value;
    }

    public event EventHandler<int?>? Changed;

    public Task<CommandResult> SetPresetAsync(int presetId, CancellationToken cancellationToken = default) =>
        _channel.SendAsync(
            () => Presets.Any(p => p.Id == presetId) ? null : CommandResult.Unsupported("That EQ preset isn't available on this device."),
            () => Publish(presetId),
            cancellationToken);

    /// <summary>Simulates a change made on the earbuds themselves.</summary>
    public void ApplyFromDevice(int presetId) => Publish(presetId);

    private void Publish(int presetId)
    {
        Volatile.Write(ref _current, presetId);
        Changed?.Invoke(this, presetId);
    }
}

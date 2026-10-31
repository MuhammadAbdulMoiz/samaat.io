using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.NoiseControl;

namespace Samaat.Simulator;

public sealed class SimulatedNoiseControl : INoiseControlFeature
{
    private readonly SimulatedCommandChannel _channel;
    private NoiseControlState _current;

    public SimulatedNoiseControl(NoiseControlOptions options, TimeSpan latency, TimeProvider? time = null)
    {
        Options = options;
        _channel = new SimulatedCommandChannel(latency, time);
        _current = new NoiseControlState(
            NoiseMode.Off,
            options.AncLevel?.Max,
            options.AmbientLevel is { } ambient ? (ambient.Min + ambient.Max) / 2 : null,
            options.WindReduction ? false : null,
            options.ConversationAwareness ? false : null);
    }

    public NoiseControlOptions Options { get; }

    public bool OptionsAreExhaustive => true;

    public CapabilitySource Source => CapabilitySource.Simulator;

    public Verification Verification => Verification.Verified;

    public NoiseControlState Current => Volatile.Read(ref _current);

    public bool FailNextCommand
    {
        get => _channel.FailNextCommand;
        set => _channel.FailNextCommand = value;
    }

    public event EventHandler<NoiseControlState>? Changed;

    public Task<CommandResult> SetModeAsync(NoiseMode mode, CancellationToken cancellationToken = default) =>
        Send(() => Options.ValidateMode(mode), s => s with { Mode = mode }, cancellationToken);

    public Task<CommandResult> SetAncLevelAsync(int level, CancellationToken cancellationToken = default) =>
        Send(() => Options.ValidateAncLevel(level), s => s with { AncLevel = level }, cancellationToken);

    public Task<CommandResult> SetAmbientLevelAsync(int level, CancellationToken cancellationToken = default) =>
        Send(() => Options.ValidateAmbientLevel(level), s => s with { AmbientLevel = level }, cancellationToken);

    public Task<CommandResult> SetWindReductionAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Send(() => Options.WindReduction ? null : CommandResult.Unsupported("Wind noise reduction isn't available on this device."),
            s => s with { WindReduction = enabled }, cancellationToken);

    public Task<CommandResult> SetConversationAwarenessAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Send(() => Options.ConversationAwareness ? null : CommandResult.Unsupported("Conversation awareness isn't available on this device."),
            s => s with { ConversationAwareness = enabled }, cancellationToken);

    /// <summary>Simulates a change made on the earbuds themselves (e.g. a long press).</summary>
    public void ApplyFromDevice(NoiseControlState state) => Publish(state);

    private Task<CommandResult> Send(Func<CommandResult?> validate, Func<NoiseControlState, NoiseControlState> change, CancellationToken ct) =>
        _channel.SendAsync(validate, () => Publish(change(Current)), ct);

    private void Publish(NoiseControlState state)
    {
        Volatile.Write(ref _current, state);
        Changed?.Invoke(this, state);
    }
}

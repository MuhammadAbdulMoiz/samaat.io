using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Connections;

namespace Samaat.Simulator;

public sealed class SimulatedMultipoint(MultipointOptions options, MultipointState initial, TimeSpan latency, TimeProvider? time = null)
    : IMultipointFeature
{
    private readonly SimulatedCommandChannel _channel = new(latency, time);
    private MultipointState _current = initial;

    public MultipointOptions Options { get; } = options;

    public CapabilitySource Source => CapabilitySource.Simulator;

    public Verification Verification => Verification.Verified;

    public MultipointState Current => Volatile.Read(ref _current);

    public bool FailNextCommand
    {
        get => _channel.FailNextCommand;
        set => _channel.FailNextCommand = value;
    }

    public event EventHandler<MultipointState>? Changed;

    public Task<CommandResult> SwitchToAsync(string hostId, CancellationToken cancellationToken = default) =>
        Send(s => MultipointRules.SwitchTo(s, Options, hostId), cancellationToken);

    public Task<CommandResult> DisconnectAsync(string hostId, CancellationToken cancellationToken = default) =>
        Send(s => MultipointRules.Disconnect(s, Options, hostId), cancellationToken);

    public Task<CommandResult> SetMultipointEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Send(s => MultipointRules.SetMultipointEnabled(s, Options, enabled), cancellationToken);

    private Task<CommandResult> Send(Func<MultipointState, (MultipointState State, CommandResult Result)> operation, CancellationToken ct)
    {
        // Validate against the current state first so refused commands fail fast, then apply after the round trip.
        return _channel.SendAsync(
            () => operation(Current) is { Result.Succeeded: false } refused ? refused.Result : null,
            () =>
            {
                var (next, _) = operation(Current);
                if (!ReferenceEquals(next, Current))
                {
                    Volatile.Write(ref _current, next);
                    Changed?.Invoke(this, next);
                }
            },
            ct);
    }
}

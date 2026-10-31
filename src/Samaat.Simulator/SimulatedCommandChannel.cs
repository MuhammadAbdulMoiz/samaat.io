using Samaat.Core.Commands;

namespace Samaat.Simulator;

/// <summary>
/// Stands in for a device's command channel: commands take <see cref="Latency"/> to be confirmed,
/// run one at a time, and the next one can be made to fail to exercise rollback paths.
/// </summary>
public sealed class SimulatedCommandChannel(TimeSpan latency, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _failNext;

    public TimeSpan Latency { get; set; } = latency;

    public bool FailNextCommand
    {
        get => Volatile.Read(ref _failNext) == 1;
        set => Volatile.Write(ref _failNext, value ? 1 : 0);
    }

    /// <summary>Validates, waits for the simulated round trip, then applies the change.</summary>
    public async Task<CommandResult> SendAsync(Func<CommandResult?> validate, Action apply, CancellationToken cancellationToken)
    {
        if (validate() is { } invalid)
        {
            return invalid;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Latency > TimeSpan.Zero)
            {
                await Task.Delay(Latency, _time, cancellationToken);
            }

            if (Interlocked.Exchange(ref _failNext, 0) == 1)
            {
                return CommandResult.Rejected("The earbuds refused the change (simulated failure).");
            }

            apply();
            return CommandResult.Confirmed;
        }
        finally
        {
            _gate.Release();
        }
    }
}

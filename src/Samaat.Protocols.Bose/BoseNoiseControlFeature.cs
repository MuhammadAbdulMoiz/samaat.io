using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.NoiseControl;
using Samaat.Transport;

namespace Samaat.Protocols.Bose;

/// <summary>
/// Read-only noise control over Bose BMAP: the current audio mode (index 0 Quiet, 1 Aware). Its only write is
/// the audio-mode GET; mode switching was never observed, so every setter is unsupported.
/// </summary>
public sealed class BoseNoiseControlFeature : INoiseControlFeature, IAsyncDisposable
{
    private readonly IRfcommChannel _channel;
    private readonly BmapReader _reader = new();
    private readonly Lock _gate = new();
    private NoiseControlState _current;

    private BoseNoiseControlFeature(IRfcommChannel channel, NoiseMode current)
    {
        _channel = channel;
        _current = new NoiseControlState(current);
        _channel.Received += OnReceived;
    }

    public NoiseControlOptions Options { get; } = new([NoiseMode.NoiseCancelling, NoiseMode.Transparency]);

    /// <summary>Indexes 2 and up are user-configured modes Samaat doesn't list.</summary>
    public bool OptionsAreExhaustive => false;

    public CapabilitySource Source => CapabilitySource.RuntimeQuery;

    public Verification Verification => Verification.Unverified;

    public CapabilityAccess Access => CapabilityAccess.ReadOnly;

    public NoiseControlState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event EventHandler<NoiseControlState>? Changed;

    /// <summary>
    /// Asks for the current mode. Null on timeout, an error reply, a closed channel or a mode Samaat doesn't know;
    /// the caller still owns (and disposes) the channel then.
    /// </summary>
    public static async Task<BoseNoiseControlFeature?> CreateAsync(IRfcommChannel channel, TimeProvider time, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var reader = new BmapReader();
        var answer = new TaskCompletionSource<byte?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnChunk(object? sender, ReadOnlyMemory<byte> chunk)
        {
            foreach (var packet in reader.Feed(chunk.Span))
            {
                if (packet.Block != BmapFrame.AudioBlock || packet.Function != BmapFrame.CurrentModeFunction)
                {
                    continue;
                }

                if (packet.Operator == BmapFrame.OperatorStatus && packet.Payload.Length >= 1)
                {
                    answer.TrySetResult(packet.Payload[0]);
                }
                else if (packet.Operator == BmapFrame.OperatorError)
                {
                    answer.TrySetResult(null);
                }
            }
        }

        void OnClosed(object? sender, EventArgs e) => answer.TrySetResult(null);

        channel.Received += OnChunk;
        channel.Closed += OnClosed;
        try
        {
            if (channel.IsClosed)
            {
                return null;
            }

            await channel.WriteAsync(BmapFrame.Get(BmapFrame.AudioBlock, BmapFrame.CurrentModeFunction), cancellationToken);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var deadline = Task.Delay(timeout, time, timeoutCts.Token);
            if (await Task.WhenAny(answer.Task, deadline) != answer.Task)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return null;
            }

            await timeoutCts.CancelAsync();
            return await answer.Task is { } index && ModeOf(index) is { } mode ? new BoseNoiseControlFeature(channel, mode) : null;
        }
        finally
        {
            channel.Received -= OnChunk;
            channel.Closed -= OnClosed;
        }
    }

    public Task<CommandResult> SetModeAsync(NoiseMode mode, CancellationToken cancellationToken = default) =>
        Task.FromResult(Options.ValidateMode(mode) ?? CommandResult.Unsupported("Changing noise control on these headphones isn't supported yet."));

    public Task<CommandResult> SetAncLevelAsync(int level, CancellationToken cancellationToken = default) =>
        Task.FromResult(Options.ValidateAncLevel(level) ?? CommandResult.Unsupported());

    public Task<CommandResult> SetAmbientLevelAsync(int level, CancellationToken cancellationToken = default) =>
        Task.FromResult(Options.ValidateAmbientLevel(level) ?? CommandResult.Unsupported());

    public Task<CommandResult> SetWindReductionAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Task.FromResult(CommandResult.Unsupported("Wind reduction isn't available on this device."));

    public Task<CommandResult> SetConversationAwarenessAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Task.FromResult(CommandResult.Unsupported("Conversation awareness isn't available on this device."));

    public async ValueTask DisposeAsync()
    {
        _channel.Received -= OnReceived;
        await _channel.DisposeAsync();
    }

    private static NoiseMode? ModeOf(byte index) => index switch
    {
        0 => NoiseMode.NoiseCancelling,
        1 => NoiseMode.Transparency,
        _ => null,
    };

    private void OnReceived(object? sender, ReadOnlyMemory<byte> chunk)
    {
        NoiseControlState? changed = null;
        lock (_gate)
        {
            foreach (var packet in _reader.Feed(chunk.Span))
            {
                if (packet is { Block: BmapFrame.AudioBlock, Function: BmapFrame.CurrentModeFunction, Operator: BmapFrame.OperatorStatus, Payload.Length: >= 1 }
                    && ModeOf(packet.Payload[0]) is { } mode)
                {
                    _current = changed = _current with { Mode = mode };
                }
            }
        }

        if (changed is not null)
        {
            Changed?.Invoke(this, changed);
        }
    }
}

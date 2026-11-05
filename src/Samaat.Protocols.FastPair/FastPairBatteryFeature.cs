using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Devices;
using Samaat.Transport;

namespace Samaat.Protocols.FastPair;

/// <summary>
/// Left, right and case battery plus firmware and model from the Fast Pair Message Stream the buds push on
/// connect. It writes only the two SASS get requests (<see cref="FastPairMessages.SassGetCapability"/>, <see cref="FastPairMessages.SassGetConnectionStatus"/>), via <see cref="SendAsync"/>. It owns the channel; disposing it closes it.
/// </summary>
public sealed class FastPairBatteryFeature : IBatteryFeature, IDeviceInfoFeature, IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<IRfcommChannel?>> _reopen;
    private readonly TimeProvider _time;
    private readonly TimeSpan _refreshTimeout;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly TaskCompletionSource _firstReading = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private BatteryState _current = BatteryState.Empty;
    private string? _firmware;
    private string? _modelId;
    private Link? _link;
    private TaskCompletionSource? _nextReading;
    private bool _disposed;
    private EventHandler? _infoChanged;

    private FastPairBatteryFeature(Func<CancellationToken, Task<IRfcommChannel?>> reopen, TimeProvider time, TimeSpan refreshTimeout)
    {
        _reopen = reopen;
        _time = time;
        _refreshTimeout = refreshTimeout;
    }

    public IReadOnlySet<BatteryComponent> Components { get; } =
        new HashSet<BatteryComponent> { BatteryComponent.Left, BatteryComponent.Right, BatteryComponent.Case };

    public bool ComponentsAreExhaustive => false;

    public CapabilitySource Source => CapabilitySource.RuntimeQuery;

    public Verification Verification => Verification.Unverified;

    public BatteryState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public string? FirmwareVersion
    {
        get
        {
            lock (_gate)
            {
                return _firmware;
            }
        }
    }

    /// <summary>The 3-byte Fast Pair model ID as hex, once reported.</summary>
    public string? ModelId
    {
        get
        {
            lock (_gate)
            {
                return _modelId;
            }
        }
    }

    public event EventHandler<BatteryState>? Changed;

    /// <summary>Every parsed message, raised outside the lock. Handlers must not throw.</summary>
    internal event Action<StreamMessage>? MessageReceived;

    /// <summary>Raised after <see cref="RefreshAsync"/> starts listening on a reopened channel.</summary>
    internal event Action? ChannelAttached;

    // Both interfaces name their event Changed; this one is the firmware/model event.
    event EventHandler? IDeviceInfoFeature.Changed
    {
        add => _infoChanged += value;
        remove => _infoChanged -= value;
    }

    /// <summary>
    /// Listens on an already-open channel. <paramref name="reopen"/> opens a fresh one for
    /// <see cref="RefreshAsync"/>; the caller awaits <see cref="FirstReadingAsync"/>.
    /// </summary>
    public static FastPairBatteryFeature Attach(
        IRfcommChannel channel,
        Func<CancellationToken, Task<IRfcommChannel?>> reopen,
        TimeProvider time,
        TimeSpan refreshTimeout)
    {
        var feature = new FastPairBatteryFeature(reopen, time, refreshTimeout);
        feature.Listen(channel);
        return feature;
    }

    /// <summary>Completes with the first valid battery message; throws if the channel closes first or <paramref name="cancellationToken"/> expires.</summary>
    public Task FirstReadingAsync(CancellationToken cancellationToken) => _firstReading.Task.WaitAsync(cancellationToken);

    /// <summary>
    /// Reopens the channel: the buds push their device information again on every connect. Caller
    /// cancellation propagates; transport failures and a silent device keep the last values.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        var gotReading = false;
        try
        {
            if (_disposed)
            {
                return;
            }

            await DetachAsync();
            var next = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _nextReading = next;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_refreshTimeout);
            if (await _reopen(timeout.Token) is { } channel)
            {
                Listen(channel);
                ChannelAttached?.Invoke();
                await next.Task.WaitAsync(timeout.Token);
                gotReading = true;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Keep the last values; the next refresh tries again.
        }
        finally
        {
            if (!gotReading)
            {
                await DetachAsync(); // a replacement that never delivered must not keep its socket open
            }

            _nextReading = null;
            _refreshLock.Release();
        }
    }

    /// <summary>Writes to the current channel; only the SASS get requests may be passed. Throws IOException with no channel.</summary>
    internal async Task SendAsync(byte[] message, CancellationToken cancellationToken)
    {
        Link? link;
        lock (_gate)
        {
            link = _link;
        }

        if (link is null)
        {
            throw new IOException("The Message Stream isn't open.");
        }

        await link.Channel.WriteAsync(message, cancellationToken);
    }

    private void Listen(IRfcommChannel channel)
    {
        var parser = new MessageStreamParser();
        EventHandler<ReadOnlyMemory<byte>> received = (_, chunk) => OnChunk(parser, chunk.Span);
        EventHandler closed = (_, _) => OnClosed(channel);
        var link = new Link(channel, received, closed);
        lock (_gate)
        {
            if (_disposed)
            {
                link = null;
            }
            else
            {
                _link = link;
            }
        }

        if (link is null)
        {
            _ = channel.DisposeAsync().AsTask();
            return;
        }

        channel.Closed += closed;
        channel.Received += received; // last: subscribing replays what the buds already pushed

        // Closed isn't replayed, so a channel that died before we subscribed is caught here.
        if (channel.IsClosed)
        {
            OnClosed(channel);
        }
    }

    private void OnChunk(MessageStreamParser parser, ReadOnlySpan<byte> chunk)
    {
        BatteryState? battery = null;
        var infoChanged = false;
        List<StreamMessage> received = [];
        lock (_gate)
        {
            foreach (var message in parser.Push(chunk))
            {
                received.Add(message);
                if (FastPairMessages.TryBattery(message, out var level))
                {
                    battery = Apply(level) ?? battery;
                }
                else if (FastPairMessages.TryFirmware(message, out var firmware))
                {
                    infoChanged |= firmware != _firmware;
                    _firmware = firmware;
                }
                else if (FastPairMessages.TryModelId(message, out var modelId))
                {
                    _modelId = modelId;
                }
            }
        }

        if (battery is not null)
        {
            _firstReading.TrySetResult();
            _nextReading?.TrySetResult();
            Changed?.Invoke(this, battery);
        }

        if (infoChanged)
        {
            _infoChanged?.Invoke(this, EventArgs.Empty);
        }

        foreach (var message in received)
        {
            MessageReceived?.Invoke(message);
        }
    }

    // Caller holds _gate. A message with nothing known keeps the last values and isn't a reading.
    private BatteryState? Apply(FastPairBattery level)
    {
        var now = _time.GetUtcNow();
        var readings = new[] { (BatteryComponent.Left, level.Left), (BatteryComponent.Right, level.Right), (BatteryComponent.Case, level.Case) }
            .Where(x => x.Item2 is not null)
            .Select(x => new BatteryReading(
                x.Item1,
                x.Item2!.Value.Percent,
                now,
                x.Item2.Value.Charging ? ChargingState.Charging : ChargingState.NotCharging))
            .ToList();
        if (readings.Count == 0)
        {
            return null;
        }

        _current = _current.With(readings);
        return _current;
    }

    // Before the first reading a closed channel is a failed open; afterwards the values just age into "stale".
    private void OnClosed(IRfcommChannel channel)
    {
        lock (_gate)
        {
            if (_link?.Channel != channel)
            {
                return;
            }
        }

        var closed = new IOException("The Message Stream closed.");
        _firstReading.TrySetException(closed);
        _nextReading?.TrySetException(closed);
    }

    private async Task DetachAsync()
    {
        Link? link;
        lock (_gate)
        {
            link = _link;
            _link = null;
        }

        if (link is null)
        {
            return;
        }

        link.Channel.Received -= link.Received;
        link.Channel.Closed -= link.Closed;
        try
        {
            await link.Channel.DisposeAsync();
        }
        catch (Exception)
        {
            // Already gone.
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        await DetachAsync();
    }

    private sealed record Link(IRfcommChannel Channel, EventHandler<ReadOnlyMemory<byte>> Received, EventHandler Closed);
}

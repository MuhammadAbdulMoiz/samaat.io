using System.Threading.Channels;
using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Transport;

namespace Samaat.Protocols.Jieli;

/// <summary>
/// Left, right and case battery from Jieli's AdvInfo push (<c>0xC2</c>) over the RCSP BLE service.
/// A session is: subscribe AE02, authenticate, then listen. The only writes are the auth steps.
/// </summary>
public sealed class JieliBatteryFeature : IBatteryFeature, IAsyncDisposable
{
    // Device-initiated frames carry flags 0x80; 0x40 marks a command that expects our response, which we never send.
    private const byte ExpectsResponse = 0x40;

    private readonly IGattCharacteristic _write;
    private readonly IGattCharacteristic _notify;
    private readonly TimeProvider _time;
    private readonly TimeSpan _authStepTimeout;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _session = new(1, 1);
    private readonly TaskCompletionSource _firstReading = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IAsyncDisposable? _subscription;
    private Channel<byte[]>? _authInbox;
    private RcspFrameParser _parser = new();
    private BatteryState _current = BatteryState.Empty;
    private bool _disposed;

    private JieliBatteryFeature(IGattCharacteristic write, IGattCharacteristic notify, TimeProvider time, TimeSpan authStepTimeout)
    {
        _write = write;
        _notify = notify;
        _time = time;
        _authStepTimeout = authStepTimeout;
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

    public event EventHandler<BatteryState>? Changed;

    /// <summary>
    /// Runs a session and waits for the first valid battery push. Returns null (with everything released)
    /// on any failure or when <paramref name="cancellationToken"/> expires first; never throws.
    /// </summary>
    public static async Task<JieliBatteryFeature?> CreateAsync(
        IGattCharacteristic write,
        IGattCharacteristic notify,
        TimeProvider time,
        TimeSpan authStepTimeout,
        CancellationToken cancellationToken)
    {
        var feature = new JieliBatteryFeature(write, notify, time, authStepTimeout);
        try
        {
            if (await feature.RunSessionAsync(cancellationToken))
            {
                await feature._firstReading.Task.WaitAsync(cancellationToken);
                return feature;
            }
        }
        catch (Exception)
        {
            // Any failure means "no Jieli battery"; the caller falls back to the Windows level.
        }

        await feature.DisposeAsync();
        return null;
    }

    /// <summary>Re-runs the session; the buds push their battery again after authentication.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await RunSessionAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep the last values; the next refresh tries again.
        }
    }

    private async Task<bool> RunSessionAsync(CancellationToken cancellationToken)
    {
        await _session.WaitAsync(cancellationToken);
        try
        {
            if (_disposed)
            {
                return false;
            }

            await ReleaseSubscriptionAsync();
            var inbox = Channel.CreateUnbounded<byte[]>();
            lock (_gate)
            {
                _authInbox = inbox;
                _parser = new RcspFrameParser();
            }

            _subscription = await _notify.SubscribeAsync(OnChunk, cancellationToken);
            var authenticated = await RcspAuth.RunAsync(_write, inbox.Reader, _authStepTimeout, cancellationToken);

            var early = new List<RcspPacket>();
            lock (_gate)
            {
                _authInbox = null;
                while (authenticated && inbox.Reader.TryRead(out var chunk))
                {
                    early.AddRange(_parser.Feed(chunk));
                }
            }

            Handle(early);
            return authenticated;
        }
        finally
        {
            _session.Release();
        }
    }

    private void OnChunk(byte[] chunk)
    {
        IReadOnlyList<RcspPacket> packets;
        lock (_gate)
        {
            if (_authInbox is { } inbox)
            {
                inbox.Writer.TryWrite(chunk);
                return;
            }

            packets = _parser.Feed(chunk);
        }

        Handle(packets);
    }

    private void Handle(IReadOnlyList<RcspPacket> packets)
    {
        foreach (var packet in packets)
        {
            if (packet.Op == AdvInfo.Op && (packet.Flags & ExpectsResponse) == 0 && AdvInfo.TryParse(packet.Params, out var info))
            {
                Apply(info);
            }
        }
    }

    private void Apply(AdvInfo info)
    {
        var now = _time.GetUtcNow();
        var readings = new[]
            {
                (BatteryComponent.Left, info.Left),
                (BatteryComponent.Right, info.Right),
                (BatteryComponent.Case, info.Case),
            }
            .Where(x => x.Item2 is not null)
            .Select(x => new BatteryReading(
                x.Item1,
                x.Item2!.Value.Percent,
                now,
                x.Item2.Value.Charging ? ChargingState.Charging : ChargingState.NotCharging))
            .ToList();

        // A push with nothing reported (e.g. buds in a closed case) keeps the last values.
        if (readings.Count == 0)
        {
            return;
        }

        BatteryState updated;
        lock (_gate)
        {
            _current = _current.With(readings);
            updated = _current;
        }

        _firstReading.TrySetResult();
        Changed?.Invoke(this, updated);
    }

    private async Task ReleaseSubscriptionAsync()
    {
        if (Interlocked.Exchange(ref _subscription, null) is { } subscription)
        {
            await subscription.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await ReleaseSubscriptionAsync();
    }
}

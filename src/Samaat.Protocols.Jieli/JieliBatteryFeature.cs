using Samaat.Core.Battery;
using Samaat.Core.Capabilities;

namespace Samaat.Protocols.Jieli;

/// <summary>
/// Left, right and case battery from Jieli's AdvInfo push (<c>0xC2</c>) over the RCSP session. It owns
/// the session: disposing it disposes the session the noise and EQ features share.
/// </summary>
public sealed class JieliBatteryFeature : IBatteryFeature, IAsyncDisposable
{
    // Device-initiated frames carry flags 0x80; 0x40 marks a command that expects our response, which we never send.
    private const byte ExpectsResponse = 0x40;

    private readonly RcspSession _session;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly TaskCompletionSource _firstReading = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private BatteryState _current = BatteryState.Empty;

    private JieliBatteryFeature(RcspSession session, TimeProvider time)
    {
        _session = session;
        _time = time;
        _session.Packet += OnPacket;
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
    /// Starts <paramref name="session"/> and waits for the first valid battery push. Returns null (with the
    /// session disposed) on any failure or when <paramref name="cancellationToken"/> expires first; never throws.
    /// </summary>
    public static async Task<JieliBatteryFeature?> CreateAsync(RcspSession session, TimeProvider time, CancellationToken cancellationToken)
    {
        var feature = Attach(session, time);
        try
        {
            if (await session.StartAsync(cancellationToken))
            {
                await feature.FirstReadingAsync(cancellationToken);
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

    /// <summary>
    /// Listens on a session that isn't started yet, so the push the buds send right after auth isn't missed.
    /// The caller starts the session and awaits <see cref="FirstReadingAsync"/>.
    /// </summary>
    public static JieliBatteryFeature Attach(RcspSession session, TimeProvider time) => new(session, time);

    /// <summary>Completes with the first valid battery push; throws if <paramref name="cancellationToken"/> expires first.</summary>
    public Task FirstReadingAsync(CancellationToken cancellationToken) => _firstReading.Task.WaitAsync(cancellationToken);

    /// <summary>Re-runs the session; the buds push their battery again after authentication.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _session.RestartAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep the last values; the next refresh tries again.
        }
    }

    private void OnPacket(RcspPacket packet)
    {
        if (packet.Op == AdvInfo.Op && (packet.Flags & ExpectsResponse) == 0 && AdvInfo.TryParse(packet.Params, out var info))
        {
            Apply(info);
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

    public async ValueTask DisposeAsync()
    {
        _session.Packet -= OnPacket;
        await _session.DisposeAsync();
    }
}

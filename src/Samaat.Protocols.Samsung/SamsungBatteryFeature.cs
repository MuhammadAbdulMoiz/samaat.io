// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/timschneeb/GalaxyBudsClient @ 754b6fdfb1c151b022cece775c8aaad4eb185afa, GalaxyBudsClient/Message/Decoder/ExtendedStatusUpdateDecoder.cs and GalaxyBudsClient/Message/Decoder/StatusUpdateDecoder.cs, Copyright (c) 2019-2026 Tim Schneeberger and contributors, GPL-3.0.
// Changes: rewritten onto Samaat.Transport (IRfcommChannel) and Samaat.Core features.

using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Devices;

namespace Samaat.Protocols.Samsung;

/// <summary>
/// Left, right and (except on the 2019 Galaxy Buds) case battery from the status the buds push. Charging isn't
/// reported: its layout differs per model and revision. It owns the session: disposing it closes the channel
/// (idempotent); the noise and equalizer features only detach.
/// </summary>
public sealed class SamsungBatteryFeature : IBatteryFeature, IAsyncDisposable
{
    private readonly SamsungSession _session;
    private readonly TimeProvider _time;
    private readonly SamsungModel _model;
    private readonly Lock _gate = new();
    private BatteryState _current;

    internal SamsungBatteryFeature(SamsungSession session, SamsungModel model, SamsungStatus first, TimeProvider time)
    {
        _session = session;
        _time = time;
        _current = BatteryState.Empty.With(Readings(first));
        Components = model.Legacy
            ? new HashSet<BatteryComponent> { BatteryComponent.Left, BatteryComponent.Right }
            : new HashSet<BatteryComponent> { BatteryComponent.Left, BatteryComponent.Right, BatteryComponent.Case };
        _model = model;
        _session.Message += OnMessage;
    }

    public IReadOnlySet<BatteryComponent> Components { get; }

    public bool ComponentsAreExhaustive => true;

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

    /// <summary>The buds push their state, so there is nothing to ask for.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _session.Message -= OnMessage;
        await _session.DisposeAsync();
    }

    private void OnMessage(SppMessage message)
    {
        if (SamsungStatus.TryParse(message, _model) is not { } status)
        {
            return;
        }

        BatteryState state;
        lock (_gate)
        {
            state = _current = _current.With(Readings(status));
        }

        Changed?.Invoke(this, state);
    }

    private IEnumerable<BatteryReading> Readings(SamsungStatus status)
    {
        var now = _time.GetUtcNow();
        yield return Bud(BatteryComponent.Left, status.LeftPercent, status.PlacementLeft, now);
        yield return Bud(BatteryComponent.Right, status.RightPercent, status.PlacementRight, now);
        if (status.CaseRaw is not null)
        {
            yield return new BatteryReading(BatteryComponent.Case, status.CasePercent, now);
        }
    }

    private static BatteryReading Bud(BatteryComponent component, int? percent, SamsungPlacement placement, DateTimeOffset now) => placement switch
    {
        SamsungPlacement.Disconnected => new(component, null, now, ChargingState.Unknown, BudStatus.Disconnected),
        SamsungPlacement.Wearing or SamsungPlacement.Idle => new(component, percent, now, ChargingState.Unknown, BudStatus.Connected),
        SamsungPlacement.InCase => new(component, percent, now, ChargingState.Unknown, BudStatus.InCase),
        _ => new(component, percent, now),
    };
}

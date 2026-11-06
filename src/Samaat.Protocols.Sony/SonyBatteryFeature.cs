// SPDX-License-Identifier: GPL-3.0-or-later
// Written for Samaat from docs/protocols/sony.md (clean-room: behaviour learned from Gadgetbridge, no code copied).

using Samaat.Core.Battery;
using Samaat.Core.Capabilities;

namespace Samaat.Protocols.Sony;

/// <summary>
/// One combined level (headset), or left, right and case (buds), from the query replies and the battery pushes. It
/// owns the session: disposing it closes the channel (idempotent); the noise feature only detaches.
/// </summary>
public sealed class SonyBatteryFeature : IBatteryFeature, IAsyncDisposable
{
    private readonly SonySession _session;
    private readonly Lock _gate = new();
    private BatteryState _current;

    internal SonyBatteryFeature(SonySession session, IReadOnlyList<SonyBatteryReport> first)
    {
        _session = session;
        _current = BatteryState.Empty.With(first.SelectMany(Readings));
        Components = first.SelectMany(r => Readings(r).Select(x => x.Component)).ToHashSet();
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

    /// <summary>The headset pushes changes, so there is nothing to ask for.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _session.Message -= OnMessage;
        await _session.DisposeAsync();
    }

    private void OnMessage(byte[] payload)
    {
        if (SonyBatteryReport.TryParse(payload) is not { } report)
        {
            return;
        }

        var readings = Readings(report).Where(r => Components.Contains(r.Component)).ToList();
        if (readings.Count == 0)
        {
            return;
        }

        BatteryState updated;
        lock (_gate)
        {
            updated = _current = _current.With(readings);
        }

        Changed?.Invoke(this, updated);
    }

    private IEnumerable<BatteryReading> Readings(SonyBatteryReport report)
    {
        var now = _session.Time.GetUtcNow();
        switch (report.Type)
        {
            case 0:
                yield return Whole(BatteryComponent.Combined, report.Level, report.Charging, now);
                break;
            case 2:
                yield return Whole(BatteryComponent.Case, report.Level, report.Charging, now);
                break;
            default:
                yield return Bud(BatteryComponent.Left, report.Level, report.Charging, now);
                yield return Bud(BatteryComponent.Right, report.RightLevel ?? 0, report.RightCharging, now);
                break;
        }
    }

    // A level above 100 isn't a percent.
    private static BatteryReading Whole(BatteryComponent component, int level, ChargingState charging, DateTimeOffset now) =>
        new(component, level <= 100 ? level : null, now, charging);

    // A bud at level 0 isn't connected, so it has no reading.
    private static BatteryReading Bud(BatteryComponent component, int level, ChargingState charging, DateTimeOffset now) =>
        level == 0 ? new(component, null, now, ChargingState.Unknown, BudStatus.Disconnected) : Whole(component, level, charging, now);
}

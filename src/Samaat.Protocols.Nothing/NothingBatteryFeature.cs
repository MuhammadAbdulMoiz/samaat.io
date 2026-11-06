// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/maniacx/BudsLink @ 0eb4d07dcd7eafabf425d98c607e02fc9519c94b, src/lib/devices/nothingBuds/{nothingBudsSocket.js,nothingBudsConfig.js,deviceConfigs/CMFBuds.js}, GPL-3.0 (BudsLink credits radiance-project/ear-web).
// Changes: rewritten in C# onto Samaat.Transport and Samaat.Core; ANC read offset and noise-cancelling byte corrected from Samaat's own CMF Buds captures (docs/hardware-tests/2026-11-06-cmf-buds.md).

using Samaat.Core.Battery;
using Samaat.Core.Capabilities;

namespace Samaat.Protocols.Nothing;

/// <summary>
/// Left, right and case levels with charging, from the 4007 reply and the E001 push (<c>count, [id, level]...</c>;
/// id 02 left, 03 right, 04 case, 06 a single bud; level &amp; 0x7F is percent, 0x80 is charging).
/// </summary>
public sealed class NothingBatteryFeature : IBatteryFeature, IAsyncDisposable
{
    private readonly NothingSession _session;
    private readonly bool _ownsSession;
    private readonly Lock _gate = new();
    private BatteryState _current = BatteryState.Empty;

    internal NothingBatteryFeature(NothingSession session, NothingMessage first, bool ownsSession)
    {
        _session = session;
        _ownsSession = ownsSession;
        _current = _current.With(Readings(first.Payload));
        _session.Message += OnMessage;
    }

    public IReadOnlySet<BatteryComponent> Components { get; } =
        new HashSet<BatteryComponent> { BatteryComponent.Left, BatteryComponent.Right, BatteryComponent.Case };

    public bool ComponentsAreExhaustive => true;

    public CapabilitySource Source => CapabilitySource.RuntimeQuery;

    public Verification Verification => Verification.Verified;

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

    /// <summary>Asks the buds for the levels; the reply updates <see cref="Current"/> through the same path as a push.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default) =>
        await _session.RequestAsync(NothingCommands.Battery, [], cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _session.Message -= OnMessage;
        if (_ownsSession)
        {
            await _session.DisposeAsync();
        }
    }

    private void OnMessage(NothingMessage message)
    {
        if (message.Command is not (NothingCommands.BatteryReply or NothingCommands.BatteryPush))
        {
            return;
        }

        var readings = Readings(message.Payload).ToList();
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

    private IEnumerable<BatteryReading> Readings(byte[] payload)
    {
        var now = _session.Time.GetUtcNow();
        var count = payload.Length > 0 ? payload[0] : 0;
        for (var i = 0; i < count && 2 + (i * 2) < payload.Length; i++)
        {
            var component = payload[1 + (i * 2)] switch
            {
                0x02 => BatteryComponent.Left,
                0x03 => BatteryComponent.Right,
                0x04 => BatteryComponent.Case,
                0x06 => BatteryComponent.Combined,
                _ => (BatteryComponent?)null,
            };
            if (component is { } c)
            {
                var level = payload[2 + (i * 2)];
                yield return new BatteryReading(c, Math.Min(level & 0x7F, 100), now, (level & 0x80) != 0 ? ChargingState.Charging : ChargingState.NotCharging);
            }
        }
    }
}

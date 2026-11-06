// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/Oppzippy/OpenSCQ30 @ e75a68b70de8eaf2cf0ca364e132085a097557c5, lib/src/devices/soundcore/common/packet/inbound/{battery_level,battery_charging}.rs and lib/src/devices/soundcore/common/modules/battery.rs, Copyright (c) 2023-2026 Kyle Scheuing and contributors, GPL-3.0.
// Changes: rewritten in C# onto Samaat.Transport (IRfcommChannel) and Samaat.Core features.

using Samaat.Core.Battery;
using Samaat.Core.Capabilities;

namespace Samaat.Protocols.Soundcore;

/// <summary>
/// One combined level (or left and right on earbuds) from the state and the battery pushes. It owns the session:
/// disposing it closes the channel (idempotent); the noise and equalizer features only detach.
/// </summary>
public sealed class SoundcoreBatteryFeature : IBatteryFeature, IAsyncDisposable
{
    private readonly SoundcoreSession _session;
    private readonly SoundcoreModel _model;
    private readonly Lock _gate = new();
    private BatteryState _current;

    internal SoundcoreBatteryFeature(SoundcoreSession session, SoundcoreModel model, SoundcoreState first)
    {
        _session = session;
        _model = model;
        _current = BatteryState.Empty.With(Readings(first));
        Components = model.Battery == SoundcoreBattery.Dual
            ? new HashSet<BatteryComponent> { BatteryComponent.Left, BatteryComponent.Right }
            : new HashSet<BatteryComponent> { BatteryComponent.Combined };
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

    /// <summary>The device pushes its state, so there is nothing to ask for.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _session.Message -= OnMessage;
        await _session.DisposeAsync();
    }

    // shortcut: the device reports 0-5 steps, so percent is coarse; upgrade if a model reports finer levels
    private static int Percent(int level) => Math.Min(level, 5) * 20;

    private void OnMessage(SoundcoreMessage message)
    {
        var readings = SoundcoreState.TryParse(_model, message) is { } state ? Readings(state).ToList() : PushReadings(message);
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

    private IEnumerable<BatteryReading> Readings(SoundcoreState state)
    {
        var now = _session.Time.GetUtcNow();
        if (_model.Battery == SoundcoreBattery.Dual)
        {
            yield return new BatteryReading(BatteryComponent.Left, Percent(state.Level), now, state.Charging);
            yield return new BatteryReading(BatteryComponent.Right, Percent(state.RightLevel ?? 0), now, state.RightCharging);
        }
        else
        {
            yield return new BatteryReading(BatteryComponent.Combined, Percent(state.Level), now, state.Charging);
        }
    }

    /// <summary>The level (01 03) and charging (01 04) pushes. Each keeps the other half of the reading as it was.</summary>
    private List<BatteryReading> PushReadings(SoundcoreMessage message)
    {
        var dual = _model.Battery == SoundcoreBattery.Dual;
        var isLevel = message is { C0: SoundcoreCommands.StatusGroup, C1: SoundcoreCommands.BatteryLevel };
        var isCharging = message is { C0: SoundcoreCommands.StatusGroup, C1: SoundcoreCommands.BatteryCharging };
        var needed = dual ? 2 : 1;
        if ((!isLevel && !isCharging) || message.Body.Length < needed || (isCharging && _model.Battery == SoundcoreBattery.LevelOnly))
        {
            return [];
        }

        var now = _session.Time.GetUtcNow();
        var components = dual ? new[] { BatteryComponent.Left, BatteryComponent.Right } : [BatteryComponent.Combined];
        var readings = new List<BatteryReading>();
        lock (_gate)
        {
            for (var i = 0; i < components.Length; i++)
            {
                var old = _current.Get(components[i]);
                readings.Add(isLevel
                    ? new BatteryReading(components[i], Percent(message.Body[i]), now, old?.Charging ?? ChargingState.Unknown)
                    : new BatteryReading(components[i], old?.Percent, now, SoundcoreState.ChargingOf(message.Body[i])));
            }
        }

        return readings;
    }
}

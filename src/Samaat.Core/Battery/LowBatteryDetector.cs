namespace Samaat.Core.Battery;

public enum BatteryAlertLevel
{
    Low,
    Critical,
}

public sealed record BatteryAlert(
    string DeviceId,
    BatteryComponent Component,
    int Percent,
    BatteryAlertLevel Level);

public sealed record BatteryAlertPolicy
{
    public int LowThreshold { get; init; } = 20;

    public int CriticalThreshold { get; init; } = 10;

    /// <summary>How far above a threshold a level must rise before that alert can fire again.</summary>
    public int RearmMargin { get; init; } = 5;

    /// <summary>A low case is less urgent than low buds, so it is opt-in.</summary>
    public bool IncludeCase { get; init; }
}

/// <summary>
/// Turns a stream of battery snapshots into low/critical alerts, at most once per threshold
/// crossing. Not thread-safe: call from one thread (the UI dispatcher in the app).
/// </summary>
public sealed class LowBatteryDetector(BatteryAlertPolicy? policy = null)
{
    private readonly Dictionary<(string DeviceId, BatteryComponent Component), BatteryAlertLevel> _alerted = [];

    /// <summary>Replaceable at runtime (settings changes); alert state already recorded is kept.</summary>
    public BatteryAlertPolicy Policy { get; set; } = policy ?? new BatteryAlertPolicy();

    public IReadOnlyList<BatteryAlert> Evaluate(string deviceId, BatteryState state)
    {
        var alerts = new List<BatteryAlert>();
        foreach (var reading in state.Readings)
        {
            if (reading.Percent is not { } percent || (reading.Component == BatteryComponent.Case && !Policy.IncludeCase))
            {
                continue;
            }

            var key = (deviceId, reading.Component);
            var previous = _alerted.TryGetValue(key, out var level) ? level : (BatteryAlertLevel?)null;

            if (reading.Charging is ChargingState.Charging or ChargingState.Full
                || percent >= Policy.LowThreshold + Policy.RearmMargin)
            {
                _alerted.Remove(key);
            }
            else if (percent <= Policy.CriticalThreshold)
            {
                if (previous != BatteryAlertLevel.Critical)
                {
                    _alerted[key] = BatteryAlertLevel.Critical;
                    alerts.Add(new BatteryAlert(deviceId, reading.Component, percent, BatteryAlertLevel.Critical));
                }
            }
            else if (percent <= Policy.LowThreshold)
            {
                if (previous is null)
                {
                    _alerted[key] = BatteryAlertLevel.Low;
                    alerts.Add(new BatteryAlert(deviceId, reading.Component, percent, BatteryAlertLevel.Low));
                }
                else if (previous == BatteryAlertLevel.Critical && percent >= Policy.CriticalThreshold + Policy.RearmMargin)
                {
                    // Recovered from critical without charging (e.g. a noisy reading): allow critical to fire again.
                    _alerted[key] = BatteryAlertLevel.Low;
                }
            }
        }

        return alerts;
    }

    public void Forget(string deviceId)
    {
        foreach (var key in _alerted.Keys.Where(k => k.DeviceId == deviceId).ToList())
        {
            _alerted.Remove(key);
        }
    }
}

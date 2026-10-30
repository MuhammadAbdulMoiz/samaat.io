namespace Samaat.Core.Battery;

public enum BatteryComponent
{
    /// <summary>A single level for the whole device, used when buds aren't reported separately.</summary>
    Combined,
    Left,
    Right,
    Case,
}

public enum ChargingState
{
    Unknown,
    NotCharging,
    Charging,
    Full,
}

/// <summary>Where an individual bud is, when the device reports it.</summary>
public enum BudStatus
{
    Unknown,
    Connected,
    InCase,
    Disconnected,
}

/// <summary>
/// One battery value. <see cref="Percent"/> is null when the component exists but its level is
/// not currently known, e.g. a closed case or a disconnected bud.
/// </summary>
public sealed record BatteryReading
{
    public BatteryReading(
        BatteryComponent component,
        int? percent,
        DateTimeOffset updatedAt,
        ChargingState charging = ChargingState.Unknown,
        BudStatus status = BudStatus.Unknown)
    {
        if (percent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), percent, "Battery percent must be between 0 and 100.");
        }

        Component = component;
        Percent = percent;
        UpdatedAt = updatedAt;
        Charging = charging;
        Status = status;
    }

    public BatteryComponent Component { get; }

    public int? Percent { get; }

    public DateTimeOffset UpdatedAt { get; }

    public ChargingState Charging { get; }

    public BudStatus Status { get; }

    public bool IsCharging => Charging is ChargingState.Charging;

    public bool IsStale(DateTimeOffset now, TimeSpan maxAge) => now - UpdatedAt > maxAge;
}

/// <summary>Immutable snapshot of every battery component a device reports.</summary>
public sealed class BatteryState
{
    private readonly Dictionary<BatteryComponent, BatteryReading> _readings;

    private BatteryState(Dictionary<BatteryComponent, BatteryReading> readings)
    {
        _readings = readings;
    }

    public static BatteryState Empty { get; } = new([]);

    public IReadOnlyCollection<BatteryReading> Readings => _readings.Values;

    public BatteryReading? Get(BatteryComponent component) => _readings.GetValueOrDefault(component);

    public BatteryState With(BatteryReading reading) => new(new(_readings) { [reading.Component] = reading });

    public BatteryState With(IEnumerable<BatteryReading> readings)
    {
        var copy = new Dictionary<BatteryComponent, BatteryReading>(_readings);
        foreach (var reading in readings)
        {
            copy[reading.Component] = reading;
        }

        return new(copy);
    }

    /// <summary>The level to show in compact places (tray, device list): lowest bud, else combined.</summary>
    public int? SummaryPercent
    {
        get
        {
            var buds = new[] { Get(BatteryComponent.Left)?.Percent, Get(BatteryComponent.Right)?.Percent }
                .Where(p => p is not null)
                .ToList();
            return buds.Count > 0 ? buds.Min() : Get(BatteryComponent.Combined)?.Percent;
        }
    }
}

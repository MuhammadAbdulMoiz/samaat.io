using Samaat.Core.Battery;

namespace Samaat.Core.Tests;

public class BatteryStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Reading_RejectsOutOfRangePercent(int percent) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BatteryReading(BatteryComponent.Left, percent, Now));

    [Fact]
    public void Reading_AllowsUnknownPercent()
    {
        var reading = new BatteryReading(BatteryComponent.Case, null, Now);

        Assert.Null(reading.Percent);
    }

    [Fact]
    public void With_ReplacesOnlyThatComponent()
    {
        var state = BatteryState.Empty
            .With(new BatteryReading(BatteryComponent.Left, 80, Now))
            .With(new BatteryReading(BatteryComponent.Right, 70, Now));

        var updated = state.With(new BatteryReading(BatteryComponent.Left, 79, Now));

        Assert.Equal(79, updated.Get(BatteryComponent.Left)!.Percent);
        Assert.Equal(70, updated.Get(BatteryComponent.Right)!.Percent);
        Assert.Equal(80, state.Get(BatteryComponent.Left)!.Percent);
    }

    [Fact]
    public void SummaryPercent_IsLowestBud()
    {
        var state = BatteryState.Empty.With([
            new BatteryReading(BatteryComponent.Left, 80, Now),
            new BatteryReading(BatteryComponent.Right, 35, Now),
            new BatteryReading(BatteryComponent.Case, 10, Now),
        ]);

        Assert.Equal(35, state.SummaryPercent);
    }

    [Fact]
    public void SummaryPercent_FallsBackToCombined()
    {
        var state = BatteryState.Empty.With(new BatteryReading(BatteryComponent.Combined, 60, Now));

        Assert.Equal(60, state.SummaryPercent);
    }

    [Fact]
    public void IsStale_ComparesAgainstMaxAge()
    {
        var reading = new BatteryReading(BatteryComponent.Left, 50, Now);

        Assert.False(reading.IsStale(Now.AddMinutes(4), TimeSpan.FromMinutes(5)));
        Assert.True(reading.IsStale(Now.AddMinutes(6), TimeSpan.FromMinutes(5)));
    }
}

public class LowBatteryDetectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static BatteryState State(int percent, BatteryComponent component = BatteryComponent.Left, ChargingState charging = ChargingState.NotCharging) =>
        BatteryState.Empty.With(new BatteryReading(component, percent, Now, charging));

    [Fact]
    public void FiresLowOnceWhenCrossingThreshold()
    {
        var detector = new LowBatteryDetector();

        Assert.Empty(detector.Evaluate("d", State(25)));
        var alert = Assert.Single(detector.Evaluate("d", State(20)));
        Assert.Equal(BatteryAlertLevel.Low, alert.Level);
        Assert.Empty(detector.Evaluate("d", State(19)));
        Assert.Empty(detector.Evaluate("d", State(15)));
    }

    [Fact]
    public void EscalatesToCriticalOnce()
    {
        var detector = new LowBatteryDetector();
        detector.Evaluate("d", State(18));

        var alert = Assert.Single(detector.Evaluate("d", State(10)));
        Assert.Equal(BatteryAlertLevel.Critical, alert.Level);
        Assert.Empty(detector.Evaluate("d", State(5)));
    }

    [Fact]
    public void JumpingStraightToCritical_FiresOnlyCritical()
    {
        var detector = new LowBatteryDetector();

        var alert = Assert.Single(detector.Evaluate("d", State(8)));
        Assert.Equal(BatteryAlertLevel.Critical, alert.Level);
    }

    [Fact]
    public void ChargingRearms()
    {
        var detector = new LowBatteryDetector();
        detector.Evaluate("d", State(15));

        Assert.Empty(detector.Evaluate("d", State(16, charging: ChargingState.Charging)));
        Assert.Single(detector.Evaluate("d", State(15)));
    }

    [Fact]
    public void SmallRecoveryDoesNotRearm_Hysteresis()
    {
        var detector = new LowBatteryDetector();
        detector.Evaluate("d", State(20));

        Assert.Empty(detector.Evaluate("d", State(22)));
        Assert.Empty(detector.Evaluate("d", State(20)));
        Assert.Empty(detector.Evaluate("d", State(25)));
        Assert.Single(detector.Evaluate("d", State(20)));
    }

    [Fact]
    public void CaseIgnoredUnlessOptedIn()
    {
        Assert.Empty(new LowBatteryDetector().Evaluate("d", State(5, BatteryComponent.Case)));
        Assert.Single(new LowBatteryDetector(new BatteryAlertPolicy { IncludeCase = true }).Evaluate("d", State(5, BatteryComponent.Case)));
    }

    [Fact]
    public void DevicesAndComponentsAreTrackedIndependently()
    {
        var detector = new LowBatteryDetector();
        var both = BatteryState.Empty.With([
            new BatteryReading(BatteryComponent.Left, 15, Now),
            new BatteryReading(BatteryComponent.Right, 15, Now),
        ]);

        Assert.Equal(2, detector.Evaluate("a", both).Count);
        Assert.Equal(2, detector.Evaluate("b", both).Count);
        Assert.Empty(detector.Evaluate("a", both));
    }
}

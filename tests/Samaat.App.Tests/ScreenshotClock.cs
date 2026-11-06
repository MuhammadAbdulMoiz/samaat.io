using Microsoft.Extensions.Time.Testing;
using Samaat.Core.Devices;
using Samaat.Simulator;

namespace Samaat.App.Tests;

/// <summary>
/// The clock the docs screenshots run on: still at one instant, so "Updated" ages, event times and the simulator's
/// battery levels (it drains on a timer) are the same on every run. Commands get no simulated latency, since their
/// delays would wait on this clock, which never moves.
/// </summary>
internal static class ScreenshotClock
{
    public static readonly DateTimeOffset Instant = new(2026, 11, 2, 9, 41, 0, TimeSpan.Zero);

    public static FakeTimeProvider Create() => new(Instant);

    public static IReadOnlyList<IDeviceSource> Sources(TimeProvider clock) =>
        [new SimulatorDeviceSource(TimeSpan.FromSeconds(2), clock, commandLatency: TimeSpan.Zero)];
}

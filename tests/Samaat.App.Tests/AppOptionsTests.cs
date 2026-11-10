using Samaat.Core.Diagnostics;
using Samaat.Simulator;

namespace Samaat.App.Tests;

/// <summary>The dev-only switch between the simulator's data sets: --simulate, --simulate=worst, --simulate=empty.</summary>
public sealed class AppOptionsTests
{
    [Theory]
    [InlineData(new string[0], false, SimulatorScenario.Demo)]
    [InlineData(new[] { "--simulate" }, true, SimulatorScenario.Demo)]
    [InlineData(new[] { "--simulate=worst" }, true, SimulatorScenario.WorstCase)]
    [InlineData(new[] { "--simulate=empty" }, true, SimulatorScenario.Empty)]
    [InlineData(new[] { "--simulate=nonsense" }, true, SimulatorScenario.Demo)]
    public void Parse_ReadsTheSimulatorScenario(string[] args, bool simulate, SimulatorScenario scenario)
    {
        var options = AppOptions.Parse(args);

        Assert.Equal(simulate, options.Simulate);
        Assert.Equal(scenario, options.Scenario);
    }

    private sealed class Capture : ILogSink
    {
        public List<string> Warnings { get; } = [];

        public void Write(LogLevel level, string source, string message, Exception? error)
        {
            if (level == LogLevel.Warning && message.StartsWith("Unknown option", StringComparison.Ordinal))
            {
                lock (Warnings)
                {
                    Warnings.Add(message);
                }
            }
        }
    }

    [Fact]
    public void Parse_WarnsAboutUnknownOptionsOnly()
    {
        var capture = new Capture();
        var previous = SamaatLog.Sink;
        SamaatLog.Sink = capture;
        try
        {
            AppOptions.Parse(["--simulate=worst", "--all-devices", "--minimized", "--test-notification", "--multi-instance", "--bogus"]);
        }
        finally
        {
            SamaatLog.Sink = previous;
        }

        Assert.Equal(["Unknown option --bogus ignored"], capture.Warnings);
    }
}

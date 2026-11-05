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
}

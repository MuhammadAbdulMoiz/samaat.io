namespace Samaat.Simulator;

/// <summary>Which data set the simulator serves. Dev-only: picked with <c>--simulate=worst</c> or <c>--simulate=empty</c>.</summary>
public enum SimulatorScenario
{
    /// <summary>Three tidy devices, one per capability shape. Used for docs screenshots and most tests.</summary>
    Demo,

    /// <summary>Realistic worst-case values, spread across devices the way real data is (see <see cref="WorstCaseDevices"/>).</summary>
    WorstCase,

    /// <summary>No devices at all, for the empty states.</summary>
    Empty,
}

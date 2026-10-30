using Samaat.Core.Devices;
using Samaat.Simulator;

namespace Samaat.App;

public sealed record AppOptions(bool Simulate, bool IncludeNonAudio)
{
    public static AppOptions Parse(string[] args) => new(
        Simulate: args.Contains("--simulate"),
        IncludeNonAudio: args.Contains("--all-devices"));

#if SAMAAT_WINDOWS
    public bool HasRealBluetooth => true;
#else
    public bool HasRealBluetooth => false;
#endif

    /// <summary>The simulator is used when asked for, or when this build has no real Bluetooth backend.</summary>
    public bool UsesSimulator => Simulate || !HasRealBluetooth;

    public IReadOnlyList<IDeviceSource> CreateSources(TimeProvider time)
    {
        var sources = new List<IDeviceSource>();
#if SAMAAT_WINDOWS
        sources.Add(new Samaat.Platform.Windows.WindowsDeviceSource(time, IncludeNonAudio));
#endif
        if (UsesSimulator)
        {
            sources.Add(new SimulatorDeviceSource(TimeSpan.FromSeconds(2), time));
        }

        return sources;
    }
}

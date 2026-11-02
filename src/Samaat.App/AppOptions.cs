using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Core.Platform;
using Samaat.Simulator;

namespace Samaat.App;

public sealed record AppOptions(bool Simulate, bool IncludeNonAudio, bool Minimized = false, bool TestNotification = false)
{
    public static AppOptions Parse(string[] args) => new(
        Simulate: args.Contains("--simulate"),
        IncludeNonAudio: args.Contains("--all-devices"),
        Minimized: args.Contains("--minimized"),
        TestNotification: args.Contains("--test-notification"));

#if SAMAAT_WINDOWS
    public bool HasRealBluetooth => true;
#else
    public bool HasRealBluetooth => false;
#endif

    /// <summary>The simulator is used when asked for, or when this build has no real Bluetooth backend.</summary>
    public bool UsesSimulator => Simulate || !HasRealBluetooth;

    /// <summary>Known models from the <c>devices/</c> folder next to the app. Drivers register here as they're built.</summary>
    public static DriverRegistry CreateRegistry() =>
        new(DeviceCatalog.FromDirectory(Path.Combine(AppContext.BaseDirectory, "devices")));

    public IReadOnlyList<IDeviceSource> CreateSources(TimeProvider time)
    {
        var sources = new List<IDeviceSource>();
#if SAMAAT_WINDOWS
        sources.Add(new Samaat.Platform.Windows.WindowsDeviceSource(time, IncludeNonAudio, CreateRegistry()));
#endif
        if (UsesSimulator)
        {
            sources.Add(new SimulatorDeviceSource(TimeSpan.FromSeconds(2), time));
        }

        return sources;
    }

    /// <param name="iconPng">Supplies the toast icon on first use; the icon is drawn by the UI layer.</param>
    public INotifier CreateNotifier(Func<byte[]?>? iconPng = null) =>
#if SAMAAT_WINDOWS
        new Samaat.Platform.Windows.WindowsToastNotifier(iconPng);
#else
        new NoNotifier();
#endif

    public IStartupRegistration CreateStartupRegistration() =>
#if SAMAAT_WINDOWS
        new Samaat.Platform.Windows.WindowsStartupRegistration();
#else
        new NoStartupRegistration();
#endif
}

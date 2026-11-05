using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Core.Platform;
using Samaat.Handoff;
using Samaat.Handoff.Storage;
using Samaat.Simulator;

namespace Samaat.App;

public sealed record AppOptions(bool Simulate, bool IncludeNonAudio, bool Minimized = false, bool TestNotification = false,
    SimulatorScenario Scenario = SimulatorScenario.Demo)
{
    public static AppOptions Parse(string[] args)
    {
        // --simulate, or --simulate=worst / --simulate=empty for the stress-test data sets (dev only).
        var simulate = args.FirstOrDefault(a => a == "--simulate" || a.StartsWith("--simulate=", StringComparison.Ordinal));
        var scenario = simulate?.Split('=', 2) is [_, var name] ? ParseScenario(name) : SimulatorScenario.Demo;
        return new(
            Simulate: simulate is not null,
            IncludeNonAudio: args.Contains("--all-devices"),
            Minimized: args.Contains("--minimized"),
            TestNotification: args.Contains("--test-notification"),
            Scenario: scenario);
    }

    private static SimulatorScenario ParseScenario(string name) => name switch
    {
        "worst" => SimulatorScenario.WorstCase,
        "empty" => SimulatorScenario.Empty,
        _ => SimulatorScenario.Demo,
    };

#if SAMAAT_WINDOWS
    public bool HasRealBluetooth => true;
#else
    public bool HasRealBluetooth => false;
#endif

    /// <summary>The simulator is used when asked for, or when this build has no real Bluetooth backend.</summary>
    public bool UsesSimulator => Simulate || !HasRealBluetooth;

    /// <summary>Every shipped driver, with the known models from the <c>devices/</c> folder next to the app.</summary>
    public static DriverRegistry CreateRegistry() =>
        Samaat.Drivers.BuiltInDrivers.CreateRegistry(Path.Combine(AppContext.BaseDirectory, "devices"));

    public IReadOnlyList<IDeviceSource> CreateSources(TimeProvider time)
    {
        var sources = new List<IDeviceSource>();
#if SAMAAT_WINDOWS
        sources.Add(new Samaat.Platform.Windows.WindowsDeviceSource(time, IncludeNonAudio, CreateRegistry()));
#endif
        if (UsesSimulator)
        {
            sources.Add(new SimulatorDeviceSource(TimeSpan.FromSeconds(2), time, scenario: Scenario));
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

    public IMotionPreference CreateMotionPreference() =>
#if SAMAAT_WINDOWS
        new Samaat.Platform.Windows.WindowsMotionPreference();
#else
        new NoMotionPreference();
#endif

    public IStartupRegistration CreateStartupRegistration() =>
#if SAMAAT_WINDOWS
        new Samaat.Platform.Windows.WindowsStartupRegistration();
#else
        new NoStartupRegistration();
#endif

    public Samaat.Core.Audio.IAudioShare CreateAudioShare() =>
#if SAMAAT_WINDOWS
        new Samaat.Core.Audio.AudioShare(new Samaat.Platform.Windows.WasapiBackend());
#else
        new Samaat.Core.Audio.NoAudioShare();
#endif

    /// <summary>The phone link, with paired phones kept next to the settings file (encrypted with DPAPI on Windows).</summary>
    public PhoneLinkHost CreatePhoneLinkHost(string storePath, Func<IReadOnlyList<IEarbudDevice>> devices)
    {
#if SAMAAT_WINDOWS
        ISecretProtector protector = new Samaat.Platform.Windows.DpapiSecretProtector();
#else
        ISecretProtector protector = PlainSecretProtector.Instance;
#endif
        return new PhoneLinkHost(new PeerStore(storePath, protector), new PhoneLinkHostOptions(Environment.MachineName), devices);
    }

    public IDefaultOutputSwitcher CreateDefaultOutputSwitcher() =>
#if SAMAAT_WINDOWS
        new Samaat.Platform.Windows.WindowsDefaultOutputSwitcher();
#else
        new NoDefaultOutputSwitcher();
#endif
}

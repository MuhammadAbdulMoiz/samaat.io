using Avalonia;
using Samaat.Core.Profiles;
using Samaat.Handoff.Discovery;
using Samaat.Handoff.Storage;
using Samaat.Mobile;
using Samaat.Mobile.Runtime;
using Samaat.Mobile.Simulation;
using Samaat.Simulator;

namespace Samaat.Mobile.Preview;

/// <summary>
/// Runs the phone app on a computer. Its earbuds are the simulator's three devices (the same addresses as the desktop's
/// <c>--simulate</c> devices), and it finds and dials the computer over the real network, so you can pair it with the
/// desktop app and move earbuds between the two. They start disconnected here, as if they were on the computer.
/// </summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        MobileApp.RuntimeFactory = CreateRuntime;
        AppBuilder.Configure<MobileApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }

    private static MobileRuntime CreateRuntime()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Samaat", "phone-preview");
        var source = new SimulatorDeviceSource(TimeSpan.FromSeconds(2));
        foreach (var device in source.Devices)
        {
            device.SetConnected(false);
        }

        return new MobileRuntime(
            [source],
            new SimulatedPhoneAudio(source.Devices),
            new PeerStore(Path.Combine(folder, "computers.json")),
            $"Phone preview ({Environment.MachineName})",
            new UdpBeaconListener(),
            new NoMobilePlatform(),
            profiles: DeviceProfileDb.Load(Path.Combine(AppContext.BaseDirectory, "devices")),
            settings: new Samaat.Core.Settings.SettingsStore(Path.Combine(folder, "settings.json")));
    }
}

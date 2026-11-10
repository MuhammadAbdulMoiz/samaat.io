using Avalonia;
using Samaat.App.Diagnostics;
using Samaat.Core.Diagnostics;
using Samaat.Core.Settings;

namespace Samaat.App;

internal static class Program
{
    // Held for the process lifetime so the guard's listener keeps running.
    private static SingleInstance? _singleInstance;

    [STAThread]
    public static void Main(string[] args)
    {
        // The log file sits beside the settings; Diagnostics → Copy report includes its newest lines.
        SamaatLog.Sink = RollingFileLog.BesideSettings(SettingsStore.DefaultPath);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            SamaatLog.Error("App", "Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => SamaatLog.Error("App", "Unobserved task exception", e.Exception);
        // --multi-instance (dev) skips the guard so a second copy can run beside the first.
        if (!args.Contains("--multi-instance"))
        {
            _singleInstance = SingleInstance.TryAcquire(@"Local\Samaat.SingleInstance", App.RequestShowMainWindow);
            if (_singleInstance is null)
            {
                SamaatLog.Info("App", "Already running; showing the first window");
                return;
            }
        }

        App.Options = AppOptions.Parse(args);
        SamaatLog.Info("App", $"Started (phone handoff {(App.Options.PhoneHandoff ? "on" : "off")})");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

using Avalonia;
using Samaat.App.Diagnostics;
using Samaat.Core.Diagnostics;
using Samaat.Core.Settings;

namespace Samaat.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // The log file sits beside the settings; Diagnostics → Copy report includes its newest lines.
        SamaatLog.Sink = RollingFileLog.BesideSettings(SettingsStore.DefaultPath);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            SamaatLog.Error("App", "Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => SamaatLog.Error("App", "Unobserved task exception", e.Exception);
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

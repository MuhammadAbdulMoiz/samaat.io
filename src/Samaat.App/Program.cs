using Avalonia;

namespace Samaat.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.Options = AppOptions.Parse(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

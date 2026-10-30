using Avalonia;
using Avalonia.Headless;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(Samaat.App.Tests.TestAppBuilder))]

namespace Samaat.App.Tests;

public static class TestAppBuilder
{
    // Real Skia rendering (not headless drawing) so tests can capture screenshots.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

using Avalonia;
using Avalonia.Headless;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(Samaat.Mobile.Tests.TestAppBuilder))]

namespace Samaat.Mobile.Tests;

public static class TestAppBuilder
{
    // Real Skia rendering so tests can capture phone-sized screenshots.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<MobileApp>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

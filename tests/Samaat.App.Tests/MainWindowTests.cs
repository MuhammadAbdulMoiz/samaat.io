using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Samaat.App.ViewModels;
using Samaat.App.Views;
using Samaat.Core.Battery;

namespace Samaat.App.Tests;

public class MainWindowTests
{
    private static async Task<(MainWindow Window, MainWindowViewModel ViewModel)> ShowAsync()
    {
        var viewModel = new MainWindowViewModel(new AppOptions(Simulate: true, IncludeNonAudio: false));
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        await viewModel.RefreshDevicesAsync();
        Dispatcher.UIThread.RunJobs();
        return (window, viewModel);
    }

    [AvaloniaFact]
    public async Task ShowsSimulatedDevicesWithBatteryTiles()
    {
        var (window, viewModel) = await ShowAsync();

        Assert.Equal(3, viewModel.Devices.Count);
        var buds = viewModel.SelectedDevice!;
        Assert.Equal("Simulated Buds Pro", buds.Name);
        Assert.All(buds.Tiles.Where(t => t.Component != BatteryComponent.Combined), t => Assert.True(t.IsAvailable));
        Assert.False(buds.Tiles.Single(t => t.Component == BatteryComponent.Combined).IsVisible);

        var headset = viewModel.Devices.Single(d => d.Name == "Simulated Headset");
        Assert.True(headset.Tiles.Single(t => t.Component == BatteryComponent.Combined).IsVisible);
        Assert.False(headset.Tiles.Single(t => t.Component == BatteryComponent.Case).IsAvailable);

        window.Close();
        await viewModel.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task LowBudRaisesAlert()
    {
        var (window, viewModel) = await ShowAsync();

        // "Simulated Buds Lite" starts with its right bud at 17%.
        Assert.Contains(viewModel.Alerts, a => a.Contains("Simulated Buds Lite: right bud low"));

        window.Close();
        await viewModel.DisposeAsync();
    }

    /// <summary>Set SAMAAT_SCREENSHOTS to a directory to save rendered screenshots for review.</summary>
    [AvaloniaFact]
    public async Task RendersScreenshots()
    {
        var (window, viewModel) = await ShowAsync();
        var directory = Environment.GetEnvironmentVariable("SAMAAT_SCREENSHOTS");

        foreach (var (variant, suffix) in new[] { (ThemeVariant.Dark, ""), (ThemeVariant.Light, "-light") })
        {
            Application.Current!.RequestedThemeVariant = variant;
            foreach (var device in viewModel.Devices.ToList())
            {
                viewModel.SelectedDevice = device;
                Dispatcher.UIThread.RunJobs();
                var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                if (directory is not null)
                {
                    Directory.CreateDirectory(directory);
                    frame!.Save(Path.Combine(directory, $"{device.Device.Identity.Id}{suffix}.png"));
                }
            }
        }

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        window.Close();
        await viewModel.DisposeAsync();
    }
}

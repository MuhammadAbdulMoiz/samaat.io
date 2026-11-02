using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Samaat.App.Features.Connections;
using Samaat.App.Features.Overview;
using Samaat.App.Features.Sound;
using Samaat.App.Shell;
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

    [AvaloniaFact]
    public async Task NavigationSwitchesSections()
    {
        var (window, viewModel) = await ShowAsync();

        Assert.True(viewModel.IsOverview);
        viewModel.NavigateCommand.Execute(AppSection.Sound);
        Assert.True(viewModel.IsSound);
        Assert.False(viewModel.IsOverview);
        viewModel.NavigateCommand.Execute(AppSection.Connections);
        Assert.True(viewModel.IsConnections);

        window.Close();
        await viewModel.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task DevicesExposeModuleViewModels()
    {
        var (window, viewModel) = await ShowAsync();
        var pro = viewModel.Devices.Single(d => d.Name == "Simulated Buds Pro");
        var headset = viewModel.Devices.Single(d => d.Name == "Simulated Headset");

        Assert.True(pro.NoiseControl.IsSelector);
        Assert.True(pro.Connections.HasMultipoint);
        Assert.True(headset.NoiseControl.IsUnavailable);
        Assert.False(headset.Connections.HasMultipoint);
        Assert.Equal(["Battery", "Noise control", "Connections"], pro.CapabilityGroups.Select(g => g.Title));

        window.Close();
        await viewModel.DisposeAsync();
    }

    /// <summary>Lets page cross-fades and value transitions finish (headless time only moves when ticked).</summary>
    private static void SettleAnimations()
    {
        for (var i = 0; i < 60; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
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
                foreach (var section in Enum.GetValues<AppSection>())
                {
                    viewModel.NavigateCommand.Execute(section);
                    SettleAnimations();
                    var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    if (directory is not null)
                    {
                        Directory.CreateDirectory(directory);
                        var page = section == AppSection.Overview ? "" : $"-{section.ToString().ToLowerInvariant()}";
                        frame!.Save(Path.Combine(directory, $"{device.Device.Identity.Id}{page}{suffix}.png"));
                    }
                }
            }
        }

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        viewModel.NavigateCommand.Execute(AppSection.Overview);

        // Narrow window: the hero stacks and the stat strip wraps.
        window.Width = 720;
        viewModel.SelectedDevice = viewModel.Devices[0];
        SettleAnimations();
        var narrow = window.CaptureRenderedFrame();
        Assert.NotNull(narrow);
        if (directory is not null)
        {
            narrow!.Save(Path.Combine(directory, "sim-buds-pro-narrow.png"));
        }

        window.Close();
        await viewModel.DisposeAsync();
    }
}

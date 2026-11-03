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
using Samaat.Core.Settings;

namespace Samaat.App.Tests;

public class MainWindowTests
{
    private static async Task<(MainWindow Window, MainWindowViewModel ViewModel)> ShowAsync()
    {
        // A temp-directory store, so tests never read or write the real %APPDATA% settings.
        var settings = new SettingsStore(Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"), "settings.json"));
        var viewModel = new MainWindowViewModel(new AppOptions(Simulate: true, IncludeNonAudio: false), settings: settings);
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
        viewModel.NavigateCommand.Execute(AppSection.Info);
        Assert.True(viewModel.IsInfo);
        viewModel.NavigateCommand.Execute(AppSection.Details);
        Assert.IsType<DetailsPageModel>(viewModel.CurrentPage);
        viewModel.NavigateCommand.Execute(AppSection.Settings);
        Assert.True(viewModel.IsSettings);
        Assert.False(viewModel.ShowEmptyState);

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
        Assert.Equal(["Battery"], pro.Info.UnavailableGroups.Select(g => g.Module));
        Assert.Equal(["Battery", "Noise control", "Connections"], headset.Info.UnavailableGroups.Select(g => g.Module));

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
        // The docs know the simulated devices without their "sim-" prefix.
        static string FileName(DeviceViewModel d) => d.Device.Identity.Id.Replace("sim-", "");

        foreach (var (variant, suffix) in new[] { (ThemeVariant.Dark, ""), (ThemeVariant.Light, "-light") })
        {
            Application.Current!.RequestedThemeVariant = variant;

            void Capture(string name)
            {
                SettleAnimations();
                var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                if (directory is not null)
                {
                    Directory.CreateDirectory(directory);
                    frame!.Save(Path.Combine(directory, $"{name}{suffix}.png"));
                }
            }

            foreach (var device in viewModel.Devices.ToList())
            {
                viewModel.SelectDeviceCommand.Execute(device);
                foreach (var section in Enum.GetValues<AppSection>().Where(s => s != AppSection.Settings))
                {
                    viewModel.NavigateCommand.Execute(section);
                    var page = section == AppSection.Overview ? "" : $"-{section.ToString().ToLowerInvariant()}";
                    Capture($"{FileName(device)}{page}");
                }
            }

            viewModel.NavigateCommand.Execute(AppSection.Settings);
            Capture("settings");
        }

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        viewModel.SelectDeviceCommand.Execute(viewModel.Devices[0]);
        viewModel.NavigateCommand.Execute(AppSection.Overview);

        // Narrow window: the sidebar collapses to a rail and the device rail stacks above the workspace.
        window.Width = 720;
        SettleAnimations();
        var narrow = window.CaptureRenderedFrame();
        Assert.NotNull(narrow);
        if (directory is not null)
        {
            narrow!.Save(Path.Combine(directory, "buds-pro-narrow.png"));
        }

        window.Close();
        await viewModel.DisposeAsync();
    }
}

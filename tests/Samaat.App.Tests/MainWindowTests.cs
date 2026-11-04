using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Avalonia.Styling;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Samaat.App.Features.Connections;
using Samaat.App.Features.Overview;
using Samaat.App.Features.Sound;
using Samaat.App.Shell;
using Samaat.Core.Battery;
using Samaat.Core.Profiles;
using Samaat.Core.Settings;

namespace Samaat.App.Tests;

public class MainWindowTests
{
    private static async Task<(MainWindow Window, MainWindowViewModel ViewModel)> ShowAsync(
        DeviceProfileDb? profiles = null, Func<Samaat.Core.Audio.IAudioShare>? audioShareFactory = null, Samaat.Core.Platform.IDefaultOutputSwitcher? outputSwitcher = null)
    {
        // A temp-directory store, so tests never read or write the real %APPDATA% settings.
        var settings = new SettingsStore(Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"), "settings.json"));
        var viewModel = new MainWindowViewModel(new AppOptions(Simulate: true, IncludeNonAudio: false), settings: settings, profiles: profiles,
            audioShareFactory: audioShareFactory, outputSwitcher: outputSwitcher);
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
        Assert.Empty(pro.Info.UnavailableGroups); // every battery level, including the derived overall one, is available
        Assert.Equal(["Battery", "Noise control", "Equalizer", "Connections"], headset.Info.UnavailableGroups.Select(g => g.Module));

        window.Close();
        await viewModel.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task RailShowsEachPartWithItsLevel_WhenTheProfileHasPartImages()
    {
        // A profile for the simulated buds whose three part images are copies of a shipped photo.
        var dir = Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var photo = Path.Combine(AppContext.BaseDirectory, "devices", "audionic", "paradox-e.png");
        foreach (var part in new[] { "left", "right", "case" })
        {
            File.Copy(photo, Path.Combine(dir, $"{part}.png"));
        }

        File.WriteAllText(Path.Combine(dir, "pro.json"), """
            { "id": "sim.pro", "displayName": "Pro", "match": { "namePattern": "^Simulated Buds Pro$" }, "image": "left.png",
              "images": { "left": "left.png", "right": "right.png", "case": "case.png" } }
            """);
        var (window, viewModel) = await ShowAsync(DeviceProfileDb.Load(dir));

        List<string> VisibleNames() => window.GetVisualDescendants().OfType<Control>()
            .Where(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) is { } n && (n.StartsWith("Left bud,") || n.StartsWith("Right bud,") || n.StartsWith("Case,")))
            .Select(c => AutomationProperties.GetName(c)!).ToList();

        // The simulator starts its buds at 93% and 81% and its case at 64%, none charging.
        Assert.Equal(["Left bud, 93 percent", "Right bud, 81 percent", "Case, 64 percent"], VisibleNames());

        viewModel.SelectDeviceCommand.Execute(viewModel.Devices.Single(d => d.Name == "Simulated Buds Lite")); // no profile
        Dispatcher.UIThread.RunJobs();
        Assert.False(viewModel.SelectedDevice!.ShowArtworkParts); // no profile photos: the unknown pieces look instead
        Assert.True(viewModel.SelectedDevice.ShowUnknownPieces);

        window.Close();
        await viewModel.DisposeAsync();
        Directory.Delete(dir, recursive: true);
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

            // A disconnected device shows the hero Connect button.
            var lite = viewModel.Devices.Single(d => d.Name == "Simulated Buds Lite");
            viewModel.SelectDeviceCommand.Execute(lite);
            viewModel.NavigateCommand.Execute(AppSection.Overview);
            ((Samaat.Simulator.SimulatedEarbudDevice)lite.Device).SetConnected(false);
            Dispatcher.UIThread.RunJobs();
            Capture("buds-lite-disconnected");
            ((Samaat.Simulator.SimulatedEarbudDevice)lite.Device).SetConnected(true);
            Dispatcher.UIThread.RunJobs();

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


        // The Share audio card needs fake outputs, so it gets its own window.
        var fakes = new List<FakeAudioShare>();
        var (shareWindow, shareViewModel) = await ShowAsync(audioShareFactory: () =>
        {
            var fake = new FakeAudioShare();
            if (fakes.Count == 0)
            {
                fake.Outputs =
                [
                    new("hp", "Headphones (Quantum)", true),
                    new("spk", "Speakers (Realtek)", false),
                    new("tv", "LG TV", false),
                    new("xm5", "WH-1000XM5 Headset", false),
                ];
            }

            fakes.Add(fake);
            return fake;
        }, outputSwitcher: new FakeSwitcher());
        shareViewModel.SelectDeviceCommand.Execute(shareViewModel.Devices[0]);
        shareViewModel.NavigateCommand.Execute(AppSection.Sound);
        var speakers = shareViewModel.AudioShare.Rows[0];
        speakers.IsSharing = true;
        await Task.Delay(100);
        Dispatcher.UIThread.RunJobs();
        speakers.DelayMs = 120;
        foreach (var (variant, suffix) in new[] { (ThemeVariant.Dark, ""), (ThemeVariant.Light, "-light") })
        {
            Application.Current!.RequestedThemeVariant = variant;
            SettleAnimations();
            var scroller = shareWindow.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Extent.Height > s.Viewport.Height);
            shareViewModel.Alerts.Clear();
            scroller.ScrollToEnd();
            SettleAnimations();
            SettleAnimations();
            Assert.Empty(shareViewModel.Alerts);
            var shareFrame = shareWindow.CaptureRenderedFrame();
            Assert.NotNull(shareFrame);
            if (directory is not null)
            {
                shareFrame!.Save(Path.Combine(directory, $"buds-pro-sound-share{suffix}.png"));
            }
        }

        shareWindow.Close();
        await shareViewModel.DisposeAsync();
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        window.Close();
        await viewModel.DisposeAsync();
    }
}

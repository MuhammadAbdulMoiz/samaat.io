using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Samaat.App.Shell;
using Samaat.Core.Settings;
using Samaat.Simulator;

namespace Samaat.App.Tests;

/// <summary>
/// Renders every page with the simulator's worst-case data (break-ui): long, short, right-to-left and emoji names,
/// missing fields, 0 % and 100 %, unreported readings, seven linked hosts, and no devices at all. Set
/// SAMAAT_STRESS_SCREENSHOTS to a folder to save the frames; without it the test still renders every state, so a
/// crash or binding failure on unusual data fails the build.
/// </summary>
public sealed class WorstCaseRenderTests
{
    private static async Task<(MainWindow Window, MainWindowViewModel ViewModel)> ShowAsync(SimulatorScenario scenario)
    {
        var settings = new SettingsStore(Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"), "settings.json"));
        var source = new SimulatorDeviceSource(TimeSpan.FromHours(1), commandLatency: TimeSpan.Zero, scenario: scenario);
        var viewModel = new MainWindowViewModel(new AppOptions(Simulate: true, IncludeNonAudio: false, Scenario: scenario),
            settings: settings, sources: [source]);
        var window = new MainWindow { DataContext = viewModel, Width = 1180, Height = 780 };
        window.Show();
        await viewModel.RefreshDevicesAsync();
        viewModel.SetDeviceFilterCommand.Execute(DeviceFilter.Paired);
        Dispatcher.UIThread.RunJobs();
        return (window, viewModel);
    }

    private static void Settle()
    {
        for (var i = 0; i < 60; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Capture(MainWindow window, string name)
    {
        Settle();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SAMAAT_STRESS_SCREENSHOTS") is { } directory)
        {
            Directory.CreateDirectory(directory);
            frame!.Save(Path.Combine(directory, $"{name}.png"));
        }
    }

    [AvaloniaFact]
    public async Task WorstCase_RendersEveryPage_InBothThemes_AtFullAndMinimumWidth()
    {
        var (window, viewModel) = await ShowAsync(SimulatorScenario.WorstCase);
        Assert.Equal(4, viewModel.Devices.Count);

        foreach (var (variant, suffix) in new[] { (ThemeVariant.Dark, ""), (ThemeVariant.Light, "-light") })
        {
            Application.Current!.RequestedThemeVariant = variant;
            viewModel.Settings.Theme = variant == ThemeVariant.Dark ? ThemeChoice.Dark : ThemeChoice.Light;
            foreach (var (width, size) in new[] { (1180.0, ""), (640.0, "-640") })
            {
                window.Width = width;
                for (var i = 0; i < viewModel.Devices.Count; i++)
                {
                    viewModel.SelectDeviceCommand.Execute(viewModel.Devices[i]);
                    foreach (var section in Enum.GetValues<AppSection>().Where(s => s != AppSection.Settings))
                    {
                        viewModel.NavigateCommand.Execute(section);
                        Capture(window, $"worst-{i + 1}-{section.ToString().ToLowerInvariant()}{size}{suffix}");
                    }
                }
            }

            window.Width = 1180;
            viewModel.SetDashboardCommand.Execute(DashboardView.Focus);
            viewModel.SelectDeviceCommand.Execute(viewModel.Devices[0]);
            viewModel.NavigateCommand.Execute(AppSection.Overview);
            Capture(window, $"worst-focus{suffix}");
            viewModel.SetDashboardCommand.Execute(DashboardView.Glance);
            Capture(window, $"worst-glance{suffix}");
            window.Width = 420;
            Capture(window, $"worst-glance-420{suffix}");
            window.Width = 1180;
            viewModel.SetDashboardCommand.Execute(DashboardView.Studio);
        }

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        window.Close();
        await viewModel.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Empty_RendersTheNoDevicesState()
    {
        var (window, viewModel) = await ShowAsync(SimulatorScenario.Empty);

        Assert.Empty(viewModel.Devices);
        Capture(window, "worst-empty");

        window.Close();
        await viewModel.DisposeAsync();
    }
}

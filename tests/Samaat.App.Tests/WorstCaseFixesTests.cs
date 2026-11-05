using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Samaat.App.Shell;
using Samaat.Core.Settings;
using Samaat.Simulator;

namespace Samaat.App.Tests;

/// <summary>Fixes for what the worst-case data broke (break-ui report, 2026-11-05).</summary>
public sealed class WorstCaseFixesTests
{
    private static async Task<MainWindowViewModel> WorstCaseAsync()
    {
        var settings = new SettingsStore(Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"), "settings.json"));
        var vm = new MainWindowViewModel(new AppOptions(Simulate: true, IncludeNonAudio: false, Scenario: SimulatorScenario.WorstCase),
            settings: settings, sources: [new SimulatorDeviceSource(TimeSpan.FromHours(1), commandLatency: TimeSpan.Zero, scenario: SimulatorScenario.WorstCase)]);
        await vm.RefreshDevicesAsync();
        vm.SetDeviceFilterCommand.Execute(DeviceFilter.Paired);
        Dispatcher.UIThread.RunJobs();
        return vm;
    }

    // #1: one toast at a time, so stacked toasts can't cover the page's own buttons.
    [AvaloniaFact]
    public async Task Toasts_ShowOneAtATime_AndCountTheRest()
    {
        await using var vm = await WorstCaseAsync();
        vm.Alerts.Clear();

        vm.ShowMessage("First");
        vm.ShowMessage("Second");
        vm.ShowMessage("Third");

        Assert.Equal("Third", vm.CurrentAlert);
        Assert.Equal("+2 more", vm.MoreAlertsText);

        vm.DismissAlertCommand.Execute("Third");
        Assert.Equal("Second", vm.CurrentAlert);
        Assert.Equal("+1 more", vm.MoreAlertsText);

        vm.Alerts.Clear();
        Assert.Null(vm.CurrentAlert);
        Assert.Null(vm.MoreAlertsText);
    }

    // #2: a device that has never reported a level doesn't claim an update time or a source.
    [AvaloniaFact]
    public async Task ADeviceWithNoReading_ShowsNoUpdateTimeOrSource()
    {
        await using var vm = await WorstCaseAsync();
        var away = vm.Devices.Single(d => d.Name.StartsWith("ソニー", StringComparison.Ordinal));

        Assert.False(away.HasLastUpdate);
        Assert.Equal("waiting for a reading", away.BatterySourceMeta);

        var jo = vm.Devices.Single(d => d.Name == "Jo");
        Assert.True(jo.HasLastUpdate);
        Assert.Equal("from simulator", jo.BatterySourceMeta);
    }

    // #4: the caption names every piece without a level, not only the ones the device never lists.
    [AvaloniaFact]
    public async Task BatteryCaption_NamesPiecesWithNoLevel()
    {
        await using var vm = await WorstCaseAsync();

        var jo = vm.Devices.Single(d => d.Name == "Jo");
        Assert.Equal("No level from the right bud yet, and the case isn't reported by this device", jo.BatteryReasonText);

        var full = vm.Devices.Single(d => d.Name.StartsWith("Aleksandra", StringComparison.Ordinal));
        Assert.Equal("No level from the case yet", full.BatteryReasonText);
    }
}

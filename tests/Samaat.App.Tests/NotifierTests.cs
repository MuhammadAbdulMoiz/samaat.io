using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Samaat.App.Features.Overview;
using Samaat.App.Features.Settings;
using Samaat.App.Shell;
using Samaat.Core.Platform;
using Samaat.Core.Settings;

namespace Samaat.App.Tests;

public class NotifierTests
{
    private sealed class FakeNotifier : INotifier
    {
        public List<(string Title, string Body)> Shown { get; } = [];

        public void Show(string title, string body) => Shown.Add((title, body));
    }

    private static SettingsStore NewStore(bool systemNotifications)
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"), "settings.json"));
        store.Save(new AppSettings { SystemNotifications = systemNotifications });
        return store;
    }

    private static async Task<(FakeNotifier Notifier, MainWindowViewModel Vm)> RunAsync(bool systemNotifications, bool? windowHidden = null, SettingsStore? existing = null)
    {
        var store = existing ?? NewStore(systemNotifications);
        var notifier = new FakeNotifier();
        var vm = new MainWindowViewModel(new AppOptions(Simulate: true, IncludeNonAudio: false), settings: store, notifier: notifier);
        if (windowHidden is { } hidden)
        {
            vm.IsWindowHidden = () => hidden;
        }

        await vm.RefreshDevicesAsync();
        Dispatcher.UIThread.RunJobs();
        return (notifier, vm);
    }

    [AvaloniaTheory]
    [InlineData(true, true, 1)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    public async Task ShowMessage_NotifiesOnlyWhenWindowHiddenAndEnabled(bool hidden, bool enabled, int expected)
    {
        var (notifier, vm) = await RunAsync(enabled, windowHidden: hidden);
        notifier.Shown.Clear();

        vm.ShowMessage("Connect failed");

        Assert.Equal(expected, notifier.Shown.Count(n => n.Body == "Connect failed"));
        Assert.Contains(vm.Alerts, a => a == "Connect failed");
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task BatteryAlert_WhileHidden_NotifiesOnce()
    {
        var (notifier, vm) = await RunAsync(true, windowHidden: true);

        Assert.Single(notifier.Shown, n => n.Body.Contains("Simulated Buds Lite: right bud low"));
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task HiddenToTray_ShowsTheHintOnceAndSavesTheFlag()
    {
        var store = NewStore(true);
        var (notifier, vm) = await RunAsync(true, existing: store);
        notifier.Shown.Clear();

        vm.OnHiddenToTray();
        vm.OnHiddenToTray();

        var hint = Assert.Single(notifier.Shown);
        Assert.Equal("Samaat is still running", hint.Title);
        Assert.Contains("Keep running in the tray", hint.Body);
        Assert.True(store.Load().TrayHintShown);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task HiddenToTray_WithNotificationsOff_ShowsNothingAndKeepsTheFlag()
    {
        var store = NewStore(false);
        var (notifier, vm) = await RunAsync(false, existing: store);

        vm.OnHiddenToTray();

        Assert.Empty(notifier.Shown);
        Assert.False(store.Load().TrayHintShown);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task ChangingAnotherSettingAfterTheHint_KeepsTheFlag()
    {
        var store = NewStore(true);
        var (_, vm) = await RunAsync(true, existing: store);
        vm.OnHiddenToTray();

        vm.Settings.Theme = ThemeChoice.Light;

        var saved = store.Load();
        Assert.Equal(ThemeChoice.Light, saved.Theme);
        Assert.True(saved.TrayHintShown);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Alert_ShowsSystemNotificationWhenEnabled()
    {
        var (notifier, vm) = await RunAsync(systemNotifications: true);

        // "Simulated Buds Lite" starts with its right bud at 17%.
        var shown = Assert.Single(notifier.Shown, n => n.Body.Contains("Simulated Buds Lite: right bud low"));
        Assert.Equal("Samaat", shown.Title);
        Assert.Contains(vm.Alerts, a => a == shown.Body);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Alert_StaysInAppOnlyWhenDisabled()
    {
        var (notifier, vm) = await RunAsync(systemNotifications: false);

        Assert.Empty(notifier.Shown);
        Assert.Contains(vm.Alerts, a => a.Contains("Simulated Buds Lite: right bud low"));
        await vm.DisposeAsync();
    }
}

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

    private static async Task<(FakeNotifier Notifier, MainWindowViewModel Vm)> RunAsync(bool systemNotifications)
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"), "settings.json"));
        store.Save(new AppSettings { SystemNotifications = systemNotifications });
        var notifier = new FakeNotifier();
        var vm = new MainWindowViewModel(new AppOptions(Simulate: true, IncludeNonAudio: false), settings: store, notifier: notifier);
        await vm.RefreshDevicesAsync();
        Dispatcher.UIThread.RunJobs();
        return (notifier, vm);
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

using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Samaat.App.Shell;
using Samaat.App.Tray;
using Samaat.Core.Settings;
using Samaat.Handoff;
using Samaat.Handoff.Discovery;
using Samaat.Handoff.Storage;

namespace Samaat.App.Tests;

/// <summary>
/// One switch, <see cref="AppOptions.PhoneHandoff"/>, decides whether the phone handoff ships: the Settings and Links
/// phone panels and the tray follow it. Off, the phone link is never created, so nothing listens or broadcasts.
/// </summary>
public sealed class PhoneHandoffFlagTests
{
    private readonly InMemoryBeaconBus _bus = new();

    [Fact]
    public void Default_IsOnOutsideTheStoreBuild()
    {
        Assert.True(AppOptions.PhoneHandoffDefault);
        Assert.True(new AppOptions(Simulate: true, IncludeNonAudio: false).PhoneHandoff);
        Assert.True(AppOptions.Parse([]).PhoneHandoff);
    }

    [Fact]
    public void Tray_OffersPairing_OnlyWhenTheFlagIsOn()
    {
        Assert.Contains(TraySummary.PairPhoneItem, TraySummary.AppItems(phoneHandoff: true));
        Assert.DoesNotContain(TraySummary.PairPhoneItem, TraySummary.AppItems(phoneHandoff: false));
        Assert.Equal(["Open Samaat", "Refresh devices", "Quit"], TraySummary.AppItems(phoneHandoff: false));
    }

    [AvaloniaFact]
    public async Task Off_NoPhoneLink_PanelsShowComingSoon()
    {
        var (window, viewModel) = await ShowAsync(phoneHandoff: false);
        try
        {
            Assert.Null(viewModel.PhoneLink);
            Assert.Null(viewModel.Settings.PhoneLink);
            Assert.True(viewModel.Settings.ShowPhoneComingSoon);
            Assert.All(viewModel.Devices, d =>
            {
                Assert.Null(d.PhoneMove);
                Assert.True(d.ShowPhoneComingSoon);
            });

            viewModel.Section = AppSection.Settings;
            Settle();
            Assert.Contains("Coming soon", VisibleTexts(window));
            Assert.DoesNotContain("Pair a phone", VisibleButtons(window));

            viewModel.Section = AppSection.Connections;
            Settle();
            Assert.Contains("Coming soon", VisibleTexts(window));
            Assert.DoesNotContain("Pair a phone", VisibleButtons(window));
        }
        finally
        {
            window.Close();
            await viewModel.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task On_PanelsOfferPairing_AndNoComingSoon()
    {
        var (window, viewModel) = await ShowAsync(phoneHandoff: true);
        try
        {
            Assert.NotNull(viewModel.PhoneLink);
            Assert.False(viewModel.Settings.ShowPhoneComingSoon);
            Assert.All(viewModel.Devices, d => Assert.False(d.ShowPhoneComingSoon));

            viewModel.Section = AppSection.Settings;
            Settle();
            Assert.DoesNotContain("Coming soon", VisibleTexts(window));
            Assert.Contains("Pair a phone", VisibleButtons(window));

            viewModel.Section = AppSection.Connections;
            Settle();
            Assert.DoesNotContain("Coming soon", VisibleTexts(window));
            Assert.Contains("Pair a phone", VisibleButtons(window));
        }
        finally
        {
            window.Close();
            await viewModel.DisposeAsync();
        }
    }

    private async Task<(MainWindow Window, MainWindowViewModel ViewModel)> ShowAsync(bool phoneHandoff)
    {
        var dir = Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"));
        var viewModel = new MainWindowViewModel(new AppOptions(Simulate: true, IncludeNonAudio: false, PhoneHandoff: phoneHandoff),
            settings: new SettingsStore(Path.Combine(dir, "settings.json")),
            phoneLinkFactory: devices => new PhoneLinkHost(new PeerStore(Path.Combine(dir, "phones.json")),
                new PhoneLinkHostOptions("Desk PC") { BindAddress = IPAddress.Loopback, Port = 0 }, devices, _bus.CreateSender()));
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        await viewModel.RefreshDevicesAsync();
        Settle();
        Assert.NotEmpty(viewModel.Devices);
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

    private static List<string> VisibleTexts(Visual root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Text is not null).Select(t => t.Text!)];

    private static List<string> VisibleButtons(Visual root) =>
        [.. root.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string ?? "")];
}

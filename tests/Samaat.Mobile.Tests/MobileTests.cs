using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Automation;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Samaat.Core.Devices;
using Samaat.Core.NoiseControl;
using Samaat.Handoff;
using Samaat.Handoff.Discovery;
using Samaat.Handoff.Storage;
using Samaat.Mobile.Runtime;
using Samaat.Mobile.Simulation;
using Samaat.Mobile.ViewModels;
using Samaat.Mobile.Views;
using Samaat.Simulator;

namespace Samaat.Mobile.Tests;

/// <summary>
/// The phone app headless, against a real computer-side <see cref="PhoneLinkHost"/> over loopback. Both sides use the
/// simulator, so the same earbuds (by address) exist on each, like a real pair of buds paired with both.
/// </summary>
public sealed class MobileTests : IAsyncDisposable
{
    private const string BudsPro = "02:00:5E:10:00:01";

    private readonly InMemoryBeaconBus _bus = new();
    private readonly SimulatorDeviceSource _computerDevices = new(TimeSpan.FromHours(1));
    private readonly PhoneLinkHost _computer;
    private readonly MobileRuntime _runtime;
    private readonly MainViewModel _viewModel;
    private readonly Window _window;
    private PairingRequest? _request;

    public MobileTests()
    {
        _computer = new PhoneLinkHost(new PeerStore(null),
            new PhoneLinkHostOptions("Desk PC") { BindAddress = IPAddress.Loopback, Port = 0, BeaconInterval = TimeSpan.FromMilliseconds(100) },
            () => _computerDevices.Devices, _bus.CreateSender());
        _computer.PairingRequested += (_, r) => _request = r;
        _computer.Start();

        _runtime = SimulatedMobile.Create(_bus.CreateListener(),
            clientOptions: new PhoneLinkClientOptions { RetryMin = TimeSpan.FromMilliseconds(100), RetryMax = TimeSpan.FromMilliseconds(500) });
        _viewModel = new MainViewModel(_runtime);
        _window = new Window { Width = 412, Height = 892, Content = new MainView { DataContext = _viewModel } };
        _window.Show();
    }

    private SimulatedEarbudDevice PhoneBuds => (SimulatedEarbudDevice)_runtime.Registry.Devices.Single(d => d.Identity.Details?.Address == BudsPro);

    private SimulatedEarbudDevice ComputerBuds => _computerDevices.Devices.Single(d => d.Identity.Details?.Address == BudsPro);

    private async Task StartAsync()
    {
        await _viewModel.StartAsync();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task UntilAsync(Func<bool> condition, string what)
    {
        var deadline = Environment.TickCount64 + 15_000;
        while (!condition())
        {
            Assert.True(Environment.TickCount64 < deadline, $"Timed out waiting for: {what}");
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private async Task PairAsync(Action? whileCodeShown = null)
    {
        _viewModel.ShowComputerCommand.Execute(null);
        _computer.OpenPairing();
        await UntilAsync(() => _viewModel.Computer.HasNearby, "the computer in the nearby list");
        Assert.Equal("Desk PC", _viewModel.Computer.Nearby[0].Name);

        await _viewModel.Computer.Nearby[0].PairCommand.ExecuteAsync(null);
        await UntilAsync(() => _viewModel.Computer.ShowCode && _request is not null, "codes on both sides");
        Assert.Equal(_request!.Code, _viewModel.Computer.Code);
        Assert.Equal("Pairing with Desk PC", _viewModel.Computer.PairingTitle);
        whileCodeShown?.Invoke();

        var computerSide = _request.AcceptAsync();
        await _viewModel.Computer.ConfirmCommand.ExecuteAsync(null);
        Assert.True(await computerSide);
        await UntilAsync(() => _viewModel.Computer.IsOnline, "the phone to connect");
    }

    [AvaloniaFact]
    public async Task ListsTheEarbudsPairedWithThePhone()
    {
        await StartAsync();

        Assert.Equal(3, _viewModel.Devices.Count);
        Assert.True(_viewModel.ShowDeviceList);
        Assert.Equal("Samaat", _viewModel.Title);
    }

    [AvaloniaFact]
    public async Task DevicePage_ShowsBatteryNoiseAndEq()
    {
        await StartAsync();
        var buds = _viewModel.Devices.Single(d => d.Name == "Simulated Buds Pro");

        _viewModel.OpenCommand.Execute(buds);

        Assert.True(_viewModel.ShowDevice);
        Assert.True(_viewModel.CanGoBack);
        Assert.Equal("Simulated Buds Pro", _viewModel.Title);
        Assert.Equal(["Left bud", "Right bud", "Case"], buds.Battery.Select(b => b.Label));
        Assert.True(buds.HasNoise);
        Assert.Contains(buds.NoiseModes, m => m.Label == "Noise cancelling");
        Assert.True(buds.HasEq);
        Assert.Equal(4, buds.EqPresets.Count);
        Assert.True(_viewModel.GoBack());
        Assert.True(_viewModel.ShowDeviceList);
    }

    [AvaloniaFact]
    public async Task ChoosingANoiseMode_SetsItOnTheBuds()
    {
        await StartAsync();
        var buds = _viewModel.Devices.Single(d => d.Name == "Simulated Buds Pro");
        var transparency = buds.NoiseModes.Single(m => m.Label == "Transparency");

        await transparency.SelectCommand.ExecuteAsync(null);

        Assert.Equal(NoiseMode.Transparency, buds.Device.GetFeature<INoiseControlFeature>()!.Current.Mode);
        Assert.True(buds.NoiseModes.Single(m => m.Label == "Transparency").IsSelected);
    }

    [AvaloniaFact]
    public async Task ConnectOnThePhone_UsesThePhonesAudioLink()
    {
        await StartAsync();
        PhoneBuds.SetConnected(false);
        Dispatcher.UIThread.RunJobs();
        var buds = _viewModel.Devices.Single(d => d.Name == "Simulated Buds Pro");
        Assert.True(buds.ShowConnect);

        await buds.ConnectCommand.ExecuteAsync(null);

        Assert.True(buds.IsConnected);
        Assert.Equal("Connected to this phone", buds.ConnectionText);
    }

    [AvaloniaFact]
    public async Task NotPaired_DevicePageOffersPairing()
    {
        await StartAsync();
        var buds = _viewModel.Devices[0];

        Assert.True(buds.ShowPairComputer);
        Assert.False(buds.CanSendToComputer);
        buds.PairComputerCommand.Execute(null);
        Assert.True(_viewModel.IsComputer);
        Assert.True(_viewModel.Computer.ShowNearby);
        Assert.True(_viewModel.Computer.ShowSearching);
    }

    [AvaloniaFact]
    public async Task PairFromThePhone_ThenBringEarbudsFromTheComputerAndSendThemBack()
    {
        await StartAsync();
        PhoneBuds.SetConnected(false);
        Dispatcher.UIThread.RunJobs();
        await PairAsync();
        Assert.Equal("Desk PC", _viewModel.Computer.ComputerName);
        Assert.Equal("Paired with Desk PC.", _viewModel.Message);

        var buds = _viewModel.Devices.Single(d => d.Name == "Simulated Buds Pro");
        await UntilAsync(() => buds.CanBringHere, "Bring to this phone");
        Assert.Equal("Playing on Desk PC", buds.ComputerStateText);

        await buds.BringHereCommand.ExecuteAsync(null);
        await buds.PendingMove;

        Assert.False(buds.ResultIsError);
        Assert.Equal("Simulated Buds Pro moved to Simulated phone.", buds.ResultText);
        Assert.Equal(ConnectionState.Disconnected, ComputerBuds.Connection);
        Assert.Equal(ConnectionState.Connected, PhoneBuds.Connection);

        await UntilAsync(() => buds.CanSendToComputer, "Move to Desk PC");
        Assert.Equal("Move to Desk PC", buds.SendText);
        await buds.SendToComputerCommand.ExecuteAsync(null);
        await buds.PendingMove;

        Assert.Equal("Simulated Buds Pro moved to this computer.", buds.ResultText);
        Assert.Equal(ConnectionState.Connected, ComputerBuds.Connection);
        Assert.Equal(ConnectionState.Disconnected, PhoneBuds.Connection);
    }

    [AvaloniaFact]
    public async Task PhoneCantConnect_ComputerKeepsThem_AndThePhoneSaysWhy()
    {
        await StartAsync();
        PhoneBuds.SetConnected(false);
        Dispatcher.UIThread.RunJobs();
        await PairAsync();
        ((SimulatedPhoneAudio)_runtime.Audio).FailNextConnect = "You didn't pick the earbuds.";
        var buds = _viewModel.Devices.Single(d => d.Name == "Simulated Buds Pro");
        await UntilAsync(() => buds.CanBringHere, "Bring to this phone");

        await buds.BringHereCommand.ExecuteAsync(null);
        await buds.PendingMove;

        Assert.True(buds.ResultIsError);
        Assert.Contains("You didn't pick the earbuds.", buds.ResultText);
        Assert.Equal(ConnectionState.Connected, ComputerBuds.Connection);
    }

    [AvaloniaFact]
    public async Task ComputerDeclines_PhoneSaysSo()
    {
        await StartAsync();
        _viewModel.ShowComputerCommand.Execute(null);
        _computer.OpenPairing();
        await UntilAsync(() => _viewModel.Computer.HasNearby, "the computer in the nearby list");
        await _viewModel.Computer.Nearby[0].PairCommand.ExecuteAsync(null);
        await UntilAsync(() => _request is not null, "the computer's prompt");

        await _request!.DeclineAsync();

        await UntilAsync(() => _viewModel.Computer.HasPairingError, "the error on the phone");
        Assert.Contains("cancelled on Desk PC", _viewModel.Computer.PairingError);
        Assert.False(_viewModel.Computer.IsPaired);
    }

    [AvaloniaFact]
    public async Task Forget_GoesBackToPairing()
    {
        await StartAsync();
        await PairAsync();

        await _viewModel.Computer.ForgetCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(_viewModel.Computer.IsPaired);
        Assert.True(_viewModel.Computer.ShowNearby);
    }

    [AvaloniaFact]
    public async Task BluetoothOff_ShowsBanner()
    {
        ((SimulatedPhoneAudio)_runtime.Audio).IsBluetoothOn = false;
        await StartAsync();

        Assert.True(_viewModel.HasBanner);
        Assert.Equal("Bluetooth is off.", _viewModel.BannerText);
        await _viewModel.FixBannerCommand.ExecuteAsync(null);
        Assert.Equal(1, ((NoMobilePlatform)_runtime.Platform).SettingsOpened);
    }

    [AvaloniaFact]
    public async Task PermissionMissing_AsksOnStart_AndBannerOffersAllow()
    {
        var audio = (SimulatedPhoneAudio)_runtime.Audio;
        audio.Blocker = "Allow Samaat to find and connect to nearby devices.";
        await StartAsync();

        Assert.Equal(1, ((NoMobilePlatform)_runtime.Platform).PermissionRequests);
        Assert.Equal("Allow", _viewModel.BannerAction);
        audio.Blocker = null;
        await _viewModel.FixBannerCommand.ExecuteAsync(null);
        Assert.False(_viewModel.HasBanner);
    }

    /// <summary>Every visible button on a screen: at least 44×44 (WCAG 2.5.5) and named for screen readers (4.1.2).</summary>
    private void AssertButtonsAccessible(string screen)
    {
        for (var i = 0; i < 10; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        var buttons = _window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && b.Bounds.Width > 0)
            // The scrollbar's own arrows aren't controls of ours; Avalonia hides them for touch.
            .Where(b => !b.GetVisualAncestors().OfType<Avalonia.Controls.Primitives.ScrollBar>().Any())
            .ToList();
        Assert.NotEmpty(buttons);
        foreach (var button in buttons)
        {
            var name = AutomationProperties.GetName(button) is { Length: > 0 } n ? n
                : button.Content as string
                ?? button.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            Assert.False(string.IsNullOrWhiteSpace(name), $"{screen}: a button has no accessible name");
            Assert.True(button.Bounds.Height >= 44 && button.Bounds.Width >= 44,
                $"{screen}: '{name}' is {button.Bounds.Width:0}×{button.Bounds.Height:0}, needs 44×44");
        }
    }

    [AvaloniaFact]
    public async Task EveryScreen_ButtonsAreBigEnoughAndNamed()
    {
        await StartAsync();
        PhoneBuds.SetConnected(false);
        _viewModel.ShowMessage("A message with its dismiss button");
        AssertButtonsAccessible("earbuds list");

        _viewModel.OpenCommand.Execute(_viewModel.Devices.Single(d => d.Name == "Simulated Buds Pro"));
        AssertButtonsAccessible("device page");

        _viewModel.ShowComputerCommand.Execute(null);
        _computer.OpenPairing();
        await UntilAsync(() => _viewModel.Computer.HasNearby, "the computer in the nearby list");
        AssertButtonsAccessible("pairing list");

        await PairAsync(() => AssertButtonsAccessible("pairing code"));
        AssertButtonsAccessible("paired computer");
    }

    private static Color Token(string key, ThemeVariant theme)
    {
        Assert.True(Application.Current!.TryGetResource(key, theme, out var value), key);
        return ((ISolidColorBrush)value!).Color;
    }

    private static Color Over(Color top, Color bottom)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte b) => (byte)Math.Round(t * a + b * (1 - a));
        return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    private static double Ratio(Color a, Color b)
    {
        static double L(Color c)
        {
            static double Ch(byte v) => v / 255.0 <= 0.03928 ? v / 255.0 / 12.92 : Math.Pow((v / 255.0 + 0.055) / 1.055, 2.4);
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }

        var (l1, l2) = (L(a), L(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    /// <summary>
    /// The colour pairs the phone adds beyond the shared token checks in the desktop's ContrastTests:
    /// low and critical figures on the page, selected rows and tabs on accent-soft, status bars on their track.
    /// </summary>
    [AvaloniaFact]
    public void PhoneColourPairs_MeetWcagAA_InBothThemes()
    {
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            Color T(string key) => Token(key, theme);
            var checks = new (string What, Color Fore, Color Back, double Min)[]
            {
                ("Low figure on the page", T("Samaat.Warn"), T("Samaat.Bg"), 4.5),
                ("Critical figure on the page", T("Samaat.Critical"), T("Samaat.Bg"), 4.5),
                ("Low words in a device row", T("Samaat.Warn"), T("Samaat.Surface"), 4.5),
                ("Selected option label", T("Samaat.Text"), Over(T("Samaat.AccentSoft"), T("Samaat.Surface")), 4.5),
                // Muted would be 4.45:1 here in dark mode; selected rows switch their description to the text colour.
                ("Selected option description", T("Samaat.Text"), Over(T("Samaat.AccentSoft"), T("Samaat.Surface")), 4.5),
                ("Selected tab label", T("Samaat.Text"), Over(T("Samaat.AccentSoft"), T("Samaat.Rail")), 4.5),
                ("Unselected tab label", T("Samaat.Muted"), T("Samaat.Rail"), 4.5),
                ("Check mark on a selected row", T("Samaat.Accent"), Over(T("Samaat.AccentSoft"), T("Samaat.Surface")), 3.0),
                ("Low alert pill on the page", T("Samaat.Warn"), Over(T("Samaat.WarnSoft"), T("Samaat.Bg")), 4.5),
                ("Critical alert pill on the page", T("Samaat.Critical"), Over(T("Samaat.CriticalSoft"), T("Samaat.Bg")), 4.5),
                ("Critical pill inside a panel", T("Samaat.Critical"), Over(T("Samaat.CriticalSoft"), T("Samaat.Surface")), 4.5),
                ("Low level bar on its track", T("Samaat.Warn"), T("Samaat.Track"), 3.0),
                ("Critical level bar on its track", T("Samaat.Critical"), T("Samaat.Track"), 3.0),
                ("Battery ring on the page", T("Samaat.Accent"), T("Samaat.Bg"), 3.0),
                ("Icon in an option tile", T("Samaat.Muted"), T("Samaat.Surface2"), 3.0),
            };
            foreach (var (what, fore, back, min) in checks)
            {
                var ratio = Ratio(fore, back);
                Assert.True(ratio >= min, $"{what} ({theme}) is {ratio:0.00}:1, needs {min}:1");
            }
        }
    }

    [AvaloniaFact]
    public async Task LowBattery_IsShownInWords_NotJustColour()
    {
        await StartAsync();
        var lite = _viewModel.Devices.Single(d => d.Name == "Simulated Buds Lite");

        // The simulator's Buds Lite start with the right bud at 17 %.
        Assert.True(lite.SummaryIsLow);
        Assert.Equal("Right bud low · 17%", lite.AlertText);
        Assert.Contains(lite.Battery, c => c.IsLow && c.Meta.StartsWith("Low") && c.AccessibleText.Contains("low"));
    }

    [AvaloniaFact]
    public async Task WhileTheComputerHasThem_ConnectGivesWayToBringHere()
    {
        await StartAsync();
        PhoneBuds.SetConnected(false);
        await PairAsync();
        var buds = _viewModel.Devices.Single(d => d.Name == "Simulated Buds Pro");
        await UntilAsync(() => buds.CanBringHere, "Bring to this phone");

        Assert.True(buds.ComputerHasThem);
        Assert.False(buds.ShowConnect);
    }

    [AvaloniaFact]
    public async Task Artwork_UsesTheDesktopsLooksAndPhotos()
    {
        await StartAsync();
        var buds = _viewModel.Devices.Single(d => d.Name == "Simulated Buds Pro");

        // No profile matches the simulator, and it reports each bud: the desktop's "separate pieces" look.
        Assert.True(buds.ShowUnknownPieces);
        Assert.False(buds.ShowRing);
        Assert.Equal(["93", "81", "64"], buds.Pieces.Select(p => p.Value));

        // "Change look" offers the same models with photos as the desktop, from the same devices/ folder.
        Assert.Equal(["Automatic", "Unknown · single", "Unknown · separate pieces"], buds.LookOptions.Take(3).Select(o => o.Label));
        Assert.Contains(buds.LookOptions, o => o.Label == "Audionic ParadoX-E");
        Assert.Contains(buds.LookOptions, o => o.Label == "realme Buds T200 Lite");

        // A model with part photos: left bud, right bud and case, each with its own level.
        buds.LookOptions.Single(o => o.Label == "Audionic ParadoX-E").Select.Execute(null);
        Assert.True(buds.ShowArtworkParts);
        Assert.Equal(3, buds.ArtworkParts.Count);
        Assert.All(buds.ArtworkParts, part => Assert.True(part.Image.Size.Width > 0));
        Assert.Equal("audionic.paradox-e", _runtime.Looks.Get(buds.Device.Identity.Id));
        Assert.True(buds.LookOptions.Single(o => o.Label == "Audionic ParadoX-E").IsSelected);

        // A headset: one photo inside the ring.
        var headset = _viewModel.Devices.Single(d => d.Name == "Simulated Headset");
        headset.LookOptions.Single(o => o.Label == "Microsoft Modern Wireless Headset").Select.Execute(null);
        Assert.True(headset.HasArtworkImage);
        Assert.True(headset.ShowRing);

        // Back to Automatic forgets the choice.
        buds.LookOptions.Single(o => o.Label == "Automatic").Select.Execute(null);
        Assert.Null(_runtime.Looks.Get(buds.Device.Identity.Id));
        Assert.True(buds.ShowUnknownPieces);
    }

    [AvaloniaFact]
    public async Task Header_ShowsTheFixedLogo()
    {
        await StartAsync();

        var logo = _window.GetVisualDescendants().OfType<Image>().Single(i => AutomationProperties.GetName(i) == "Samaat");
        Assert.Same(Samaat.UI.Logo.Image, logo.Source);
    }

    /// <summary>Set SAMAAT_SCREENSHOTS to save phone-sized screenshots (dark and light).</summary>
    [AvaloniaFact]
    public async Task RendersScreenshots()
    {
        var directory = Environment.GetEnvironmentVariable("SAMAAT_SCREENSHOTS");
        void Capture(string name)
        {
            foreach (var (variant, suffix) in new[] { (ThemeVariant.Dark, ""), (ThemeVariant.Light, "-light") })
            {
                Application.Current!.RequestedThemeVariant = variant;
                for (var i = 0; i < 30; i++)
                {
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs();
                }

                var frame = _window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                if (directory is not null)
                {
                    Directory.CreateDirectory(directory);
                    frame!.Save(Path.Combine(directory, $"mobile-{name}{suffix}.png"));
                }
            }

            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }

        await StartAsync();
        PhoneBuds.SetConnected(false);
        Dispatcher.UIThread.RunJobs();
        Capture("devices");

        _viewModel.ShowComputerCommand.Execute(null);
        _computer.OpenPairing();
        await UntilAsync(() => _viewModel.Computer.HasNearby, "the computer in the nearby list");
        Capture("computer-nearby");

        await PairAsync(() => Capture("computer-code"));
        _viewModel.DismissMessageCommand.Execute(null);
        Capture("computer-paired");

        var buds = _viewModel.Devices.Single(d => d.Name == "Simulated Buds Pro");
        _viewModel.OpenCommand.Execute(buds);
        await UntilAsync(() => buds.CanBringHere, "Bring to this phone");
        Capture("device");

        await buds.BringHereCommand.ExecuteAsync(null);
        await buds.PendingMove;
        await UntilAsync(() => buds.CanSendToComputer, "Move to Desk PC");
        Capture("device-moved");

        // The desktop's artwork: a model with part photos, and a headset photo in the ring.
        buds.LookOptions.Single(o => o.Label == "Audionic ParadoX-E").Select.Execute(null);
        Capture("device-artwork");
        var headset = _viewModel.Devices.Single(d => d.Name == "Simulated Headset");
        headset.LookOptions.Single(o => o.Label == "Microsoft Modern Wireless Headset").Select.Execute(null);
        _viewModel.OpenCommand.Execute(headset);
        Capture("device-photo");
    }

    public async ValueTask DisposeAsync()
    {
        _window.Close();
        _viewModel.Dispose();
        await _runtime.DisposeAsync();
        await _computer.DisposeAsync();
        await _computerDevices.DisposeAsync();
    }
}

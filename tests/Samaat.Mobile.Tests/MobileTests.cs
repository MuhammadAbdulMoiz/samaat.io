using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
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
        Assert.Equal(["Left", "Right", "Case"], buds.Battery.Select(b => b.Label));
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

using System.Net;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Samaat.App.Features.Phone;
using Samaat.App.Shell;
using Samaat.Core.Commands;
using Samaat.Core.Devices;
using Samaat.Core.Settings;
using Samaat.Handoff;
using Samaat.Handoff.Discovery;
using Samaat.Handoff.Moving;
using Samaat.Handoff.Protocol;
using Samaat.Handoff.Storage;

namespace Samaat.App.Tests;

/// <summary>The phone's Bluetooth audio, in memory.</summary>
internal sealed class FakePhoneAudio : IPhoneAudioLink
{
    private volatile IReadOnlyList<PhoneAudioDevice> _devices = [];

    public bool IsBluetoothOn => true;

    public string? Blocker => null;

    public IReadOnlyList<PhoneAudioDevice> Devices => _devices;

    public CommandResult NextConnect { get; set; } = CommandResult.Confirmed;

    public event EventHandler? Changed;

    public FakePhoneAudio With(string address, bool connected)
    {
        _devices = [.. _devices.Where(d => d.Address != address), new PhoneAudioDevice(address, "Simulated Buds Pro", connected)];
        Changed?.Invoke(this, EventArgs.Empty);
        return this;
    }

    public Task<CommandResult> ConnectAsync(string address, TimeSpan timeout, CancellationToken ct)
    {
        if (NextConnect.Succeeded)
        {
            With(address, true);
        }

        return Task.FromResult(NextConnect);
    }

    public Task<CommandResult> DisconnectAsync(string address, TimeSpan timeout, CancellationToken ct)
    {
        With(address, false);
        return Task.FromResult(CommandResult.Confirmed);
    }
}

public class PhoneTargetTests
{
    private static readonly PeerDevice Buds = new("AA", "Buds", Connected: false, true, true);

    private static PhoneTargetViewModel Target(bool online = true, PeerDevice? onPhone = null, bool here = true, bool canLink = true, bool busy = false) =>
        new(new PhoneStatus("p", "Pixel 8", online, []), onPhone, here, canLink, busy, (_, _) => Task.CompletedTask);

    [Fact]
    public void Offline_NoButtons_SaysHowToFix()
    {
        var target = Target(online: false, onPhone: Buds);

        Assert.False(target.CanMoveThere);
        Assert.False(target.CanBringHere);
        Assert.Contains("Open Samaat on Pixel 8", target.StateText);
    }

    [Fact]
    public void ConnectedHere_CanMoveToPhone()
    {
        var target = Target(onPhone: Buds);

        Assert.True(target.CanMoveThere);
        Assert.False(target.CanBringHere);
        Assert.Equal("Move to Pixel 8", target.MoveText);
    }

    [Fact]
    public void PlayingOnPhone_CanBringBack()
    {
        var target = Target(onPhone: Buds with { Connected = true }, here: false);

        Assert.False(target.CanMoveThere);
        Assert.True(target.CanBringHere);
        Assert.Equal("Playing on Pixel 8", target.StateText);
    }

    [Fact]
    public void NotPairedWithPhone_Explains()
    {
        var target = Target(onPhone: null);

        Assert.False(target.CanMoveThere);
        Assert.Contains("aren't paired with Pixel 8", target.StateText);
    }

    [Fact]
    public void NoPcLink_Explains()
    {
        var target = Target(onPhone: Buds, canLink: false);

        Assert.False(target.CanMoveThere);
        Assert.Contains("can't connect or disconnect", target.StateText);
    }

    [Fact]
    public void Busy_DisablesBoth()
    {
        var target = Target(onPhone: Buds, busy: true);

        Assert.False(target.CanMoveThere);
        Assert.False(target.CanBringHere);
    }

    [Fact]
    public void ConnectedNowhere_CanDoEither()
    {
        var target = Target(onPhone: Buds, here: false);

        Assert.True(target.CanMoveThere);
        Assert.True(target.CanBringHere);
    }
}

/// <summary>The desktop app against a real phone client over loopback: pairing in Settings, then moving from the Links page.</summary>
public sealed class PhoneLinkFlowTests
{
    private const string BudsProAddress = "02:00:5E:10:00:01";

    private readonly InMemoryBeaconBus _bus = new();
    private readonly FakePhoneAudio _phoneAudio = new FakePhoneAudio().With(BudsProAddress, connected: false);

    private async Task<(MainWindow Window, MainWindowViewModel ViewModel, PhoneLinkClient Phone)> ShowAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"));
        var viewModel = new MainWindowViewModel(new AppOptions(Simulate: true, IncludeNonAudio: false),
            settings: new SettingsStore(Path.Combine(dir, "settings.json")),
            phoneLinkFactory: devices => new PhoneLinkHost(new PeerStore(Path.Combine(dir, "phones.json")),
                new PhoneLinkHostOptions("Desk PC") { BindAddress = IPAddress.Loopback, Port = 0, BeaconInterval = TimeSpan.FromMilliseconds(100) },
                devices, _bus.CreateSender()));
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        await viewModel.RefreshDevicesAsync();
        Dispatcher.UIThread.RunJobs();
        var phone = new PhoneLinkClient(new PeerStore(null), "Pixel 8", new PhoneAgent(_phoneAudio), _bus.CreateListener(),
            options: new PhoneLinkClientOptions { RetryMin = TimeSpan.FromMilliseconds(100), RetryMax = TimeSpan.FromMilliseconds(500) });
        phone.Start();
        return (window, viewModel, phone);
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

    private static void Settle()
    {
        for (var i = 0; i < 60; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task PairAsync(MainWindowViewModel viewModel, PhoneLinkClient phone, Action? whilePrompted = null)
    {
        viewModel.Settings.PhoneLink!.PairCommand.Execute(null);
        Assert.True(viewModel.PhoneLink.ShowWaiting);
        await UntilAsync(() => phone.PairableComputers.Count == 1, "the phone to hear the computer");
        var endpoint = phone.PairableComputers[0].Endpoint;
        var session = await phone.BeginPairingAsync(endpoint);
        await UntilAsync(() => viewModel.PhoneLink.HasRequest, "the pairing prompt");
        Assert.Equal(session.Code, viewModel.PhoneLink.Request!.Code);
        Assert.Equal("Pixel 8 wants to pair", viewModel.PhoneLink.Request.Title);
        whilePrompted?.Invoke();

        var phoneSide = phone.FinishPairingAsync(session, true, endpoint: endpoint);
        await viewModel.PhoneLink.Request.AcceptCommand.ExecuteAsync(null);
        Assert.True(await phoneSide);
        await UntilAsync(() => viewModel.PhoneLink.Phones.SingleOrDefault()?.IsOnline == true, "the phone to come online");
    }

    [AvaloniaFact]
    public async Task PairInSettings_ThenMoveToPhoneAndBack()
    {
        var (window, viewModel, phone) = await ShowAsync();
        try
        {
            await PairAsync(viewModel, phone);
            Assert.False(viewModel.PhoneLink.HasRequest);
            Assert.False(viewModel.PhoneLink.IsPairingOpen);
            Assert.Contains(viewModel.Alerts, a => a.Contains("Pixel 8 is paired"));

            var buds = viewModel.Devices.Single(d => d.Name == "Simulated Buds Pro");
            var move = buds.PhoneMove!;
            await UntilAsync(() => move.Targets.SingleOrDefault()?.CanMoveThere == true, "the Move button");
            Assert.Equal("Pixel 8 can take these earbuds", move.Targets[0].StateText);

            await move.Targets[0].MoveCommand.ExecuteAsync(null);
            await move.PendingMove;

            Assert.Equal("Simulated Buds Pro moved to Pixel 8.", move.ResultText);
            Assert.False(move.ResultIsError);
            Assert.Equal(ConnectionState.Disconnected, buds.Device.Connection);
            Assert.True(_phoneAudio.Devices.Single().Connected);
            await UntilAsync(() => move.Targets[0].CanBringHere, "Bring back here");
            Assert.Equal("Playing on Pixel 8", move.Targets[0].StateText);

            await move.Targets[0].BringHereCommand.ExecuteAsync(null);
            await move.PendingMove;

            Assert.Equal("Simulated Buds Pro moved to this computer.", move.ResultText);
            Assert.Equal(ConnectionState.Connected, buds.Device.Connection);
            Assert.False(_phoneAudio.Devices.Single().Connected);
        }
        finally
        {
            await phone.DisposeAsync();
            window.Close();
            await viewModel.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task PhoneFails_ResultSaysTheyAreBack_AndToasts()
    {
        var (window, viewModel, phone) = await ShowAsync();
        try
        {
            await PairAsync(viewModel, phone);
            _phoneAudio.NextConnect = CommandResult.Rejected("You didn't pick the earbuds.");
            var buds = viewModel.Devices.Single(d => d.Name == "Simulated Buds Pro");
            var move = buds.PhoneMove!;
            await UntilAsync(() => move.Targets.SingleOrDefault()?.CanMoveThere == true, "the Move button");

            await move.Targets[0].MoveCommand.ExecuteAsync(null);
            await move.PendingMove;

            Assert.True(move.ResultIsError);
            Assert.Contains("back on this computer", move.ResultText);
            Assert.Equal(ConnectionState.Connected, buds.Device.Connection);
            Assert.Contains(viewModel.Alerts, a => a.Contains("You didn't pick the earbuds."));
        }
        finally
        {
            await phone.DisposeAsync();
            window.Close();
            await viewModel.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task NoPhone_LinksCardOffersSetup_AndOpensSettings()
    {
        var (window, viewModel, phone) = await ShowAsync();
        try
        {
            var move = viewModel.SelectedDevice!.PhoneMove!;

            Assert.True(move.ShowSetup);
            move.OpenSettingsCommand.Execute(null);
            Assert.Equal(AppSection.Settings, viewModel.Section);
            Assert.False(viewModel.PhoneLink.Host.IsListening);
        }
        finally
        {
            await phone.DisposeAsync();
            window.Close();
            await viewModel.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task Forget_RemovesPhone_AndStopsListening()
    {
        var (window, viewModel, phone) = await ShowAsync();
        try
        {
            await PairAsync(viewModel, phone);

            viewModel.PhoneLink.Phones[0].ForgetCommand.Execute(null);

            Assert.False(viewModel.PhoneLink.HasPhones);
            Assert.False(viewModel.PhoneLink.Host.IsListening);
            await UntilAsync(() => phone.State != ComputerLinkState.Online, "the phone to drop");
        }
        finally
        {
            await phone.DisposeAsync();
            window.Close();
            await viewModel.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task CancelPairing_ClosesTheWindow()
    {
        var (window, viewModel, phone) = await ShowAsync();
        try
        {
            viewModel.PhoneLink.PairCommand.Execute(null);
            Assert.True(viewModel.PhoneLink.IsPairingOpen);

            await viewModel.PhoneLink.CancelPairingCommand.ExecuteAsync(null);

            Assert.False(viewModel.PhoneLink.IsPairingOpen);
            Assert.True(viewModel.PhoneLink.ShowPairButton);
        }
        finally
        {
            await phone.DisposeAsync();
            window.Close();
            await viewModel.DisposeAsync();
        }
    }

    /// <summary>Set SAMAAT_SCREENSHOTS to save: the pairing prompt, Settings with a paired phone, and the Links page card.</summary>
    [AvaloniaFact]
    public async Task RendersPhoneScreenshots()
    {
        var (window, viewModel, phone) = await ShowAsync();
        var directory = Environment.GetEnvironmentVariable("SAMAAT_SCREENSHOTS");

        void Capture(string name)
        {
            foreach (var (variant, suffix) in new[] { (ThemeVariant.Dark, ""), (ThemeVariant.Light, "-light") })
            {
                Application.Current!.RequestedThemeVariant = variant;
                Settle();
                var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                if (directory is not null)
                {
                    Directory.CreateDirectory(directory);
                    frame!.Save(Path.Combine(directory, $"{name}{suffix}.png"));
                }
            }

            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }

        try
        {
            viewModel.NavigateCommand.Execute(AppSection.Settings);
            await PairAsync(viewModel, phone, () => Capture("settings-phone-pairing"));
            viewModel.Alerts.Clear();
            Capture("settings-phone");

            var buds = viewModel.Devices.Single(d => d.Name == "Simulated Buds Pro");
            viewModel.SelectDeviceCommand.Execute(buds);
            viewModel.NavigateCommand.Execute(AppSection.Connections);
            await UntilAsync(() => buds.PhoneMove!.Targets.SingleOrDefault()?.CanMoveThere == true, "the Move button");
            Capture("buds-pro-connections-phone");

            await buds.PhoneMove!.Targets[0].MoveCommand.ExecuteAsync(null);
            await buds.PhoneMove.PendingMove;
            await UntilAsync(() => buds.PhoneMove.Targets[0].CanBringHere, "Bring back here");
            viewModel.Alerts.Clear();
            Capture("buds-pro-connections-phone-moved");
        }
        finally
        {
            await phone.DisposeAsync();
            window.Close();
            await viewModel.DisposeAsync();
        }
    }
}

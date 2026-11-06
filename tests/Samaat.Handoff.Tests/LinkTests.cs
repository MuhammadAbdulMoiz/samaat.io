using System.Net;
using System.Security.Cryptography;
using Samaat.Core.Commands;
using Samaat.Core.Devices;
using Samaat.Handoff.Discovery;
using Samaat.Handoff.Link;
using Samaat.Handoff.Moving;
using Samaat.Handoff.Pairing;
using Samaat.Handoff.Protocol;
using Samaat.Handoff.Security;
using Samaat.Handoff.Storage;

namespace Samaat.Handoff.Tests;

public class PairingTests
{
    private static readonly TimeSpan Answer = TimeSpan.FromSeconds(5);

    private static async Task<(PairingSession Phone, PairingSession Computer)> PairAsync(LoopbackPair pair)
    {
        var phone = Handshake.PairAsPhoneAsync(pair.Client, "phone-1", "Pixel");
        var start = await Handshake.ReadStepAsync(pair.Server, default);
        var computer = await Handshake.PairAsComputerAsync(pair.Server, start, "pc-1", "Desk PC");
        return (await phone, computer);
    }

    [Fact]
    public async Task BothAccept_SameCodeAndSecret_Paired()
    {
        await using var pair = await LoopbackPair.CreateAsync();
        var (phone, computer) = await PairAsync(pair);

        Assert.Equal(phone.Code, computer.Code);
        Assert.Matches("^[0-9]{3} [0-9]{3}$", phone.Code);
        Assert.Equal(phone.Secret, computer.Secret);
        Assert.Equal("Desk PC", phone.PeerName);
        Assert.Equal("pc-1", phone.PeerId);
        Assert.Equal("Pixel", computer.PeerName);

        var results = await Task.WhenAll(phone.CompleteAsync(true, Answer), computer.CompleteAsync(true, Answer));

        Assert.Equal([true, true], results);
    }

    [Fact]
    public async Task ComputerDeclines_PhoneNotPaired()
    {
        await using var pair = await LoopbackPair.CreateAsync();
        var (phone, computer) = await PairAsync(pair);

        var results = await Task.WhenAll(phone.CompleteAsync(true, Answer), computer.CompleteAsync(false, Answer));

        Assert.Equal([false, false], results);
    }

    [Fact]
    public async Task PhoneDeclines_ComputerSeesItBeforeAnswering()
    {
        await using var pair = await LoopbackPair.CreateAsync();
        var (phone, computer) = await PairAsync(pair);

        await phone.CompleteAsync(false, Answer);

        Assert.False(await computer.PeerDecision.WaitAsync(Answer));
    }

    [Fact]
    public async Task AnsweringTwice_Throws()
    {
        await using var pair = await LoopbackPair.CreateAsync();
        var (phone, computer) = await PairAsync(pair);
        await computer.CompleteAsync(false, Answer);

        await Assert.ThrowsAsync<InvalidOperationException>(() => computer.CompleteAsync(true, Answer));
    }

    [Fact]
    public async Task RevealDoesNotMatchCommitment_Rejected()
    {
        await using var pair = await LoopbackPair.CreateAsync();
        using var promised = new PairingKey();
        using var revealed = new PairingKey();
        var nonce = KeyDerivation.NewNonce();

        // A phone (or a man in the middle) that commits to one key and then reveals another.
        var computer = Task.Run(async () =>
        {
            var start = await Handshake.ReadStepAsync(pair.Server, default);
            return await Handshake.PairAsComputerAsync(pair.Server, start, "pc-1", "Desk PC");
        });
        await HandshakeFrame.SendAsync(pair.Client, new HandshakeFrame(HandshakeFrame.PairStart)
        {
            PeerId = "phone-1",
            Name = "Pixel",
            Commitment = KeyDerivation.Commitment(promised.PublicKey, nonce),
        }, default);
        await Handshake.ReadStepAsync(pair.Client, default);
        await HandshakeFrame.SendAsync(pair.Client, new HandshakeFrame(HandshakeFrame.PairReveal) { PublicKey = revealed.PublicKey, Nonce = nonce }, default);

        var error = await Assert.ThrowsAsync<HandshakeException>(() => computer);
        Assert.Contains("didn't match", error.Message);
    }

    [Fact]
    public async Task Refused_SurfacesTheComputersReason()
    {
        await using var pair = await LoopbackPair.CreateAsync();
        var phone = Handshake.PairAsPhoneAsync(pair.Client, "phone-1", "Pixel");
        await Handshake.ReadStepAsync(pair.Server, default);
        await Handshake.RefuseAsync(pair.Server, "Pairing isn't open.");

        var error = await Assert.ThrowsAsync<HandshakeException>(() => phone);
        Assert.True(error.Refused);
        Assert.Equal("Pairing isn't open.", error.Message);
    }
}

public class SessionTests
{
    private static readonly byte[] Secret = RandomNumberGenerator.GetBytes(32);

    private static async Task<(PeerChannel Phone, PeerChannel Computer)> OpenAsync(LoopbackPair pair, byte[]? phoneSecret = null)
    {
        var phone = Handshake.ConnectAsPhoneAsync(pair.Client, "phone-1", "pc-1", phoneSecret ?? Secret, new HelloMessage("Pixel", "android", 1));
        var hello = await Handshake.ReadStepAsync(pair.Server, default);
        var computer = Handshake.AcceptAsComputerAsync(pair.Server, hello, "pc-1", Secret, new HelloMessage("Desk PC", "windows", 1));
        return (await phone, await computer);
    }

    [Fact]
    public async Task SameSecret_ExchangesHellos()
    {
        await using var pair = await LoopbackPair.CreateAsync();
        var (phone, computer) = await OpenAsync(pair);

        Assert.Equal("Desk PC", phone.PeerHello.Name);
        Assert.Equal("Pixel", computer.PeerHello.Name);
        await phone.DisposeAsync();
        await computer.DisposeAsync();
    }

    [Fact]
    public async Task WrongSecret_Refused()
    {
        await using var pair = await LoopbackPair.CreateAsync();

        var error = await Assert.ThrowsAsync<HandshakeException>(() => OpenAsync(pair, RandomNumberGenerator.GetBytes(32)));
        Assert.Contains("pair again", error.Message);
    }

    [Fact]
    public async Task WrongComputerAnswers_Refused()
    {
        await using var pair = await LoopbackPair.CreateAsync();
        var phone = Handshake.ConnectAsPhoneAsync(pair.Client, "phone-1", "pc-EXPECTED", Secret, new HelloMessage("Pixel", "android", 1));
        var hello = await Handshake.ReadStepAsync(pair.Server, default);
        _ = Handshake.AcceptAsComputerAsync(pair.Server, hello, "pc-OTHER", Secret, new HelloMessage("Other", "windows", 1));

        var error = await Assert.ThrowsAsync<HandshakeException>(() => phone);
        Assert.Contains("different computer", error.Message);
    }

    [Fact]
    public async Task Request_GetsHandlerReply()
    {
        await using var pair = await LoopbackPair.CreateAsync();
        var (phone, computer) = await OpenAsync(pair);
        phone.Start((m, _) => Task.FromResult<ReplyMessage?>(m is PrepareMessage p ? new ReplyMessage(0, CommandStatus.Rejected, "no " + p.Address) : null));
        computer.Start((_, _) => Task.FromResult<ReplyMessage?>(null));

        var reply = await computer.RequestAsync(new PrepareMessage("AA", HandoffDirection.ToPhone), TimeSpan.FromSeconds(5));

        Assert.Equal(CommandStatus.Rejected, reply.Status);
        Assert.Equal("no AA", reply.Message);
        await phone.DisposeAsync();
        await computer.DisposeAsync();
    }

    [Fact]
    public async Task Ping_AnsweredByChannel()
    {
        await using var pair = await LoopbackPair.CreateAsync();
        var (phone, computer) = await OpenAsync(pair);
        phone.Start((_, _) => Task.FromResult<ReplyMessage?>(null));
        computer.Start((_, _) => Task.FromResult<ReplyMessage?>(null));

        Assert.Equal(CommandStatus.Confirmed, (await computer.RequestAsync(new PingMessage(), TimeSpan.FromSeconds(5))).Status);
        await phone.DisposeAsync();
        await computer.DisposeAsync();
    }

    [Fact]
    public async Task UnhandledRequest_Unsupported()
    {
        await using var pair = await LoopbackPair.CreateAsync();
        var (phone, computer) = await OpenAsync(pair);
        phone.Start((_, _) => Task.FromResult<ReplyMessage?>(null));
        computer.Start((_, _) => Task.FromResult<ReplyMessage?>(null));

        Assert.Equal(CommandStatus.Unsupported, (await computer.RequestAsync(new ConnectAudioMessage("AA"), TimeSpan.FromSeconds(5))).Status);
        await phone.DisposeAsync();
        await computer.DisposeAsync();
    }

    [Fact]
    public async Task SlowRequest_DoesNotBlockNotifications()
    {
        await using var pair = await LoopbackPair.CreateAsync();
        var (phone, computer) = await OpenAsync(pair);
        var release = new TaskCompletionSource();
        var lists = 0;
        phone.Start(async (m, _) =>
        {
            if (m is DeviceListMessage)
            {
                Interlocked.Increment(ref lists);
                return null;
            }

            await release.Task;
            return new ReplyMessage(0, CommandStatus.Confirmed);
        });
        computer.Start((_, _) => Task.FromResult<ReplyMessage?>(null));

        var slow = computer.RequestAsync(new ConnectAudioMessage("AA"), TimeSpan.FromSeconds(10));
        await computer.SendAsync(new DeviceListMessage([]));
        await Eventually.TrueAsync(() => Volatile.Read(ref lists) == 1, "the device list to arrive during the slow request");
        release.SetResult();

        Assert.Equal(CommandStatus.Confirmed, (await slow).Status);
        await phone.DisposeAsync();
        await computer.DisposeAsync();
    }

    [Fact]
    public async Task OtherSideCloses_ClosedRaised_AndRequestsFail()
    {
        await using var pair = await LoopbackPair.CreateAsync();
        var (phone, computer) = await OpenAsync(pair);
        var closed = new TaskCompletionSource();
        computer.Closed += (_, _) => closed.TrySetResult();
        phone.Start((_, _) => Task.FromResult<ReplyMessage?>(null));
        computer.Start((_, _) => Task.FromResult<ReplyMessage?>(null));

        await phone.DisposeAsync();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(computer.IsClosed);
        Assert.Equal(CommandStatus.Failed, (await computer.RequestAsync(new PingMessage(), TimeSpan.FromSeconds(1))).Status);
        await computer.DisposeAsync();
    }

    [Fact]
    public async Task NoReply_TimesOut()
    {
        await using var pair = await LoopbackPair.CreateAsync();
        var (phone, computer) = await OpenAsync(pair);
        phone.Start(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        });
        computer.Start((_, _) => Task.FromResult<ReplyMessage?>(null));

        var reply = await computer.RequestAsync(new ConnectAudioMessage("AA"), TimeSpan.FromMilliseconds(200));

        Assert.Equal(CommandStatus.TimedOut, reply.Status);
        await phone.DisposeAsync();
        await computer.DisposeAsync();
    }
}

/// <summary>The whole thing over loopback: the computer's host and the phone's client, pairing and moving both ways.</summary>
public sealed class EndToEndTests : IAsyncDisposable
{
    private const string Address = FakeLinkDevice.DefaultAddress;

    private readonly InMemoryBeaconBus _bus = new();
    private readonly FakeLinkDevice _buds = new();
    private readonly FakePhoneAudio _phoneAudio = new FakePhoneAudio().With(Address, connected: false);
    private readonly PeerStore _computerStore = new(null);
    private readonly PeerStore _phoneStore = new(null);
    private readonly PhoneLinkHost _host;
    private readonly PhoneLinkClient _client;

    public EndToEndTests()
    {
        _host = new PhoneLinkHost(_computerStore,
            new PhoneLinkHostOptions("Desk PC") { BindAddress = IPAddress.Loopback, Port = 0, BeaconInterval = TimeSpan.FromMilliseconds(100) },
            () => [_buds], _bus.CreateSender());
        _client = new PhoneLinkClient(_phoneStore, "Pixel", new PhoneAgent(_phoneAudio), _bus.CreateListener(),
            options: new PhoneLinkClientOptions { RetryMin = TimeSpan.FromMilliseconds(100), RetryMax = TimeSpan.FromMilliseconds(500) });
    }

    private async Task PairAsync()
    {
        _host.Start();
        _client.Start();
        PairingRequest? request = null;
        _host.PairingRequested += (_, r) => request = r;
        _host.OpenPairing();
        await Eventually.TrueAsync(() => _client.PairableComputers.Count == 1, "the computer to be heard");
        var computer = _client.PairableComputers[0];
        Assert.Equal("Desk PC", computer.Name);

        var session = await _client.BeginPairingAsync(computer.Endpoint);
        await Eventually.TrueAsync(() => request is not null, "the pairing prompt on the computer");
        Assert.Equal(session.Code, request!.Code);
        Assert.Equal("Pixel", request.PhoneName);

        var results = await Task.WhenAll(_client.FinishPairingAsync(session, true, endpoint: computer.Endpoint), request.AcceptAsync());
        Assert.Equal([true, true], results);
        await Eventually.TrueAsync(() => _client.State == ComputerLinkState.Online, "the phone to connect");
        await Eventually.TrueAsync(() => _host.Phones.SingleOrDefault()?.Online == true, "the computer to see the phone");
    }

    [Fact]
    public async Task Pair_ThenBothSidesSeeEachOthersEarbuds()
    {
        await PairAsync();

        Assert.False(_host.IsPairingOpen);
        Assert.Equal("Desk PC", _client.Computer?.Name);
        await Eventually.TrueAsync(() => _host.Phones[0].FindDevice(Address) is not null, "the phone's list on the computer");
        await Eventually.TrueAsync(() => _client.ComputerDevices.Any(d => d.Address == Address && d.Connected), "the computer's list on the phone");
    }

    [Fact]
    public async Task MoveToPhone_FromComputer()
    {
        await PairAsync();
        await Eventually.TrueAsync(() => _host.Phones[0].FindDevice(Address) is not null, "the phone's list");
        var phoneProgress = new List<HandoffStage>();
        _client.Progress += (_, p) => { lock (phoneProgress) { phoneProgress.Add(p.Stage); } };

        var result = await _host.MoveAsync(_buds, _host.Phones[0].Id, HandoffDirection.ToPhone);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(ConnectionState.Disconnected, _buds.Connection);
        Assert.True(_phoneAudio.Devices.Single().Connected);
        await Eventually.TrueAsync(() => { lock (phoneProgress) { return phoneProgress.Contains(HandoffStage.Done); } }, "progress on the phone");
    }

    [Fact]
    public async Task MoveToComputer_FromPhone()
    {
        _buds.Connection = ConnectionState.Disconnected;
        _phoneAudio.With(Address, connected: true);
        await PairAsync();
        await Eventually.TrueAsync(() => _host.Phones[0].FindDevice(Address)?.Connected == true, "the phone's list");
        var activity = new List<HandoffStage>();
        _host.Activity += (_, a) => { lock (activity) { activity.Add(a.Progress.Stage); } };

        var result = await _client.RequestMoveAsync(Address, HandoffDirection.ToComputer);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(HandoffOutcome.Moved, result.Outcome);
        Assert.Equal(ConnectionState.Connected, _buds.Connection);
        Assert.False(_phoneAudio.Devices.Single().Connected);
        lock (activity)
        {
            Assert.Contains(HandoffStage.Releasing, activity);
        }
    }

    [Fact]
    public async Task PhoneCantConnect_ComputerTakesThemBack()
    {
        await PairAsync();
        await Eventually.TrueAsync(() => _host.Phones[0].FindDevice(Address) is not null, "the phone's list");
        _phoneAudio.ConnectResults.Enqueue(CommandResult.Rejected("You didn't pick the earbuds."));

        var result = await _host.MoveAsync(_buds, _host.Phones[0].Id, HandoffDirection.ToPhone);

        Assert.Equal(HandoffOutcome.RolledBack, result.Outcome);
        Assert.Contains("You didn't pick the earbuds.", result.Message);
        Assert.Equal(ConnectionState.Connected, _buds.Connection);
    }

    [Fact]
    public async Task PhoneStartsMove_BluetoothTurnsOffOnPhone_ComputerKeepsTheEarbuds()
    {
        await PairAsync();
        await Eventually.TrueAsync(() => _host.Phones[0].FindDevice(Address) is not null, "the phone's list");
        _phoneAudio.HangConnect = true;

        var move = _client.RequestMoveAsync(Address, HandoffDirection.ToPhone);
        await Eventually.TrueAsync(() => _phoneAudio.Calls.Count == 1, "the phone to start connecting");
        _phoneAudio.TurnBluetoothOff();
        var result = await move;

        Assert.Equal(HandoffOutcome.RolledBack, result.Outcome);
        Assert.Contains("kept them on this computer", result.Message);
        Assert.Contains("Bluetooth turned off", result.Message);
        Assert.Equal(ConnectionState.Connected, _buds.Connection);
    }

    [Fact]
    public async Task PhoneStartsMove_ThenDropsTheSession_ComputerReconnectsTheEarbuds()
    {
        await PairAsync();
        await Eventually.TrueAsync(() => _host.Phones[0].FindDevice(Address) is not null, "the phone's list");
        _phoneAudio.HangConnect = true;
        var done = false;
        _host.Activity += (_, a) => done |= a.Progress.Stage == HandoffStage.Done;

        _ = _client.RequestMoveAsync(Address, HandoffDirection.ToPhone).ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
        await Eventually.TrueAsync(() => _phoneAudio.Calls.Count == 1, "the phone to start connecting");
        await _client.DisposeAsync();

        await Eventually.TrueAsync(() => _buds.Connection == ConnectionState.Connected && _buds.Calls.Contains("pc.connect") && done,
            "the computer to take the earbuds back");
    }

    [Fact]
    public async Task PhoneAsksForUnknownEarbuds_Refused()
    {
        await PairAsync();

        var result = await _client.RequestMoveAsync("11:22:33:44:55:66", HandoffDirection.ToPhone);

        Assert.Equal(HandoffOutcome.NotStarted, result.Outcome);
        Assert.Contains("doesn't have these earbuds", result.Message);
    }

    [Fact]
    public async Task Move_WhenPhoneOffline_NotStarted()
    {
        _computerStore.Add(new PairedPeer("ghost", "Old phone", [1], DateTimeOffset.UnixEpoch));

        var result = await _host.MoveAsync(_buds, "ghost", HandoffDirection.ToPhone);

        Assert.Equal(HandoffOutcome.NotStarted, result.Outcome);
        Assert.Contains("isn't connected", result.Message);
    }

    [Fact]
    public async Task PairingClosed_PhoneIsRefused()
    {
        _computerStore.Add(new PairedPeer("someone", "Other phone", [1], DateTimeOffset.UnixEpoch));
        _host.Start();
        await Eventually.TrueAsync(() => _host.Port is not null, "the computer to listen");

        var error = await Assert.ThrowsAsync<HandshakeException>(() => _client.BeginPairingAsync(new IPEndPoint(IPAddress.Loopback, _host.Port!.Value)));
        Assert.Contains("Pair a phone", error.Message);
    }

    [Fact]
    public async Task ComputerForgetsPhone_PhoneGoesOffline()
    {
        await PairAsync();

        _host.Forget(_host.Phones[0].Id);

        await Eventually.TrueAsync(() => _client.State != ComputerLinkState.Online, "the phone to drop");
        Assert.Empty(_host.Phones);
        Assert.False(_host.IsListening);
    }

    [Fact]
    public async Task ComputerRestarts_PhoneReconnects()
    {
        await PairAsync();
        var port = _host.Port!.Value;
        await _host.DisposeAsync();
        await Eventually.TrueAsync(() => _client.State != ComputerLinkState.Online, "the phone to notice");

        await using var again = new PhoneLinkHost(_computerStore,
            new PhoneLinkHostOptions("Desk PC") { BindAddress = IPAddress.Loopback, Port = port, BeaconInterval = TimeSpan.FromMilliseconds(100) },
            () => [_buds], _bus.CreateSender());
        again.Start();

        await Eventually.TrueAsync(() => _client.State == ComputerLinkState.Online, "the phone to reconnect", 15_000);
    }

    [Fact]
    public void Host_DoesNotListen_UntilPairedOrPairing()
    {
        _host.Start();
        Assert.False(_host.IsListening);

        _host.OpenPairing();
        Assert.True(_host.IsListening);

        _host.ClosePairing();
        Assert.False(_host.IsListening);
    }

    [Fact]
    public async Task AfterAMove_PhoneSeesTheComputersNewState()
    {
        await PairAsync();
        await Eventually.TrueAsync(() => _host.Phones[0].FindDevice(Address) is not null, "the phone's list");

        await _client.RequestMoveAsync(Address, HandoffDirection.ToPhone);

        await Eventually.TrueAsync(() => _client.ComputerDevices.Single(d => d.Address == Address).Connected == false, "the computer's new state on the phone");
    }

    [Fact]
    public async Task PausedAndReopened_ListensOnTheSamePort()
    {
        _host.Start();
        _host.OpenPairing();
        var port = _host.Port;
        _host.ClosePairing();

        _host.OpenPairing();

        Assert.Equal(port, _host.Port);
    }

    [Fact]
    public async Task PhoneDevicesChange_ComputerHears()
    {
        await PairAsync();

        _phoneAudio.With("11:22:33:44:55:66", connected: true, "Other Buds");

        await Eventually.TrueAsync(() => _host.Phones[0].FindDevice("11:22:33:44:55:66")?.Connected == true, "the new list on the computer");
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        await _host.DisposeAsync();
    }
}

using Microsoft.Extensions.Time.Testing;
using System.Net.Sockets;
using Samaat.Core.Commands;
using Samaat.Core.Devices;
using Samaat.Core.Diagnostics;
using Samaat.Handoff.Moving;
using Samaat.Handoff.Protocol;

namespace Samaat.Handoff.Tests;

public class CoordinatorTests
{
    private const string Address = FakeLinkDevice.DefaultAddress;

    private readonly List<string> _calls = [];
    private readonly FakeLinkDevice _device = new();
    private readonly FakePhoneEndpoint _phone;

    public CoordinatorTests()
    {
        _phone = new FakePhoneEndpoint(_calls);
        _phone.Devices[Address] = new PeerDevice(Address, "Test Buds", Connected: false, true, true);
    }

    private List<string> AllCalls => [.. _calls, .. _device.Calls];

    private Task<HandoffResult> Run(HandoffDirection direction, IProgress<HandoffProgress>? progress = null, TimeProvider? time = null, CancellationToken ct = default) =>
        new HandoffCoordinator(time).RunAsync(_device, _phone, direction, progress, ct);

    [Fact]
    public async Task ToPhone_HappyPath_ChecksDisconnectsThenConnects()
    {
        var result = await Run(HandoffDirection.ToPhone);

        Assert.Equal(HandoffOutcome.Moved, result.Outcome);
        Assert.Equal("Test Buds moved to Test Phone.", result.Message);
        Assert.Equal(["phone.prepare", "phone.connect"], _calls);
        Assert.Equal(["pc.disconnect"], _device.Calls);
        Assert.Equal(ConnectionState.Disconnected, _device.Connection);
    }

    [Fact]
    public async Task ToPhone_PhoneRefuses_NothingChanges()
    {
        _phone.PrepareResults.Enqueue(CommandResult.Rejected("Bluetooth is off on the phone."));

        var result = await Run(HandoffDirection.ToPhone);

        Assert.Equal(HandoffOutcome.NotStarted, result.Outcome);
        Assert.Contains("Bluetooth is off", result.Message);
        Assert.Empty(_device.Calls);
        Assert.Equal(ConnectionState.Connected, _device.Connection);
    }

    [Fact]
    public async Task ToPhone_DisconnectFails_PhoneNotAsked()
    {
        _device.DisconnectResults.Enqueue(CommandResult.Rejected("Windows refused the request (0x80004005)."));

        var result = await Run(HandoffDirection.ToPhone);

        Assert.Equal(HandoffOutcome.NotStarted, result.Outcome);
        Assert.DoesNotContain("phone.connect", _calls);
        Assert.StartsWith("Couldn't disconnect Test Buds from this computer", result.Message);
    }

    [Fact]
    public async Task ToPhone_PhoneCantConnect_ReconnectsHere()
    {
        _phone.ConnectResults.Enqueue(CommandResult.Rejected("You didn't pick the earbuds."));

        var result = await Run(HandoffDirection.ToPhone);

        Assert.Equal(HandoffOutcome.RolledBack, result.Outcome);
        Assert.Equal(["pc.disconnect", "pc.connect"], _device.Calls);
        Assert.Equal(ConnectionState.Connected, _device.Connection);
        Assert.Contains("kept them on this computer", result.Message);
        Assert.Contains("You didn't pick the earbuds.", result.Message);
    }

    [Fact]
    public async Task ToPhone_PhoneCantConnect_AndReconnectFails_Stranded()
    {
        _phone.ConnectResults.Enqueue(CommandResult.Rejected("no"));
        _device.ConnectResults.Enqueue(CommandResult.Rejected("also no"));

        var result = await Run(HandoffDirection.ToPhone);

        Assert.Equal(HandoffOutcome.Stranded, result.Outcome);
        Assert.Contains("Bluetooth settings", result.Message);
    }

    [Fact]
    public async Task ToPhone_PhoneNeverAnswers_TimesOutAndRollsBack()
    {
        var time = new FakeTimeProvider();
        _phone.HangConnect = true;
        var run = Run(HandoffDirection.ToPhone, time: time);
        await Eventually.TrueAsync(() => _calls.Contains("phone.connect"), "the phone to be asked");

        time.Advance(HandoffTimeouts.Default.PhoneLink + TimeSpan.FromSeconds(5));
        var result = await run;

        Assert.Equal(HandoffOutcome.RolledBack, result.Outcome);
        Assert.Contains("pc.connect", _device.Calls);
    }

    [Fact]
    public async Task ToPhone_CallerCancelsWhilePhoneConnects_RollsBackInsteadOfThrowing()
    {
        _phone.HangConnect = true;
        using var cts = new CancellationTokenSource();
        var run = Run(HandoffDirection.ToPhone, ct: cts.Token);
        await Eventually.TrueAsync(() => _calls.Contains("phone.connect"), "the phone to be asked");

        await cts.CancelAsync();
        var result = await run;

        Assert.Equal(HandoffOutcome.RolledBack, result.Outcome);
        Assert.Contains("pc.connect", _device.Calls);
        Assert.Equal(ConnectionState.Connected, _device.Connection);
        Assert.Contains("kept them on this computer", result.Message);
    }

    [Fact]
    public async Task ToPhone_CallerCancelsDuringDisconnect_DisconnectFinishesAndBudsComeBack()
    {
        _device.DisconnectGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var run = Run(HandoffDirection.ToPhone, ct: cts.Token);
        await Eventually.TrueAsync(() => _device.Calls.Contains("pc.disconnect"), "the disconnect to start");

        await cts.CancelAsync();
        _device.DisconnectGate.SetResult();
        var result = await run;

        Assert.Equal(HandoffOutcome.RolledBack, result.Outcome);
        Assert.Contains("pc.connect", _device.Calls);
        Assert.Equal(ConnectionState.Connected, _device.Connection);
    }

    [Fact]
    public async Task ToPhone_NotConnectedHere_SkipsDisconnect()
    {
        _device.Connection = ConnectionState.Disconnected;

        var result = await Run(HandoffDirection.ToPhone);

        Assert.True(result.Succeeded);
        Assert.Empty(_device.Calls);
    }

    [Fact]
    public async Task ToPhone_NotConnectedHere_PhoneFails_DoesNotConnectHere()
    {
        _device.Connection = ConnectionState.Disconnected;
        _phone.ConnectResults.Enqueue(CommandResult.Rejected("no"));

        var result = await Run(HandoffDirection.ToPhone);

        Assert.Equal(HandoffOutcome.RolledBack, result.Outcome);
        Assert.Empty(_device.Calls);
    }

    [Fact]
    public async Task ToPhone_AlreadyThere_DoesNothing()
    {
        _device.Connection = ConnectionState.Disconnected;
        _phone.Devices[Address] = _phone.Devices[Address] with { Connected = true };

        var result = await Run(HandoffDirection.ToPhone);

        Assert.True(result.Succeeded);
        Assert.Empty(AllCalls);
    }

    [Fact]
    public async Task ToComputer_HappyPath_ReleasesThenConnects()
    {
        _device.Connection = ConnectionState.Disconnected;
        _phone.Devices[Address] = _phone.Devices[Address] with { Connected = true };

        var result = await Run(HandoffDirection.ToComputer);

        Assert.True(result.Succeeded);
        Assert.Equal("Test Buds moved to this computer.", result.Message);
        Assert.Equal(["phone.prepare", "phone.release"], _calls);
        Assert.Equal(["pc.connect"], _device.Calls);
    }

    [Fact]
    public async Task ToComputer_ConnectHereFails_PhoneTakesThemBack()
    {
        _device.Connection = ConnectionState.Disconnected;
        _phone.Devices[Address] = _phone.Devices[Address] with { Connected = true };
        _device.ConnectResults.Enqueue(CommandResult.Rejected("busy"));

        var result = await Run(HandoffDirection.ToComputer);

        Assert.Equal(HandoffOutcome.RolledBack, result.Outcome);
        Assert.Equal(["phone.prepare", "phone.release", "phone.connect"], _calls);
        Assert.Contains("back on Test Phone", result.Message);
    }

    [Fact]
    public async Task ToComputer_PhoneCantRelease_StopsBeforeConnecting()
    {
        _device.Connection = ConnectionState.Disconnected;
        _phone.Devices[Address] = _phone.Devices[Address] with { Connected = true };
        _phone.ReleaseResults.Enqueue(CommandResult.Rejected("Android didn't let go."));

        var result = await Run(HandoffDirection.ToComputer);

        Assert.Equal(HandoffOutcome.NotStarted, result.Outcome);
        Assert.Empty(_device.Calls);
    }

    [Fact]
    public async Task ToComputer_AlreadyHere_DoesNothing()
    {
        var result = await Run(HandoffDirection.ToComputer);

        Assert.True(result.Succeeded);
        Assert.Empty(AllCalls);
    }

    [Fact]
    public async Task NoAddress_NotStarted()
    {
        _device.Identity = _device.Identity with { Details = null };

        var result = await Run(HandoffDirection.ToPhone);

        Assert.Equal(HandoffOutcome.NotStarted, result.Outcome);
        Assert.Empty(AllCalls);
    }

    [Fact]
    public async Task NoPcLink_NotStarted()
    {
        var device = new FakeLinkDevice(hasLink: false);

        var result = await new HandoffCoordinator().RunAsync(device, _phone, HandoffDirection.ToPhone);

        Assert.Equal(HandoffOutcome.NotStarted, result.Outcome);
        Assert.Contains("Bluetooth settings", result.Message);
    }

    [Fact]
    public async Task Progress_ReportsEachStage()
    {
        _phone.ConnectResults.Enqueue(CommandResult.Rejected("no"));
        var stages = new List<HandoffStage>();

        await Run(HandoffDirection.ToPhone, new SyncProgress(p => stages.Add(p.Stage)));

        Assert.Equal([HandoffStage.Checking, HandoffStage.Releasing, HandoffStage.Connecting, HandoffStage.RollingBack, HandoffStage.Done], stages);
    }

    [Fact]
    public async Task AddressIsNormalized_ForThePhone()
    {
        _device.Identity = _device.Identity with { Details = new DeviceDetails { Address = "aa-bb-cc-dd-ee-01" } };

        var result = await Run(HandoffDirection.ToPhone);

        Assert.True(result.Succeeded);
    }

    private sealed class SyncProgress(Action<HandoffProgress> report) : IProgress<HandoffProgress>
    {
        public void Report(HandoffProgress value) => report(value);
    }
}

public class PhoneAgentTests
{
    private const string Address = "AA:BB:CC:DD:EE:01";

    private readonly FakePhoneAudio _audio = new FakePhoneAudio().With(Address, connected: false);

    private PhoneAgent Agent => new(_audio);

    [Fact]
    public void Prepare_Ready() => Assert.True(Agent.Prepare(Address).Succeeded);

    [Fact]
    public void Prepare_BluetoothOff_Refused()
    {
        _audio.IsBluetoothOn = false;

        Assert.Equal("Bluetooth is off on the phone.", Agent.Prepare(Address).Message);
    }

    [Fact]
    public void Prepare_Blocker_Wins()
    {
        _audio.Blocker = "Allow Samaat to use nearby devices.";

        Assert.Equal("Allow Samaat to use nearby devices.", Agent.Prepare(Address).Message);
    }

    [Fact]
    public void Prepare_NotPairedWithPhone_Refused() =>
        Assert.Contains("aren't paired with the phone", Agent.Prepare("11:22:33:44:55:66").Message);

    [Fact]
    public void Prepare_MatchesAnyAddressCase() => Assert.True(Agent.Prepare("aa:bb:cc:dd:ee:01").Succeeded);

    [Fact]
    public async Task Connect_CallsPhoneAudio()
    {
        var reply = await Agent.HandleAsync(new ConnectAudioMessage(Address) { Id = 4 }, default);

        Assert.Equal(CommandStatus.Confirmed, reply!.Status);
        Assert.Equal(4, reply.ReplyTo);
        Assert.Equal(["connect " + Address], _audio.Calls);
    }

    [Fact]
    public async Task Connect_AlreadyConnected_NoCall()
    {
        _audio.With(Address, connected: true);

        var reply = await Agent.HandleAsync(new ConnectAudioMessage(Address), default);

        Assert.Equal(CommandStatus.Confirmed, reply!.Status);
        Assert.Empty(_audio.Calls);
    }

    [Fact]
    public async Task Connect_Failure_PassesReasonBack()
    {
        _audio.ConnectResults.Enqueue(CommandResult.Rejected("Tap the earbuds in the output picker."));

        var reply = await Agent.HandleAsync(new ConnectAudioMessage(Address), default);

        Assert.Equal("Tap the earbuds in the output picker.", reply!.Message);
    }

    [Fact]
    public async Task Release_NotConnected_ConfirmedWithoutCall()
    {
        var reply = await Agent.HandleAsync(new ReleaseAudioMessage(Address), default);

        Assert.Equal(CommandStatus.Confirmed, reply!.Status);
        Assert.Empty(_audio.Calls);
    }

    [Fact]
    public async Task Release_Connected_Disconnects()
    {
        _audio.With(Address, connected: true);

        await Agent.HandleAsync(new ReleaseAudioMessage(Address), default);

        Assert.Equal(["disconnect " + Address], _audio.Calls);
    }

    [Fact]
    public async Task OtherMessages_NotHandled() => Assert.Null(await Agent.HandleAsync(new PingMessage(), default));

    [Fact]
    public async Task Connect_BluetoothTurnsOffWhileWaiting_FailsFast()
    {
        _audio.HangConnect = true;
        var reply = Agent.HandleAsync(new ConnectAudioMessage(Address) { Id = 7 }, default);
        await Eventually.TrueAsync(() => _audio.Calls.Count == 1, "the connect to start");

        _audio.TurnBluetoothOff();

        var done = await Task.WhenAny(reply, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(reply, done);
        var result = await reply;
        Assert.Equal(CommandStatus.Rejected, result!.Status);
        Assert.Equal("Bluetooth turned off on the phone.", result.Message);
    }

    [Fact]
    public void ListDevices_NormalizesAddresses()
    {
        _audio.With("aa:bb:cc:dd:ee:02", connected: true, "Other");

        Assert.Contains(Agent.ListDevices(), d => d.Address == "AA:BB:CC:DD:EE:02" && d.Connected);
    }
}

[CollectionDefinition("Handoff log", DisableParallelization = true)]
public sealed class HandoffLogCollection;

/// <summary>Swaps the process-wide log sink, so these tests must not run beside others that read it.</summary>
[Collection("Handoff log")]
public sealed class CoordinatorLoggingTests : IDisposable
{
    private readonly ILogSink _previous = SamaatLog.Sink;
    private readonly Capture _log = new();

    public CoordinatorLoggingTests() => SamaatLog.Sink = _log;

    public void Dispose() => SamaatLog.Sink = _previous;

    [Fact]
    public async Task ARolledBackMove_IsLoggedStepByStep_WithoutTheAddress()
    {
        // Other tests log under "Handoff" too, so this run's lines are the ones naming these earbuds.
        const string buds = "LogTest Buds 7f3a";
        var device = new FakeLinkDevice { Identity = new DeviceIdentity("log-test", buds, Details: new DeviceDetails { Address = FakeLinkDevice.DefaultAddress }) };
        var phone = new FakePhoneEndpoint([]);
        phone.ConnectResults.Enqueue(CommandResult.Rejected("You didn't pick the earbuds."));

        var result = await new HandoffCoordinator().RunAsync(device, phone, HandoffDirection.ToPhone);

        var mine = _log.Entries.Where(e => e.Source == "Handoff" && e.Message.Contains(buds)).ToList();
        Assert.All(mine, e => Assert.Equal(LogLevel.Info, e.Level));
        Assert.Contains(mine, e => e.Message.Contains("Starting") && e.Message.Contains("Test Phone") && e.Message.Contains("ToPhone"));
        Assert.Contains(mine, e => e.Message.Contains("disconnect") && e.Message.Contains("Confirmed"));
        Assert.Contains(mine, e => e.Message.Contains("connect") && e.Message.Contains("Rejected") && e.Message.Contains("You didn't pick the earbuds."));
        Assert.Contains(mine, e => e.Message.Contains("Rolling back"));
        Assert.Contains(mine, e => e.Message.Contains("RolledBack") && e.Message.Contains(result.Message));
        Assert.DoesNotContain(_log.Entries, e => e.Message.Contains(buds) && e.Message.Contains(FakeLinkDevice.DefaultAddress, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class Capture : ILogSink
    {
        private readonly List<(LogLevel Level, string Source, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Source, string Message)> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public void Write(LogLevel level, string source, string message, Exception? error)
        {
            lock (_entries)
            {
                _entries.Add((level, source, message));
            }
        }
    }
}

public class DialErrorTests
{
    [Fact]
    public void ConnectionRefused_SaysSamaatMustBeRunning() =>
        Assert.Equal("The computer refused the connection. Check that Samaat is running on it.", PhoneLinkClient.DialError(SocketError.ConnectionRefused));

    [Fact]
    public void EveryOtherError_IsPlainEnglish()
    {
        foreach (var error in Enum.GetValues<SocketError>().Where(e => e != SocketError.ConnectionRefused).Distinct())
        {
            var text = PhoneLinkClient.DialError(error);
            Assert.Equal("Couldn't reach the computer. Check that both are on the same Wi-Fi.", text);
            Assert.DoesNotContain("(", text);
            var name = error.ToString();
            if (name.Length > 4)
            {
                Assert.DoesNotContain(name, text);
            }
        }
    }
}

using Microsoft.Extensions.Time.Testing;
using Samaat.Core.Commands;
using Samaat.Core.Connections;
using Samaat.Core.Devices;
using Samaat.Simulator;

namespace Samaat.Core.Tests;

public class PcLinkTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task AwaitState_AlreadyThere_ConfirmedImmediately()
    {
        var device = new FakeDevice(null, ConnectionState.Connected);

        var result = await PcLink.AwaitStateAsync(device, ConnectionState.Connected, Timeout, new FakeTimeProvider());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AwaitState_ReachesStateLater_Confirmed()
    {
        var device = new FakeDevice(null, ConnectionState.Disconnected);
        var wait = PcLink.AwaitStateAsync(device, ConnectionState.Connected, Timeout, new FakeTimeProvider());
        Assert.False(wait.IsCompleted);

        device.Connection = ConnectionState.Connected;
        device.RaiseConnectionChanged();

        Assert.True((await wait).Succeeded);
    }

    [Fact]
    public async Task AwaitState_Never_TimesOut()
    {
        var time = new FakeTimeProvider();
        var device = new FakeDevice(null, ConnectionState.Disconnected);
        var wait = PcLink.AwaitStateAsync(device, ConnectionState.Connected, Timeout, time);

        time.Advance(Timeout + TimeSpan.FromSeconds(1));

        Assert.Equal(CommandStatus.TimedOut, (await wait).Status);
    }

    [Fact]
    public async Task AwaitState_Cancelled_Throws()
    {
        var device = new FakeDevice(null, ConnectionState.Disconnected);
        using var cts = new CancellationTokenSource();
        var wait = PcLink.AwaitStateAsync(device, ConnectionState.Connected, Timeout, new FakeTimeProvider(), cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }

    [Fact]
    public async Task AwaitState_ChangeRacesSubscription_StillConfirmed()
    {
        var device = new RacingDevice();

        var result = await PcLink.AwaitStateAsync(device, ConnectionState.Connected, Timeout, new FakeTimeProvider());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AwaitState_Unsubscribes_AfterTimeout()
    {
        var time = new FakeTimeProvider();
        var device = new RacingDevice(flipOnAdd: false);
        var wait = PcLink.AwaitStateAsync(device, ConnectionState.Connected, Timeout, time);
        time.Advance(Timeout + TimeSpan.FromSeconds(1));
        await wait;

        Assert.Equal(0, device.Subscribers);
    }

    [Fact]
    public void SendToAll_DedupesByFilter()
    {
        var calls = 0;
        int Send()
        {
            calls++;
            return 0;
        }

        var result = PcLink.SendToAll([new("a", Send), new("a", Send), new("b", Send)]);

        Assert.Equal(2, calls);
        Assert.Equal(2, result.Attempted);
    }

    [Fact]
    public void SendToAll_CallsEveryDistinctTarget()
    {
        var called = new List<string>();
        PcLinkTarget Target(string id) => new(id, () =>
        {
            called.Add(id);
            return 0;
        });

        var result = PcLink.SendToAll([Target("a"), Target("b"), Target("c")]);

        Assert.Equal(["a", "b", "c"], called);
        Assert.True(result.Accepted);
        Assert.Null(result.FirstFailure);
    }

    [Fact]
    public void SendToAll_OneSuccessAmongFailures_Accepted()
    {
        var result = PcLink.SendToAll([new("a", () => unchecked((int)0x80004005)), new("b", () => 0), new("c", () => unchecked((int)0x80070005))]);

        Assert.True(result.Accepted);
        Assert.Equal(3, result.Attempted);
        Assert.Equal(unchecked((int)0x80004005), result.FirstFailure);
    }

    [Fact]
    public void SendToAll_AllFail_ReportsFirstFailure()
    {
        var result = PcLink.SendToAll([new("a", () => unchecked((int)0x80070005)), new("b", () => unchecked((int)0x80004005))]);

        Assert.False(result.Accepted);
        Assert.Equal(unchecked((int)0x80070005), result.FirstFailure);
    }

    [Fact]
    public void SendToAll_ThrowingTarget_CountsAsFailure_OthersStillCalled()
    {
        var laterCalled = false;

        var result = PcLink.SendToAll([
            new("a", () => throw new InvalidOperationException("boom")),
            new("b", () =>
            {
                laterCalled = true;
                return 0;
            })]);

        Assert.True(laterCalled);
        Assert.True(result.Accepted);
        Assert.NotNull(result.FirstFailure);
        Assert.True(result.FirstFailure < 0);
    }

    [Fact]
    public void SendToAll_NoTargets_NotAcceptedZeroAttempted()
    {
        var result = PcLink.SendToAll([]);

        Assert.False(result.Accepted);
        Assert.Equal(0, result.Attempted);
        Assert.Null(result.FirstFailure);
    }


    [Fact]
    public void ToResult_MapsSendOutcomes()
    {
        Assert.Null(PcLink.ToResult(new PcLinkSendResult(true, 2, null)));
        Assert.Equal(CommandStatus.Unsupported, PcLink.ToResult(new PcLinkSendResult(false, 0, null))!.Status);
        var refused = PcLink.ToResult(new PcLinkSendResult(false, 1, unchecked((int)0x80070005)))!;
        Assert.Equal(CommandStatus.Rejected, refused.Status);
        Assert.Equal("Windows refused the request (0x80070005).", refused.Message);
    }
    [Fact]
    public async Task SimulatedPcLink_Disconnect_ThenConnect_ChangesState()
    {
        var device = new SimulatedEarbudDevice(new DeviceIdentity("sim", "Sim"), SimulatedBatteryLayout.BudsOnly);
        var link = device.GetFeature<IPcLinkFeature>()!;

        Assert.True((await link.DisconnectAsync()).Succeeded);
        Assert.Equal(ConnectionState.Disconnected, device.Connection);

        Assert.True((await link.ConnectAsync()).Succeeded);
        Assert.Equal(ConnectionState.Connected, device.Connection);
    }

    /// <summary>Flips to the wanted state inside the event's add accessor, like a change landing between subscribe and check.</summary>
    private sealed class RacingDevice(bool flipOnAdd = true) : IEarbudDevice
    {
        private EventHandler? _handlers;

        public int Subscribers => _handlers?.GetInvocationList().Length ?? 0;

        public DeviceIdentity Identity { get; } = new("race", "Race");

        public ConnectionState Connection { get; private set; } = ConnectionState.Disconnected;

        public IReadOnlyCollection<IFeature> Features { get; } = [];

        public event EventHandler? ConnectionChanged
        {
            add
            {
                _handlers += value;
                if (flipOnAdd)
                {
                    Connection = ConnectionState.Connected; // no event: only checking after subscribing sees it
                }
            }
            remove => _handlers -= value;
        }
    }
}

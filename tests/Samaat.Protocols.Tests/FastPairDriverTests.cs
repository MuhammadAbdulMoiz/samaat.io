using Microsoft.Extensions.Time.Testing;
using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Protocols.FastPair;
using Samaat.Transport;

namespace Samaat.Protocols.Tests;

public sealed class FastPairDriverTests
{
    private static readonly Guid MessageStream = Guid.Parse("df21fe2c-2515-4fdb-8886-f12c4d67927c");

    private static DriverContext Context(FakeRfcommTransports transports, FakeTimeProvider? time = null) =>
        new(new DeviceSignals("realme Buds T200 Lite"),
            new CatalogEntry("realme.buds-t200-lite", "realme Buds T200 Lite", "realme", "fast-pair", new DeviceMatch(), false, [], new HashSet<FeatureId>()),
            transports,
            time ?? new FakeTimeProvider());

    private static FakeRfcommTransports Opens(FakeRfcommChannel channel, params byte[][] greeting) =>
        new((_, _) =>
        {
            // Emitted before the driver can have subscribed, like a fast device on a real channel.
            channel.Emit(greeting);
            return Task.FromResult<IRfcommChannel?>(channel);
        });

    private static async Task<FastPairBatteryFeature> OpenFeature(FakeRfcommTransports transports, FastPairDriver? driver = null)
    {
        var features = await (driver ?? new FastPairDriver()).OpenAsync(Context(transports));
        return Assert.IsType<FastPairBatteryFeature>(Assert.Single(features));
    }

    [Fact]
    public void Identifies_AsFastPair()
    {
        Assert.Equal("fast-pair", new FastPairDriver().Id);
        Assert.Equal("Google Fast Pair", new FastPairDriver().DisplayName);
    }

    [Fact]
    public async Task HappyPath_ReturnsOneBatteryFeatureWithLeftAndRight()
    {
        var channel = new FakeRfcommChannel();
        var transports = Opens(channel, FastPairCaptured.Chunk1, FastPairCaptured.Chunk2);

        var battery = await OpenFeature(transports);

        Assert.Equal([MessageStream], transports.Opened);
        Assert.Equal(90, battery.Current.Get(BatteryComponent.Left)!.Percent);
        Assert.Equal(70, battery.Current.Get(BatteryComponent.Right)!.Percent);
        Assert.True(battery.Current.Get(BatteryComponent.Case) is null or { Percent: null });
        Assert.Equal(0, channel.Writes);
    }

    [Fact]
    public async Task Feature_DescribesAnUnverifiedRuntimeSourceThatIsNotExhaustive()
    {
        var battery = await OpenFeature(Opens(new FakeRfcommChannel(), FastPairCaptured.Greeting));

        Assert.Equal(CapabilitySource.RuntimeQuery, battery.Source);
        Assert.Equal(Verification.Unverified, battery.Verification);
        Assert.False(battery.ComponentsAreExhaustive);
        Assert.Equal(new[] { BatteryComponent.Left, BatteryComponent.Right, BatteryComponent.Case }.Order(), battery.Components.Order());
    }

    [Fact]
    public async Task Feature_ReportsFirmwareAndModel_AndImplementsDeviceInfo()
    {
        var battery = await OpenFeature(Opens(new FakeRfcommChannel(), FastPairCaptured.Greeting));

        var info = Assert.IsAssignableFrom<IDeviceInfoFeature>(battery);
        Assert.Equal("1.0.0", info.FirmwareVersion);
        Assert.Equal("67B851", battery.ModelId);
    }

    [Fact]
    public async Task ChargingBit_BecomesChargingState()
    {
        var channel = new FakeRfcommChannel();
        var battery = await OpenFeature(Opens(channel, FastPairCaptured.Greeting));

        channel.Emit(Convert.FromHexString("030300" + "03" + "DA46E4"));

        Assert.True(battery.Current.Get(BatteryComponent.Left)!.IsCharging);
        Assert.False(battery.Current.Get(BatteryComponent.Right)!.IsCharging);
        Assert.Equal(100, battery.Current.Get(BatteryComponent.Case)!.Percent);
    }

    [Fact]
    public async Task EveryBatteryMessage_UpdatesAndRaisesChanged()
    {
        var channel = new FakeRfcommChannel();
        var battery = await OpenFeature(Opens(channel, FastPairCaptured.Greeting));
        BatteryState? seen = null;
        battery.Changed += (_, state) => seen = state;

        channel.Emit(FastPairCaptured.Battery50_40);

        Assert.Equal(50, seen!.Get(BatteryComponent.Left)!.Percent);
        Assert.Equal(40, battery.Current.Get(BatteryComponent.Right)!.Percent);
    }

    [Fact]
    public async Task FirmwareArrivingLater_RaisesDeviceInfoChanged()
    {
        var channel = new FakeRfcommChannel();
        var battery = await OpenFeature(Opens(channel, FastPairCaptured.Chunk1));
        var raised = 0;
        ((IDeviceInfoFeature)battery).Changed += (_, _) => raised++;
        Assert.Null(battery.FirmwareVersion);

        channel.Emit(FastPairCaptured.Chunk2);
        channel.Emit(FastPairCaptured.Chunk2);

        Assert.Equal("1.0.0", battery.FirmwareVersion);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task NoChannel_ReturnsNothing()
    {
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(null));

        Assert.Empty(await new FastPairDriver().OpenAsync(Context(transports)));
    }

    [Fact]
    public async Task OpenerThatThrows_ReturnsNothing()
    {
        var transports = new FakeRfcommTransports((_, _) => throw new InvalidOperationException("bluetooth is off"));

        Assert.Empty(await new FastPairDriver().OpenAsync(Context(transports)));
    }

    [Fact]
    public async Task SilentChannel_ReturnsNothingWhenTheTokenExpires_AndDisposesTheChannel()
    {
        var channel = new FakeRfcommChannel();
        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var features = await new FastPairDriver().OpenAsync(Context(Opens(channel)), budget.Token);

        Assert.Empty(features);
        Assert.True(channel.Disposed);
        Assert.Equal(0, channel.Writes);
    }

    [Fact]
    public async Task ChannelWithOnlyUnknownBattery_ReturnsNothing()
    {
        var channel = new FakeRfcommChannel();
        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var features = await new FastPairDriver().OpenAsync(
            Context(Opens(channel, Convert.FromHexString("030300037F7F7F"))), budget.Token);

        Assert.Empty(features);
        Assert.True(channel.Disposed);
    }

    [Fact]
    public async Task ChannelWhoseReadLoopFails_ReturnsNothingQuickly_AndDisposesTheChannel()
    {
        var channel = new FakeRfcommChannel();
        var transports = new FakeRfcommTransports((_, _) =>
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(30);
                channel.Close();
            });
            return Task.FromResult<IRfcommChannel?>(channel);
        });
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var started = DateTime.UtcNow;

        var features = await new FastPairDriver().OpenAsync(Context(transports), budget.Token);

        Assert.Empty(features);
        Assert.True(channel.Disposed);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        Assert.Equal(0, channel.Writes);
    }

    [Fact]
    public async Task ChannelClosingAfterTheFirstReading_KeepsTheLastValuesWithoutCrashing()
    {
        var channel = new FakeRfcommChannel();
        var time = new FakeTimeProvider();
        var features = await new FastPairDriver().OpenAsync(Context(Opens(channel, FastPairCaptured.Greeting), time));
        var battery = (FastPairBatteryFeature)features.Single();

        channel.Close();
        time.Advance(TimeSpan.FromHours(1));

        var left = battery.Current.Get(BatteryComponent.Left)!;
        Assert.Equal(90, left.Percent);
        Assert.True(left.IsStale(time.GetUtcNow(), TimeSpan.FromMinutes(30)));
        Assert.Equal(0, channel.Writes);
    }

    [Fact]
    public async Task Dispose_ReleasesTheChannel_AndLaterChunksAreIgnored()
    {
        var channel = new FakeRfcommChannel();
        var battery = await OpenFeature(Opens(channel, FastPairCaptured.Greeting));

        await battery.DisposeAsync();
        channel.Emit(FastPairCaptured.Battery50_40);

        Assert.True(channel.Disposed);
        Assert.Equal(90, battery.Current.Get(BatteryComponent.Left)!.Percent);
        Assert.Equal(0, channel.Writes);
    }

    [Fact]
    public async Task Refresh_DisposesTheOldChannel_ReopensAndUpdatesFromTheNewPush()
    {
        var first = new FakeRfcommChannel();
        var second = new FakeRfcommChannel();
        var channels = new Queue<FakeRfcommChannel>([first, second]);
        var transports = new FakeRfcommTransports((_, _) =>
        {
            var next = channels.Dequeue();
            next.Emit(next == first ? FastPairCaptured.Greeting : FastPairCaptured.Battery50_40);
            return Task.FromResult<IRfcommChannel?>(next);
        });
        var battery = await OpenFeature(transports);

        await battery.RefreshAsync();

        Assert.True(first.Disposed);
        Assert.False(second.Disposed);
        Assert.Equal(2, transports.Opened.Count);
        Assert.Equal(50, battery.Current.Get(BatteryComponent.Left)!.Percent);
        Assert.Equal(40, battery.Current.Get(BatteryComponent.Right)!.Percent);
        Assert.Equal(0, first.Writes + second.Writes);
    }

    [Fact]
    public async Task FailedRefresh_KeepsTheLastReading_AndDoesNotThrow()
    {
        var first = new FakeRfcommChannel();
        var calls = 0;
        var transports = new FakeRfcommTransports((_, _) =>
        {
            if (calls++ == 0)
            {
                first.Emit(FastPairCaptured.Greeting);
                return Task.FromResult<IRfcommChannel?>(first);
            }

            throw new InvalidOperationException("link dropped");
        });
        var battery = await OpenFeature(transports);

        await battery.RefreshAsync();

        Assert.Equal(90, battery.Current.Get(BatteryComponent.Left)!.Percent);
    }

    [Fact]
    public async Task RefreshWithNoNewPush_GivesUpAfterTheRefreshTimeout_WithoutThrowing()
    {
        var channels = new Queue<FakeRfcommChannel>([new FakeRfcommChannel(), new FakeRfcommChannel()]);
        var transports = new FakeRfcommTransports((_, _) =>
        {
            var next = channels.Dequeue();
            if (channels.Count == 1)
            {
                next.Emit(FastPairCaptured.Greeting);
            }

            return Task.FromResult<IRfcommChannel?>(next);
        });
        var battery = await OpenFeature(transports, new FastPairDriver(refreshTimeout: TimeSpan.FromMilliseconds(100)));

        await battery.RefreshAsync();

        Assert.Equal(90, battery.Current.Get(BatteryComponent.Left)!.Percent);
    }

    [Fact]
    public async Task Refresh_WhoseNewChannelTimesOut_DisposesThatChannelAndKeepsTheLastReading()
    {
        var first = new FakeRfcommChannel();
        var second = new FakeRfcommChannel();
        var channels = new Queue<FakeRfcommChannel>([first, second]);
        var transports = new FakeRfcommTransports((_, _) =>
        {
            var next = channels.Dequeue();
            if (next == first)
            {
                next.Emit(FastPairCaptured.Greeting);
            }

            return Task.FromResult<IRfcommChannel?>(next);
        });
        var battery = await OpenFeature(transports, new FastPairDriver(refreshTimeout: TimeSpan.FromMilliseconds(100)));

        await battery.RefreshAsync();

        Assert.True(second.Disposed);
        Assert.Equal(0, first.Writes + second.Writes);
        Assert.Equal(90, battery.Current.Get(BatteryComponent.Left)!.Percent);
    }

    [Fact]
    public async Task Refresh_WhoseNewChannelCloses_DisposesThatChannelAndKeepsTheLastReading()
    {
        var first = new FakeRfcommChannel();
        var second = new FakeRfcommChannel();
        var channels = new Queue<FakeRfcommChannel>([first, second]);
        var transports = new FakeRfcommTransports((_, _) =>
        {
            var next = channels.Dequeue();
            if (next == first)
            {
                next.Emit(FastPairCaptured.Greeting);
            }
            else
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(30);
                    next.Close();
                });
            }

            return Task.FromResult<IRfcommChannel?>(next);
        });
        var battery = await OpenFeature(transports, new FastPairDriver(refreshTimeout: TimeSpan.FromSeconds(10)));

        await battery.RefreshAsync();

        Assert.True(second.Disposed);
        Assert.Equal(0, first.Writes + second.Writes);
        Assert.Equal(90, battery.Current.Get(BatteryComponent.Left)!.Percent);
    }

    [Fact]
    public async Task ChannelThatClosedBeforeTheSubscription_ReturnsNothingPromptly_AndDisposesTheChannel()
    {
        var channel = new FakeRfcommChannel();
        channel.Close();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var started = DateTime.UtcNow;

        var features = await new FastPairDriver().OpenAsync(Context(Opens(channel)), budget.Token);

        Assert.Empty(features);
        Assert.True(channel.Disposed);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1));
        Assert.Equal(0, channel.Writes);
    }

    [Fact]
    public async Task Refresh_PropagatesCallerCancellation()
    {
        var battery = await OpenFeature(Opens(new FakeRfcommChannel(), FastPairCaptured.Greeting));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => battery.RefreshAsync(cancelled.Token));
    }
}

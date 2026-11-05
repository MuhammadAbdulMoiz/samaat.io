using Microsoft.Extensions.Time.Testing;
using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Connections;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Protocols.FastPair;
using Samaat.Transport;

namespace Samaat.Protocols.Tests;

public sealed class FastPairDriverTests
{
    private static readonly Guid MessageStream = Guid.Parse("df21fe2c-2515-4fdb-8886-f12c4d67927c");

    private static FastPairDriver Driver(TimeSpan? refreshTimeout = null) =>
        new(refreshTimeout, sassTimeout: TimeSpan.FromMilliseconds(50));

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
        var features = await (driver ?? Driver()).OpenAsync(Context(transports));
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
        FastPairWrites.AssertAllowed(channel);
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

        Assert.Empty(await Driver().OpenAsync(Context(transports)));
    }

    [Fact]
    public async Task OpenerThatThrows_ReturnsNothing()
    {
        var transports = new FakeRfcommTransports((_, _) => throw new InvalidOperationException("bluetooth is off"));

        Assert.Empty(await Driver().OpenAsync(Context(transports)));
    }

    [Fact]
    public async Task SilentChannel_ReturnsNothingWhenTheTokenExpires_AndDisposesTheChannel()
    {
        var channel = new FakeRfcommChannel();
        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var features = await Driver().OpenAsync(Context(Opens(channel)), budget.Token);

        Assert.Empty(features);
        Assert.True(channel.Disposed);
        FastPairWrites.AssertAllowed(channel);
    }

    [Fact]
    public async Task ChannelWithOnlyUnknownBattery_ReturnsNothing()
    {
        var channel = new FakeRfcommChannel();
        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var features = await Driver().OpenAsync(
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

        var features = await Driver().OpenAsync(Context(transports), budget.Token);

        Assert.Empty(features);
        Assert.True(channel.Disposed);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        FastPairWrites.AssertAllowed(channel);
    }

    [Fact]
    public async Task ChannelClosingAfterTheFirstReading_KeepsTheLastValuesWithoutCrashing()
    {
        var channel = new FakeRfcommChannel();
        var time = new FakeTimeProvider();
        var features = await Driver().OpenAsync(Context(Opens(channel, FastPairCaptured.Greeting), time));
        var battery = (FastPairBatteryFeature)features.Single();

        channel.Close();
        time.Advance(TimeSpan.FromHours(1));

        var left = battery.Current.Get(BatteryComponent.Left)!;
        Assert.Equal(90, left.Percent);
        Assert.True(left.IsStale(time.GetUtcNow(), TimeSpan.FromMinutes(30)));
        FastPairWrites.AssertAllowed(channel);
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
        FastPairWrites.AssertAllowed(channel);
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
        FastPairWrites.AssertAllowed(first, second);
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
        var battery = await OpenFeature(transports, Driver(refreshTimeout: TimeSpan.FromMilliseconds(100)));

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
        var battery = await OpenFeature(transports, Driver(refreshTimeout: TimeSpan.FromMilliseconds(100)));

        await battery.RefreshAsync();

        Assert.True(second.Disposed);
        FastPairWrites.AssertAllowed(first, second);
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
        var battery = await OpenFeature(transports, Driver(refreshTimeout: TimeSpan.FromSeconds(10)));

        await battery.RefreshAsync();

        Assert.True(second.Disposed);
        FastPairWrites.AssertAllowed(first, second);
        Assert.Equal(90, battery.Current.Get(BatteryComponent.Left)!.Percent);
    }

    [Fact]
    public async Task ChannelThatClosedBeforeTheSubscription_ReturnsNothingPromptly_AndDisposesTheChannel()
    {
        var channel = new FakeRfcommChannel();
        channel.Close();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var started = DateTime.UtcNow;

        var features = await Driver().OpenAsync(Context(Opens(channel)), budget.Token);

        Assert.Empty(features);
        Assert.True(channel.Disposed);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1));
        FastPairWrites.AssertAllowed(channel);
    }

    [Fact]
    public async Task Refresh_PropagatesCallerCancellation()
    {
        var battery = await OpenFeature(Opens(new FakeRfcommChannel(), FastPairCaptured.Greeting));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => battery.RefreshAsync(cancelled.Token));
    }

    // SASS replies are built from the spec, not captured: the realme T200 Lite doesn't implement SASS.
    private static readonly byte[] CapMultipointOn = Convert.FromHexString("07110004" + "0102" + "E000");
    private static readonly byte[] CapToggleableOff = Convert.FromHexString("07110004" + "0102" + "C000");
    private static readonly byte[] CapSassOnly = Convert.FromHexString("07110004" + "0102" + "8000");
    private static readonly byte[] CapVersion0 = Convert.FromHexString("07110004" + "0000" + "E000");
    private static readonly byte[] OtherIsActive = Convert.FromHexString("07340003" + "02AABB");

    private static FakeRfcommChannel Sass(byte[]? capability) => new()
    {
        Respond = frame => capability is not null && frame.SequenceEqual(FastPairMessages.SassGetCapability) ? [capability] : [],
    };

    private static async Task<IReadOnlyList<IFeature>> Open(FakeRfcommChannel channel) =>
        await Driver().OpenAsync(Context(Opens(channel, FastPairCaptured.Greeting)));

    [Fact]
    public async Task RealmeGreetingWithNoSassReply_WritesOnlyTheCapabilityRequest_AndReturnsBatteryOnly()
    {
        var channel = new FakeRfcommChannel();

        var features = await Open(channel);

        Assert.IsType<FastPairBatteryFeature>(Assert.Single(features));
        Assert.Equal([FastPairMessages.SassGetCapability], channel.Written);
    }

    [Fact]
    public async Task SilentSass_TimesOutAlongsideASlowGreeting_NotAfterIt()
    {
        var channel = new FakeRfcommChannel();
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));

        var open = new FastPairDriver(sassTimeout: TimeSpan.FromSeconds(1)).OpenAsync(Context(transports));
        await Task.Delay(TimeSpan.FromSeconds(1.2)); // the SASS deadline has passed before the greeting arrives
        channel.Emit(FastPairCaptured.Greeting);

        // A deadline started only after the greeting would need another full second.
        var features = await open.WaitAsync(TimeSpan.FromMilliseconds(600));
        Assert.IsType<FastPairBatteryFeature>(Assert.Single(features));
        FastPairWrites.AssertAllowed(channel);
    }

    [Fact]
    public async Task SassMultipointProvider_WritesTheTwoGetRequestsAndNothingElse()
    {
        var channel = Sass(CapMultipointOn);

        var features = await Open(channel);

        Assert.Equal(2, features.Count);
        Assert.Equal([FastPairMessages.SassGetCapability, FastPairMessages.SassGetConnectionStatus], channel.Written);
    }

    [Fact]
    public async Task CapabilityWithMultipointOn_ReturnsAReadOnlyMultipointFeature()
    {
        var features = await Open(Sass(CapMultipointOn));

        var sass = Assert.IsAssignableFrom<IMultipointFeature>(features[1]);
        Assert.Equal(new MultipointOptions(2, false, false, false), sass.Options);
        Assert.Equal(CapabilitySource.RuntimeQuery, sass.Source);
        Assert.Equal(Verification.Unverified, sass.Verification);
        Assert.True(sass.Current.MultipointEnabled);
        Assert.Equal(HostLinkState.Active, Assert.Single(sass.Current.Hosts).State);
        Assert.Equal(CommandStatus.Unsupported, (await sass.SwitchToAsync("other")).Status);
        Assert.Equal(CommandStatus.Unsupported, (await sass.DisconnectAsync("other")).Status);
        Assert.Equal(CommandStatus.Unsupported, (await sass.SetMultipointEnabledAsync(false)).Status);
    }

    [Fact]
    public async Task StatusAndSwitchEvents_UpdateTheHostsAndRaiseChanged()
    {
        var channel = Sass(CapMultipointOn);
        var sass = (IMultipointFeature)(await Open(channel))[1];
        var raised = 0;
        sass.Changed += (_, _) => raised++;

        channel.Emit(OtherIsActive);
        Assert.Equal(1, raised);
        Assert.Equal(HostLinkState.Connected, sass.Current.Hosts.Single(h => h.IsThisHost).State);
        var other = sass.Current.Hosts.Single(h => !h.IsThisHost);
        Assert.Equal((HostLinkState.Active, "Other device"), (other.State, other.Name));

        channel.Emit(OtherIsActive); // no change, no event
        Assert.Equal(1, raised);

        channel.Emit(Convert.FromHexString("07320009" + "00" + "02" + Convert.ToHexString("Pixel 8"u8)));
        Assert.Equal("Pixel 8", sass.Current.Hosts.Single(h => !h.IsThisHost).Name);

        channel.Emit(Convert.FromHexString("07320002" + "00" + "01"));
        Assert.Equal(HostLinkState.Active, sass.Current.Hosts.Single(h => h.IsThisHost).State);
        Assert.Equal(HostLinkState.Connected, sass.Current.Hosts.Single(h => !h.IsThisHost).State);

        channel.Emit(Convert.FromHexString("07340001" + "00"));
        Assert.Equal(HostLinkState.Active, sass.Current.Hosts.Single(h => !h.IsThisHost).State);
        channel.Emit(Convert.FromHexString("07340001" + "01"));
        Assert.Equal(HostLinkState.Active, sass.Current.Hosts.Single(h => h.IsThisHost).State);
        FastPairWrites.AssertAllowed(channel);
    }

    [Fact]
    public async Task CapabilityVersion0_OrNoMultipoint_MeansBatteryOnly()
    {
        Assert.IsType<FastPairBatteryFeature>(Assert.Single(await Open(Sass(CapVersion0))));
        Assert.IsType<FastPairBatteryFeature>(Assert.Single(await Open(Sass(CapSassOnly))));
    }

    [Fact]
    public async Task ToggleableButOff_ReturnsTheFeatureWithMultipointDisabled_AndNeverAsksForStatus()
    {
        var channel = Sass(CapToggleableOff);

        var features = await Open(channel);

        Assert.False(((IMultipointFeature)features[1]).Current.MultipointEnabled);
        Assert.Equal([FastPairMessages.SassGetCapability], channel.Written);
    }

    [Fact]
    public async Task WriteThatThrows_YieldsBatteryOnlyWithoutAnException()
    {
        var channel = new FakeRfcommChannel { WriteFails = new IOException("link dropped") };

        Assert.IsType<FastPairBatteryFeature>(Assert.Single(await Open(channel)));
    }

    [Fact]
    public async Task Refresh_ResendsTheCapabilityRequestOnTheNewChannel_AndTheStatusRequestWhenMultipointIsOn()
    {
        var first = Sass(CapMultipointOn);
        var second = Sass(CapMultipointOn);
        var channels = new Queue<FakeRfcommChannel>([first, second]);
        var transports = new FakeRfcommTransports((_, _) =>
        {
            var next = channels.Dequeue();
            next.Emit(next == first ? FastPairCaptured.Greeting : FastPairCaptured.Battery50_40);
            return Task.FromResult<IRfcommChannel?>(next);
        });
        var features = await Driver().OpenAsync(Context(transports));

        await ((FastPairBatteryFeature)features[0]).RefreshAsync();

        Assert.Equal([FastPairMessages.SassGetCapability, FastPairMessages.SassGetConnectionStatus], second.Written);
        FastPairWrites.AssertAllowed(first, second);
    }
}

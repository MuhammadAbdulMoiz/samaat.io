using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Core.Commands;
using Samaat.Core.Equalizer;
using Samaat.Core.NoiseControl;
using Samaat.Protocols.Jieli;
using Samaat.Transport;

namespace Samaat.Protocols.Tests;

public sealed class RcspAuthTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(100);

    private static async Task<bool> Auth(FakeBuds buds, CancellationToken cancellationToken = default)
    {
        var inbox = Channel.CreateUnbounded<byte[]>();
        await buds.Notify.SubscribeAsync(chunk => inbox.Writer.TryWrite(chunk));
        return await RcspAuth.RunAsync(buds.Write, inbox.Reader, Short, cancellationToken);
    }

    [Fact]
    public async Task AuthenticatesAgainstTheBuds() => Assert.True(await Auth(new FakeBuds()));

    [Fact]
    public async Task WrongCipherReply_Fails() => Assert.False(await Auth(new FakeBuds(BudsMode.WrongCipher)));

    [Fact]
    public async Task Silence_FailsWithinTheStepTimeout()
    {
        var started = DateTime.UtcNow;

        Assert.False(await Auth(new FakeBuds(BudsMode.Silent)));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task FailedWrite_Fails()
    {
        var buds = new FakeBuds();
        buds.Write.OnWrite = _ => false;

        Assert.False(await Auth(buds));
    }
}

public sealed class JieliBatteryFeatureTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);

    private static Task<JieliBatteryFeature?> Create(FakeBuds buds, FakeTimeProvider? time = null, CancellationToken cancellationToken = default) =>
        JieliBatteryFeature.CreateAsync(new RcspSession(buds.Write, buds.Notify, Short, Short), time ?? new FakeTimeProvider(), cancellationToken);

    [Fact]
    public async Task DescribesAnUnverifiedDeviceProtocolSourceWithoutClaimingToBeExhaustive()
    {
        await using var feature = (await Create(new FakeBuds()))!;

        Assert.Equal(
            new[] { BatteryComponent.Left, BatteryComponent.Right, BatteryComponent.Case }.ToHashSet(),
            feature.Components.ToHashSet());
        Assert.False(feature.ComponentsAreExhaustive);
        Assert.Equal(CapabilitySource.RuntimeQuery, feature.Source);
        Assert.Equal(Verification.Unverified, feature.Verification);
    }

    [Fact]
    public async Task InitialPush_GivesLeftAndRightButNoCase()
    {
        var time = new FakeTimeProvider();
        await using var feature = (await Create(new FakeBuds(), time))!;

        Assert.Equal(100, feature.Current.Get(BatteryComponent.Left)!.Percent);
        Assert.Equal(100, feature.Current.Get(BatteryComponent.Right)!.Percent);
        Assert.Null(feature.Current.Get(BatteryComponent.Case));
        Assert.Equal(time.GetUtcNow(), feature.Current.Get(BatteryComponent.Left)!.UpdatedAt);
    }

    [Fact]
    public async Task LaterPush_UpdatesCurrentAndRaisesChanged()
    {
        var buds = new FakeBuds();
        await using var feature = (await Create(buds))!;
        BatteryState? seen = null;
        feature.Changed += (_, state) => seen = state;

        buds.Push(Captured.AdvInfoWith(0x80 | 50, 40, 30));

        Assert.NotNull(seen);
        Assert.Equal(50, feature.Current.Get(BatteryComponent.Left)!.Percent);
        Assert.True(feature.Current.Get(BatteryComponent.Left)!.IsCharging);
        Assert.Equal(ChargingState.NotCharging, feature.Current.Get(BatteryComponent.Right)!.Charging);
        Assert.Equal(40, feature.Current.Get(BatteryComponent.Right)!.Percent);
        Assert.Equal(30, feature.Current.Get(BatteryComponent.Case)!.Percent);
    }

    [Fact]
    public async Task Refresh_ResubscribesAndReauthenticates()
    {
        var buds = new FakeBuds();
        await using var feature = (await Create(buds))!;
        buds.Push(Captured.AdvInfoWith(50, 40, 0));

        await feature.RefreshAsync();

        Assert.Equal(2, buds.AuthStarts);
        Assert.Equal(100, feature.Current.Get(BatteryComponent.Left)!.Percent);
        Assert.True(buds.Notify.IsSubscribed);
    }

    [Fact]
    public async Task Refresh_AfterAuthFailureDoesNotThrow()
    {
        var buds = new FakeBuds();
        await using var feature = (await Create(buds))!;
        buds.Mode = BudsMode.Silent;

        await feature.RefreshAsync();

        Assert.Equal(100, feature.Current.Get(BatteryComponent.Left)!.Percent);
    }

    [Fact]
    public async Task CommandsThatExpectAResponse_AreIgnoredWithoutWriting()
    {
        var buds = new FakeBuds();
        await using var feature = (await Create(buds))!;
        var writes = buds.Writes.Count;

        buds.Notify.Notify(Convert.FromHexString("FEDCBAC0070005030000000" + "0EF"));

        Assert.Equal(writes, buds.Writes.Count);
    }

    [Fact]
    public async Task Dispose_Unsubscribes()
    {
        var buds = new FakeBuds();
        var feature = (await Create(buds))!;

        await feature.DisposeAsync();

        Assert.False(buds.Notify.IsSubscribed);
    }

    [Theory]
    [InlineData(BudsMode.WrongCipher)]
    [InlineData(BudsMode.Silent)]
    [InlineData(BudsMode.NoPush)]
    [InlineData(BudsMode.MalformedPush)]
    [InlineData(BudsMode.NonBatteryPush)]
    public async Task WithoutAValidInitialBatteryPush_NoFeatureIsReturnedAndTheSubscriptionIsReleased(BudsMode mode)
    {
        var buds = new FakeBuds(mode);
        using var expiry = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        var feature = await Create(buds, cancellationToken: expiry.Token);

        Assert.Null(feature);
        Assert.False(buds.Notify.IsSubscribed);
    }
}

public sealed class JieliDriverTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);

    private static readonly CatalogEntry Entry = new(
        "audionic.paradox-e", "Audionic ParadoX-E", "Audionic", JieliDriver.DriverId,
        new DeviceMatch("^ParadoX-E$", 0x05D6, 0x000A), false, [], new HashSet<FeatureId>());

    private static DriverContext Context(IGattClient? gatt) =>
        new(new DeviceSignals("ParadoX-E"), Entry, new FakeTransports(gatt), TimeProvider.System);

    private static async Task<IReadOnlyList<IFeature>> Open(IGattClient? gatt, int budgetMs = 500)
    {
        using var budget = new CancellationTokenSource(budgetMs);
        return await new JieliDriver(Short, Short).OpenAsync(Context(gatt), budget.Token);
    }

    [Fact]
    public void Identifies() => Assert.Equal(("jieli-rcsp", "Jieli RCSP"), (new JieliDriver().Id, new JieliDriver().DisplayName));

    [Fact]
    public async Task NoGatt_GivesNoFeatures() => Assert.Empty(await Open(null));

    [Fact]
    public async Task NoControlService_GivesNoFeatures() => Assert.Empty(await Open(new FakeJieliGatt()));

    [Fact]
    public async Task ServiceWithoutTheNotifyCharacteristic_GivesNoFeatures()
    {
        var write = new FakeJieliCharacteristic(JieliGatt.Write, canWrite: true, canNotify: false);
        Assert.Empty(await Open(new FakeJieliGatt(new FakeJieliService(JieliGatt.Service, write))));
    }

    [Fact]
    public async Task HappyPath_GivesBatteryNoiseControlAndEqualizer()
    {
        var buds = new FakeBuds();
        var features = await Open(buds.Gatt);

        var battery = Assert.Single(features.OfType<IBatteryFeature>());
        Assert.Equal(100, battery.Current.Get(BatteryComponent.Left)!.Percent);
        Assert.Equal(NoiseMode.Off, Assert.Single(features.OfType<INoiseControlFeature>()).Current.Mode);
        Assert.Equal(3, Assert.Single(features.OfType<IEqualizerFeature>()).CurrentPresetId);
        Assert.Equal(3, features.Count);

        foreach (var feature in features.OfType<IAsyncDisposable>())
        {
            await feature.DisposeAsync();
        }

        Assert.False(buds.Notify.IsSubscribed);
    }

    [Fact]
    public async Task BudsThatIgnoreSysInfo_GiveBatteryOnly()
    {
        var features = await Open(new FakeBuds(BudsMode.SilentSysInfo).Gatt, budgetMs: 1500);

        Assert.IsAssignableFrom<IBatteryFeature>(Assert.Single(features));
        await ((IAsyncDisposable)features[0]).DisposeAsync();
    }

    [Fact]
    public async Task DiscoveryThatNeverAnswers_GivesNoFeaturesOnceTheBudgetExpires()
    {
        var gatt = new FakeBuds().Gatt;
        gatt.OnGetServices = ct => Task.Delay(Timeout.Infinite, ct);
        var started = DateTime.UtcNow;

        Assert.Empty(await Open(gatt, budgetMs: 150));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData(BudsMode.NoPush)]
    [InlineData(BudsMode.MalformedPush)]
    [InlineData(BudsMode.NonBatteryPush)]
    [InlineData(BudsMode.Silent)]
    public async Task AuthOkButNoBatteryData_GivesNoFeaturesAndReleasesTheSubscription(BudsMode mode)
    {
        var buds = new FakeBuds(mode);

        Assert.Empty(await Open(buds.Gatt, budgetMs: 300));
        Assert.False(buds.Notify.IsSubscribed);
    }


    // The earbuds are real hardware. After the auth handshake only these frames may ever be written:
    // GetSysInfo with one of four masks, SetSysInfo noise mode with a VoiceMode the buds listed, and
    // SetSysInfo EQ preset with exactly the gains the buds listed for that mode. Nothing else, and
    // never an op in 0xD0-0xEF (app info, MTU, OTA update, reboot). Do not loosen this to "any frame".
    [Fact]
    public async Task EveryWrite_IsAnAuthStepOrAnAllowlistedRcspFrame()
    {
        var all = new List<byte[]>();

        foreach (var mode in Enum.GetValues<BudsMode>())
        {
            var buds = new FakeBuds(mode);
            using var budget = new CancellationTokenSource(1500);
            var features = await new JieliDriver(Short, Short).OpenAsync(Context(buds.Gatt), budget.Token);

            foreach (var feature in features)
            {
                switch (feature)
                {
                    case INoiseControlFeature noise:
                        foreach (var noiseMode in Enum.GetValues<NoiseMode>())
                        {
                            await noise.SetModeAsync(noiseMode);
                        }

                        await noise.SetAncLevelAsync(3);
                        await noise.SetAmbientLevelAsync(3);
                        await noise.SetWindReductionAsync(true);
                        await noise.SetConversationAwarenessAsync(true);
                        break;
                    case IEqualizerFeature eq:
                        for (var id = -1; id <= 8; id++)
                        {
                            await eq.SetPresetAsync(id);
                        }

                        break;
                    case JieliBatteryFeature battery:
                        await battery.RefreshAsync();
                        break;
                }
            }

            foreach (var feature in features.OfType<IAsyncDisposable>())
            {
                await feature.DisposeAsync();
            }

            all.AddRange(buds.Writes);
        }

        Assert.NotEmpty(all);
        Assert.Contains(all, w => w.Length > 4 && w[4] == 0x08);
        Assert.All(all, AssertAllowed);
    }

    private static void AssertAllowed(byte[] write)
    {
        var isAuth = (write.Length == 17 && write[0] is 0x00 or 0x01) || write.AsSpan().SequenceEqual("\u0002pass"u8);
        if (isAuth)
        {
            return;
        }

        var hex = Convert.ToHexString(write);
        var packet = Assert.Single(new RcspFrameParser().Feed(write));
        Assert.Equal(write, RcspFrame.Build(packet.Flags, packet.Op, packet.Sn, packet.Params));
        Assert.False(packet.Op is >= 0xD0 and <= 0xEF, "forbidden op: " + hex);
        Assert.Equal(0xC0, packet.Flags);
        var p = packet.Params;
        Assert.Equal(0xFF, p[0]);

        switch (packet.Op)
        {
            case 0x07:
                Assert.Equal(5, p.Length);
                Assert.Contains(((uint)p[1] << 24) | ((uint)p[2] << 16) | ((uint)p[3] << 8) | p[4], new uint[] { 0x6000, 0x2000, 0x1010, 0x0010 });
                break;
            case 0x08 when p.Length == 12 && p[1] == 0x0A && p[2] == 0x0D:
                Assert.Contains(Captured.Modes, m => m.AsSpan().SequenceEqual(p[3..]));
                break;
            case 0x08 when p.Length == 14 && p[1] == 0x0C && p[2] == 0x04:
                Assert.True(p[3] <= 6, hex);
                Assert.Equal(Captured.PresetGains[p[3]], p[4..].Select(b => (sbyte)b));
                break;
            default:
                Assert.Fail("not allowlisted: " + hex);
                break;
        }
    }
}

public sealed class RcspSessionTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);

    private static RcspSession NewSession(FakeBuds buds) => new(buds.Write, buds.Notify, Short, Short);

    [Fact]
    public async Task Send_ReturnsStatusAndParamsAndIncrementsTheSequenceNumber()
    {
        var buds = new FakeBuds();
        await using var session = NewSession(buds);
        await session.StartAsync(default);
        var read = RcspCommands.GetSysInfo(RcspCommands.MaskNoiseCurrent)!;

        var first = await session.SendAsync(read, default);
        var second = await session.SendAsync(read, default);

        Assert.Equal(0, first!.Status);
        Assert.Equal(0xFF, first.Params[0]);
        Assert.NotNull(second);
        var sns = buds.Commands.Select(c => c[7]).ToList();
        Assert.Equal([(byte)0, 1], sns);
    }

    // A command written mid-handshake would be read as an auth reply and break the reconnect.
    [Fact]
    public async Task SendDuringRestart_IsNotWrittenUntilTheHandshakeFinishes()
    {
        var buds = new FakeBuds();
        await using var session = NewSession(buds);
        await session.StartAsync(default);
        var read = RcspCommands.GetSysInfo(RcspCommands.MaskNoiseCurrent)!;
        buds.Writes.Clear();

        var restart = session.RestartAsync(default);
        var send = session.SendAsync(read, default);

        Assert.True(await restart);
        Assert.NotNull(await send);
        var lastAuth = buds.Writes.FindLastIndex(w => w.Length == 17 && w[0] == 0x01);
        var firstCommand = buds.Writes.FindIndex(w => w[0] == 0xFE);
        Assert.True(lastAuth >= 0 && firstCommand > lastAuth, string.Join(" ", buds.Writes.Select(Convert.ToHexString)));
    }

    [Fact]
    public async Task Send_WithNoAnswer_TimesOutWithNull()
    {
        var buds = new FakeBuds(BudsMode.SilentSysInfo);
        await using var session = NewSession(buds);
        await session.StartAsync(default);
        var read = RcspCommands.GetSysInfo(RcspCommands.MaskNoiseCurrent)!;

        Assert.Null(await session.SendAsync(read, default));
    }

    [Fact]
    public async Task Send_WhenTheWriteFails_IsNull()
    {
        var buds = new FakeBuds();
        await using var session = NewSession(buds);
        await session.StartAsync(default);
        buds.Write.OnWrite = _ => false;
        var read = RcspCommands.GetSysInfo(RcspCommands.MaskNoiseCurrent)!;

        Assert.Null(await session.SendAsync(read, default));
    }

    [Fact]
    public async Task DeviceCommands_GoToPacketAndAreNeverAnswered()
    {
        var buds = new FakeBuds();
        await using var session = NewSession(buds);
        var seen = new List<RcspPacket>();
        session.Packet += seen.Add;
        await session.StartAsync(default);
        var writes = buds.Writes.Count;

        buds.Notify.Notify(Convert.FromHexString("FEDCBAC0C400020204EF"));

        Assert.Contains(seen, p => p.Op == 0xC4);
        Assert.Contains(seen, p => p.Op == 0xC2);
        Assert.Equal(writes, buds.Writes.Count);
    }

    [Fact]
    public async Task Authenticated_IsRaisedOnStartAndOnEveryRestart()
    {
        var buds = new FakeBuds();
        await using var session = NewSession(buds);
        var count = 0;
        session.Authenticated += () => count++;

        await session.StartAsync(default);
        await session.RestartAsync(default);

        Assert.Equal(2, count);
        Assert.Equal(2, buds.AuthStarts);
    }

    [Fact]
    public async Task Dispose_UnsubscribesIsIdempotentAndStopsSending()
    {
        var buds = new FakeBuds();
        var session = NewSession(buds);
        await session.StartAsync(default);

        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.False(buds.Notify.IsSubscribed);
        var read = RcspCommands.GetSysInfo(RcspCommands.MaskNoiseCurrent)!;
        Assert.Null(await session.SendAsync(read, default));
        Assert.False(await session.StartAsync(default));
    }
}

public sealed class JieliNoiseControlFeatureTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);

    private static async Task<(RcspSession Session, JieliNoiseControlFeature? Feature)> Open(FakeBuds buds)
    {
        var session = new RcspSession(buds.Write, buds.Notify, Short, Short);
        await session.StartAsync(default);
        return (session, await JieliNoiseControlFeature.CreateAsync(session, default));
    }

    [Fact]
    public async Task Options_AreTheListedModesInDeviceOrder()
    {
        var (session, feature) = await Open(new FakeBuds());
        await using var _ = session;

        Assert.Equal([NoiseMode.Off, NoiseMode.NoiseCancelling, NoiseMode.Transparency], feature!.Options.Modes);
        Assert.True(feature.OptionsAreExhaustive);
        Assert.Null(feature.Options.AncLevel);
        Assert.Null(feature.Options.AmbientLevel);
        Assert.False(feature.Options.WindReduction);
        Assert.False(feature.Options.ConversationAwareness);
        Assert.Equal(CapabilitySource.RuntimeQuery, feature.Source);
        Assert.Equal(Verification.Unverified, feature.Verification);
        Assert.Equal(NoiseMode.Off, feature.Current.Mode);
    }

    [Fact]
    public async Task CurrentMode_IsReadFromTheBuds()
    {
        var (session, feature) = await Open(new FakeBuds { Noise = 2 });
        await using var _ = session;

        Assert.Equal(NoiseMode.Transparency, feature!.Current.Mode);
    }

    [Theory]
    [InlineData(BudsMode.SilentSysInfo)]
    [InlineData(BudsMode.BrokenReadback)]
    public async Task BudsThatDontAnswer_GiveNoFeature(BudsMode mode)
    {
        var buds = new FakeBuds(mode);
        var (session, feature) = await Open(buds);
        await using var _ = session;

        Assert.Equal(mode == BudsMode.SilentSysInfo, feature is null);
    }

    [Fact]
    public async Task SetMode_WritesTheExactFrameAndConfirms()
    {
        var buds = new FakeBuds();
        var (session, feature) = await Open(buds);
        await using var _ = session;
        var seen = new List<NoiseControlState>();
        feature!.Changed += (_, state) => seen.Add(state);

        var result = await feature.SetModeAsync(NoiseMode.NoiseCancelling);

        Assert.Equal(CommandStatus.Confirmed, result.Status);
        var frame = buds.Commands.Single(c => c[4] == 0x08);
        Assert.Equal("C008000D", Convert.ToHexString(frame[3..7]));
        Assert.Equal("FF0A0D014000400040004000EF", Convert.ToHexString(frame[8..]));
        Assert.Equal(NoiseMode.NoiseCancelling, feature.Current.Mode);
        Assert.Contains(seen, s => s.Mode == NoiseMode.NoiseCancelling);
    }

    [Fact]
    public async Task SetMode_RefusedByTheBuds_IsRejectedAndKeepsTheState()
    {
        var buds = new FakeBuds(BudsMode.SetRejected);
        var (session, feature) = await Open(buds);
        await using var _ = session;

        var result = await feature!.SetModeAsync(NoiseMode.NoiseCancelling);

        Assert.Equal(CommandStatus.Rejected, result.Status);
        Assert.Equal(NoiseMode.Off, feature.Current.Mode);
    }

    [Fact]
    public async Task SetMode_WithNoAnswer_TimesOutAndKeepsTheState()
    {
        var buds = new FakeBuds();
        var (session, feature) = await Open(buds);
        await using var _ = session;
        buds.Mode = BudsMode.SilentSets;

        var result = await feature!.SetModeAsync(NoiseMode.Transparency);

        Assert.Equal(CommandStatus.TimedOut, result.Status);
        Assert.Equal(NoiseMode.Off, feature.Current.Mode);
    }

    [Fact]
    public async Task SetMode_WhenTheReadbackDiffers_IsRejectedAndShowsWhatTheBudsReport()
    {
        var buds = new FakeBuds();
        var (session, feature) = await Open(buds);
        await using var _ = session;
        buds.Mode = BudsMode.ReadbackMismatch;
        buds.Noise = 1;

        var result = await feature!.SetModeAsync(NoiseMode.Off);

        Assert.Equal(CommandStatus.Rejected, result.Status);
        Assert.Equal(NoiseMode.NoiseCancelling, feature.Current.Mode);
    }

    [Fact]
    public async Task SetMode_WhenTheReadbackIsMissing_TimesOutAndKeepsTheState()
    {
        var buds = new FakeBuds();
        var (session, feature) = await Open(buds);
        await using var _ = session;
        buds.Mode = BudsMode.BrokenReadback;

        var result = await feature!.SetModeAsync(NoiseMode.NoiseCancelling);

        Assert.Equal(CommandStatus.TimedOut, result.Status);
        Assert.Equal(NoiseMode.Off, feature.Current.Mode);
    }

    [Fact]
    public async Task UnsupportedRequests_WriteNothing()
    {
        var buds = new FakeBuds();
        var (session, feature) = await Open(buds);
        await using var _ = session;
        var writes = buds.Writes.Count;

        var results = new[]
        {
            await feature!.SetModeAsync(NoiseMode.Adaptive),
            await feature.SetAncLevelAsync(3),
            await feature.SetAmbientLevelAsync(3),
            await feature.SetWindReductionAsync(true),
            await feature.SetConversationAwarenessAsync(true),
        };

        Assert.All(results, r => Assert.Equal(CommandStatus.Unsupported, r.Status));
        Assert.Equal(writes, buds.Writes.Count);
    }

    [Fact]
    public async Task DevicePush_UpdatesCurrentAndRaisesChanged()
    {
        var buds = new FakeBuds();
        var (session, feature) = await Open(buds);
        await using var _ = session;
        NoiseControlState? seen = null;
        feature!.Changed += (_, state) => seen = state;

        buds.PushNoise(2);

        Assert.Equal(NoiseMode.Transparency, seen!.Mode);
        Assert.Equal(NoiseMode.Transparency, feature.Current.Mode);
    }

    [Fact]
    public async Task UnrelatedPush_IsIgnored()
    {
        var buds = new FakeBuds();
        var (session, feature) = await Open(buds);
        await using var _ = session;
        var raised = false;
        feature!.Changed += (_, _) => raised = true;

        buds.Notify.Notify(Captured.OtherFrame);

        Assert.False(raised);
        Assert.Equal(NoiseMode.Off, feature.Current.Mode);
    }

    [Fact]
    public async Task APushBetweenTheCommandAndItsResponse_StillConfirms()
    {
        var buds = new FakeBuds { InterleavePushes = true };
        var (session, feature) = await Open(buds);
        await using var _ = session;

        var result = await feature!.SetModeAsync(NoiseMode.Transparency);

        Assert.Equal(CommandStatus.Confirmed, result.Status);
        Assert.Equal(NoiseMode.Transparency, feature.Current.Mode);
    }

    [Fact]
    public async Task DisposingTheFeature_DetachesButLeavesTheSessionAlone()
    {
        var buds = new FakeBuds();
        var (session, feature) = await Open(buds);
        await using var _ = session;

        await feature!.DisposeAsync();
        buds.PushNoise(1);

        Assert.Equal(NoiseMode.Off, feature.Current.Mode);
        Assert.True(buds.Notify.IsSubscribed);
    }
}

public sealed class JieliEqualizerFeatureTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);

    private static async Task<(RcspSession Session, JieliEqualizerFeature? Feature)> Open(FakeBuds buds)
    {
        var session = new RcspSession(buds.Write, buds.Notify, Short, Short);
        await session.StartAsync(default);
        return (session, await JieliEqualizerFeature.CreateAsync(session, default));
    }

    [Fact]
    public async Task Presets_AreTheSevenSdkNamedModes_AndCurrentIsClassical()
    {
        var (session, feature) = await Open(new FakeBuds());
        await using var _ = session;

        Assert.Equal(
            ["Standard", "Rock", "Pop", "Classical", "Jazz", "Country", "Custom"],
            feature!.Presets.Select(p => p.Name));
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], feature.Presets.Select(p => p.Id));
        Assert.Equal(3, feature.CurrentPresetId);
        Assert.Equal(CapabilitySource.RuntimeQuery, feature.Source);
        Assert.Equal(Verification.Unverified, feature.Verification);
    }

    [Fact]
    public async Task BudsThatIgnoreSysInfo_GiveNoFeature()
    {
        var (session, feature) = await Open(new FakeBuds(BudsMode.SilentSysInfo));
        await using var _ = session;

        Assert.Null(feature);
    }

    [Fact]
    public async Task SetPreset_WritesTheExactFrameAndConfirms()
    {
        var buds = new FakeBuds();
        var (session, feature) = await Open(buds);
        await using var _ = session;
        int? seen = null;
        feature!.Changed += (_, id) => seen = id;

        var result = await feature.SetPresetAsync(1);

        Assert.Equal(CommandStatus.Confirmed, result.Status);
        var frame = buds.Commands.Single(c => c[4] == 0x08);
        Assert.Equal("C008000F", Convert.ToHexString(frame[3..7]));
        Assert.Equal("FF0C0401FE000204FEFE00000404EF", Convert.ToHexString(frame[8..]));
        Assert.Equal(1, feature.CurrentPresetId);
        Assert.Equal(1, seen);
    }

    [Fact]
    public async Task SetPreset_RefusedByTheBuds_IsRejected()
    {
        var buds = new FakeBuds();
        var (session, feature) = await Open(buds);
        await using var _ = session;
        buds.Mode = BudsMode.SetRejected;

        var result = await feature!.SetPresetAsync(1);

        Assert.Equal(CommandStatus.Rejected, result.Status);
        Assert.Equal(3, feature.CurrentPresetId);
    }

    [Fact]
    public async Task SetPreset_WithNoAnswer_TimesOutAndKeepsTheState()
    {
        var buds = new FakeBuds();
        var (session, feature) = await Open(buds);
        await using var _ = session;
        buds.Mode = BudsMode.SilentSets;

        var result = await feature!.SetPresetAsync(1);

        Assert.Equal(CommandStatus.TimedOut, result.Status);
        Assert.Equal(3, feature.CurrentPresetId);
    }

    [Fact]
    public async Task SetPreset_WhenTheReadbackDiffers_IsRejectedAndShowsWhatTheBudsReport()
    {
        var buds = new FakeBuds();
        var (session, feature) = await Open(buds);
        await using var _ = session;
        buds.Mode = BudsMode.ReadbackMismatch;
        buds.Eq = 4;

        var result = await feature!.SetPresetAsync(1);

        Assert.Equal(CommandStatus.Rejected, result.Status);
        Assert.Equal(4, feature.CurrentPresetId);
    }

    [Fact]
    public async Task SetPreset_WhenTheReadbackIsMissing_TimesOutAndKeepsTheState()
    {
        var buds = new FakeBuds();
        var (session, feature) = await Open(buds);
        await using var _ = session;
        buds.Mode = BudsMode.BrokenReadback;

        var result = await feature!.SetPresetAsync(1);

        Assert.Equal(CommandStatus.TimedOut, result.Status);
        Assert.Equal(3, feature.CurrentPresetId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(200)]
    public async Task UnknownPreset_IsUnsupportedAndWritesNothing(int id)
    {
        var buds = new FakeBuds();
        var (session, feature) = await Open(buds);
        await using var _ = session;
        var writes = buds.Writes.Count;

        var result = await feature!.SetPresetAsync(id);

        Assert.Equal(CommandStatus.Unsupported, result.Status);
        Assert.Equal(writes, buds.Writes.Count);
    }

    [Fact]
    public async Task DevicePush_UpdatesTheCurrentPreset()
    {
        var buds = new FakeBuds();
        var (session, feature) = await Open(buds);
        await using var _ = session;
        int? seen = null;
        feature!.Changed += (_, id) => seen = id;

        buds.PushEq(5);

        Assert.Equal(5, seen);
        Assert.Equal(5, feature.CurrentPresetId);
    }
}

public sealed class JieliSharedSessionTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);

    private static async Task<bool> Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return false;
    }

    [Fact]
    public async Task ConcurrentNoiseAndEqSets_BothComplete()
    {
        var buds = new FakeBuds();
        var session = new RcspSession(buds.Write, buds.Notify, Short, Short);
        await using var _ = session;
        await session.StartAsync(default);
        var noise = (await JieliNoiseControlFeature.CreateAsync(session, default))!;
        var eq = (await JieliEqualizerFeature.CreateAsync(session, default))!;

        var results = await Task.WhenAll(
            noise.SetModeAsync(NoiseMode.NoiseCancelling),
            eq.SetPresetAsync(1),
            noise.SetModeAsync(NoiseMode.Transparency),
            eq.SetPresetAsync(2));

        Assert.All(results, r => Assert.Equal(CommandStatus.Confirmed, r.Status));
        Assert.Equal(NoiseMode.Transparency, noise.Current.Mode);
        Assert.Equal(2, eq.CurrentPresetId);
        Assert.Equal((byte)2, buds.Noise);
        Assert.Equal((byte)2, buds.Eq);
    }

    [Fact]
    public async Task DisposingTheNoiseFeature_LeavesTheEqualizerWorking()
    {
        var buds = new FakeBuds();
        var session = new RcspSession(buds.Write, buds.Notify, Short, Short);
        await using var _ = session;
        await session.StartAsync(default);
        var noise = (await JieliNoiseControlFeature.CreateAsync(session, default))!;
        var eq = (await JieliEqualizerFeature.CreateAsync(session, default))!;

        await noise.DisposeAsync();
        var result = await eq.SetPresetAsync(1);

        Assert.Equal(CommandStatus.Confirmed, result.Status);
        Assert.Equal(1, eq.CurrentPresetId);
    }

    [Fact]
    public async Task BatteryRefresh_ReauthenticatesAndNoiseAndEqReRead()
    {
        var buds = new FakeBuds();
        var session = new RcspSession(buds.Write, buds.Notify, Short, Short);
        var battery = (await JieliBatteryFeature.CreateAsync(session, TimeProvider.System, default))!;
        await using var _ = battery;
        var noise = (await JieliNoiseControlFeature.CreateAsync(session, default))!;
        var eq = (await JieliEqualizerFeature.CreateAsync(session, default))!;
        buds.Noise = 1;
        buds.Eq = 4;

        await battery.RefreshAsync();

        Assert.Equal(2, buds.AuthStarts);
        Assert.True(await Eventually(() => noise.Current.Mode == NoiseMode.NoiseCancelling && eq.CurrentPresetId == 4));
    }

    [Fact]
    public async Task DisposingTheBattery_ReleasesTheSharedSession()
    {
        var buds = new FakeBuds();
        var session = new RcspSession(buds.Write, buds.Notify, Short, Short);
        var battery = (await JieliBatteryFeature.CreateAsync(session, TimeProvider.System, default))!;

        await battery.DisposeAsync();

        Assert.False(buds.Notify.IsSubscribed);
    }
}


public sealed class ParadoxCatalogTests
{
    private static string DevicesFolder()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Samaat.slnx")))
        {
            root = root.Parent;
        }

        return Path.Combine(root!.FullName, "devices");
    }

    private static DriverRegistry Registry()
    {
        var registry = new DriverRegistry(DeviceCatalog.FromDirectory(DevicesFolder()));
        registry.Register(new JieliDriver());
        return registry;
    }

    // Signals the way WindowsDeviceSource builds them: name, class of device, PnP VID/PID, SDP services.
    private static DeviceSignals Windows(string name) =>
        new(name, 0x240404, 0x05D6, 0x000A, new HashSet<Guid> { BluetoothUuid.FromShort(0x110B) });

    [Fact]
    public void ParadoxE_ResolvesToTheJieliDriver()
    {
        var catalog = DeviceCatalog.FromDirectory(DevicesFolder());
        var resolution = Registry().Resolve(Windows("ParadoX-E"));

        Assert.Empty(catalog.Errors);
        Assert.Equal(DriverResolutionKind.Driver, resolution.Kind);
        Assert.Equal("jieli-rcsp", resolution.Driver!.Id);
        Assert.Equal("audionic.paradox-e", resolution.Entry!.Id);
        Assert.Contains(FeatureId.BatteryLeft, resolution.Entry.Features);
        Assert.Contains(FeatureId.BatteryCase, resolution.Entry.Features);
    }

    [Fact]
    public void Quantum_StillResolvesToItsOwnEntry()
    {
        var resolution = Registry().Resolve(Windows("Quantum"));

        Assert.Equal("zero-lifestyle.quantum-zbuds", resolution.Entry!.Id);
        Assert.NotEqual(DriverResolutionKind.Driver, resolution.Kind);
    }
}

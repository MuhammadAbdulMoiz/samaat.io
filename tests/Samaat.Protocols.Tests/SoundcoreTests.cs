// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/Oppzippy/OpenSCQ30 @ e75a68b70de8eaf2cf0ca364e132085a097557c5, lib/src/devices/soundcore/a3028.rs and lib/src/devices/soundcore/a3028/packets/state_update_packet.rs and lib/src/devices/soundcore/common/packet/{inbound,outbound}/*.rs (test vectors), Copyright (c) 2023-2026 Kyle Scheuing and contributors, GPL-3.0.
// Changes: rewritten in C# onto Samaat.Transport (IRfcommChannel) and Samaat.Core features.

using Microsoft.Extensions.Time.Testing;
using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Core.Equalizer;
using Samaat.Core.NoiseControl;
using Samaat.Drivers;
using Samaat.Protocols.Soundcore;
using Samaat.Transport;

namespace Samaat.Protocols.Tests;

/// <summary>
/// Upstream test vectors from OpenSCQ30 @ e75a68b7 (GPL-3.0), not Samaat captures. Anything else is labelled SYNTHETIC.
/// </summary>
internal static class SoundcoreVectors
{
    // a3028.rs example_state_update_packet: custom EQ (0xFEFE), level 5, not charging, ambient 2 (normal).
    public const string Q30Example = "09ff000001010146000500fefe3cb48fa08eb4748800000000000000000000000000000000000000000000000200010030322e33303330323930383645433832463132414330";

    // a3028/packets/state_update_packet.rs it_parses_packet_with_preset_eq: EQ preset 1.
    public const string Q30PresetEq = "09ff00000101014600050001003cb48fa08eb4748800000000000000000000000000000000000000000000000200010030322e33303330323930383645433832463132414335";

    // it_parses_packet_with_a_4_at_byte_offset_9: level 4.
    public const string Q30Level4 = "09ff000001010146000400fefe3cb48fa08eb4748800000000000000000000000000000000000000000000000200010030322e3330333032393038364543383246313241432f";

    // it_parses_packet_from_github_issue_141: level 5, EQ 14, ambient 0.
    public const string Q30Issue141 = "09ff00000101014d0005000e007896968ca0aa96a000000000000000000000000000000000000000000000000000000030342e333333303238364443383933343434373938000001010101018a";

    // it_parses_packet_from_flathub_issue_34: the state body only (no frame).
    public const string Q30FlathubBody = "05000000787878787878787800000000000000000000000000000000000000000000010101000030352e31393330323842334243323438353045383800010101000101030403";

    // a3028.rs test_ambient_sound_mode_update_packet: modes [0 (noise cancelling), 1, 1, 0].
    public const string SoundModesNoiseCancelling = "09ff0000010601 0e00 00010100 20";

    // common/packet/inbound/sound_modes.rs it_parses_valid_packet: ambient 2 (normal).
    public const string SoundModesNormal = "09ff00000106010e000202010023";

    // common/packet/inbound/battery_level.rs.
    public const string BatteryLevelDual = "09ff0000010103 0c00 0304 20";
    public const string BatteryLevelDualWithExtras = "09ff0000010103 0e00 04050102 27";
    public const string BatteryLevelSingle = "09ff00000101030b00021a";

    // common/packet/inbound/battery_charging.rs.
    public const string ChargingDual = "09ff00000101040c0001001b";
    public const string ChargingSingle = "09ff00000101040b00011a";

    public static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    /// <summary>An inbound frame with a valid length and checksum.</summary>
    public static byte[] Inbound(byte c0, byte c1, params byte[] body)
    {
        var length = 10 + body.Length;
        byte[] frame = [0x09, 0xFF, 0x00, 0x00, 0x01, c0, c1, (byte)length, (byte)(length >> 8), .. body];
        return [.. frame, (byte)frame.Sum(b => b)];
    }

    public static byte[] RequestState => Hex("08ee000000 0101 0a00 02");
}

public sealed class SoundcoreTests
{
    private static readonly byte[] RequestStateFrame = SoundcoreVectors.RequestState;

    private static SoundcoreMessage Single(string hex) => Assert.Single(new SoundcoreReader().Feed(SoundcoreVectors.Hex(hex)));

    private static SoundcoreModel ModelOf(string entry)
    {
        Assert.True(SoundcoreModels.TryGet(entry, out var model));
        return model;
    }

    // ---- codec ----

    [Fact]
    public void RequestState_EncodesLikeTheUpstreamExample()
    {
        Assert.Equal(RequestStateFrame, SoundcoreFrame.Encode(0x01, 0x01, []));
    }

    [Fact]
    public void SetSoundModes_EncodesLikeTheUpstreamExample()
    {
        Assert.Equal(SoundcoreVectors.Hex("08ee000000 0681 0e00 02000100 8e"), SoundcoreFrame.Encode(0x06, 0x81, [2, 0, 1, 0]));
    }

    [Fact]
    public void Reader_DecodesAnUpstreamFrame()
    {
        var message = Single(SoundcoreVectors.BatteryLevelSingle);

        Assert.Equal((0x01, 0x03), (message.C0, message.C1));
        Assert.Equal([0x02], message.Body);
    }

    [Fact]
    public void Reader_ReassemblesSplitChunks()
    {
        var frame = SoundcoreVectors.Hex(SoundcoreVectors.BatteryLevelDual);
        var reader = new SoundcoreReader();

        Assert.Empty(reader.Feed(frame.AsSpan(0, 3)));
        Assert.Empty(reader.Feed(frame.AsSpan(3, 5)));
        Assert.Single(reader.Feed(frame.AsSpan(8)));
    }

    [Fact]
    public void Reader_ReturnsTwoFramesFromOneChunk()
    {
        // SYNTHETIC
        var messages = new SoundcoreReader().Feed([.. SoundcoreVectors.Inbound(1, 3, 1, 2), .. SoundcoreVectors.Inbound(6, 0x81)]);

        Assert.Equal([(0x01, 0x03), (0x06, 0x81)], messages.Select(m => (m.C0, m.C1)));
    }

    [Fact]
    public void Reader_SkipsLeadingGarbage()
    {
        // SYNTHETIC
        var messages = new SoundcoreReader().Feed([0x09, 0x00, 0x11, 0xFF, .. SoundcoreVectors.Inbound(6, 0x81)]);

        Assert.Equal((0x06, 0x81), (Assert.Single(messages).C0, messages[0].C1));
    }

    [Fact]
    public void Reader_ResyncsAfterABadChecksum()
    {
        // SYNTHETIC
        var bad = SoundcoreVectors.Inbound(1, 3, 1, 2);
        bad[^1] ^= 0xFF;

        var messages = new SoundcoreReader().Feed([.. bad, .. SoundcoreVectors.Inbound(6, 0x81)]);

        Assert.Equal(0x06, Assert.Single(messages).C0);
    }

    [Fact]
    public void Reader_RejectsALengthBelowTen()
    {
        // SYNTHETIC: a header whose length says 9, then a real frame.
        byte[] shortLength = [0x09, 0xFF, 0x00, 0x00, 0x01, 0x01, 0x03, 0x09, 0x00, 0x20];

        var messages = new SoundcoreReader().Feed([.. shortLength, .. SoundcoreVectors.Inbound(6, 0x81)]);

        Assert.Equal(0x06, Assert.Single(messages).C0);
    }

    [Fact]
    public void Reader_SurvivesAnOversizedJunkChunk_AndStillFindsTheNextFrame()
    {
        // SYNTHETIC
        var reader = new SoundcoreReader();

        Assert.Empty(reader.Feed(new byte[10_000]));
        Assert.Single(reader.Feed(SoundcoreVectors.Inbound(6, 0x81)));
    }

    [Fact]
    public void Reader_SurvivesAnImpossibleLength_AndStillFindsTheNextFrame()
    {
        // SYNTHETIC: length 0xFFFF would never complete.
        var reader = new SoundcoreReader();
        byte[] huge = [0x09, 0xFF, 0x00, 0x00, 0x01, 0x01, 0x03, 0xFF, 0xFF];

        Assert.Empty(reader.Feed(huge));
        Assert.Single(reader.Feed(SoundcoreVectors.Inbound(6, 0x81)));
    }

    // ---- state parsing ----

    private static SoundcoreState? Parse(string entry, string hex) => SoundcoreState.TryParse(ModelOf(entry), Single(hex));

    [Fact]
    public void Q30Example_ParsesAsLevel5_NotCharging_NoPreset_Normal()
    {
        var state = Parse("soundcore.life-q30", SoundcoreVectors.Q30Example)!;

        Assert.Equal(5, state.Level);
        Assert.Equal(ChargingState.NotCharging, state.Charging);
        Assert.Equal(0xFEFE, state.EqRaw);
        Assert.Equal(2, state.Modes!.Value.Ambient);
    }

    [Fact]
    public void Q30_PresetEq_IsPreset1_Normal()
    {
        var state = Parse("soundcore.life-q30", SoundcoreVectors.Q30PresetEq)!;

        Assert.Equal(1, state.EqRaw);
        Assert.Equal(2, state.Modes!.Value.Ambient);
    }

    [Fact]
    public void Q30_Level4()
    {
        Assert.Equal(4, Parse("soundcore.life-q30", SoundcoreVectors.Q30Level4)!.Level);
    }

    [Fact]
    public void Q30_Issue141_IsLevel5_Preset14_NoiseCancelling()
    {
        var state = Parse("soundcore.life-q30", SoundcoreVectors.Q30Issue141)!;

        Assert.Equal(5, state.Level);
        Assert.Equal(14, state.EqRaw);
        Assert.Equal(0, state.Modes!.Value.Ambient);
    }

    [Fact]
    public void Q30_FlathubIssue34Body_IsLevel5_Preset0_Transparency()
    {
        var message = Assert.Single(new SoundcoreReader().Feed(SoundcoreVectors.Inbound(1, 1, SoundcoreVectors.Hex(SoundcoreVectors.Q30FlathubBody))));
        var state = SoundcoreState.TryParse(ModelOf("soundcore.life-q30"), message)!;

        Assert.Equal(5, state.Level);
        Assert.Equal(0, state.EqRaw);
        Assert.Equal(1, state.Modes!.Value.Ambient);
    }

    [Fact]
    public void State_IsNullWhenTheBodyIsTooShortForTheBattery()
    {
        // SYNTHETIC
        Assert.Null(SoundcoreState.TryParse(ModelOf("soundcore.life-q30"), new SoundcoreMessage(1, 1, [5])));
        Assert.Null(SoundcoreState.TryParse(ModelOf("soundcore.liberty-air-2-pro"), new SoundcoreMessage(1, 1, [0, 0, 3, 4, 1])));
        Assert.Null(SoundcoreState.TryParse(ModelOf("soundcore.space-q45"), new SoundcoreMessage(1, 1, [])));
        Assert.Null(SoundcoreState.TryParse(ModelOf("soundcore.space-q45"), new SoundcoreMessage(1, 3, [4, 0])));
    }

    [Fact]
    public void State_TooShortForTheModel_IsRejected()
    {
        // SYNTHETIC
        Assert.Null(SoundcoreState.TryParse(ModelOf("soundcore.life-q30"), new SoundcoreMessage(1, 1, [5, 1, 0x01])));
    }

    // ---- service selection ----

    private static readonly Guid Spp = Guid.Parse("00001101-0000-1000-8000-00805f9b34fb");

    [Fact]
    public void Service_OneMatchingUuid_IsUsed()
    {
        var match = Guid.Parse("0cf12d31-fac3-4553-bd80-d6832e7ab123");

        Assert.Equal(match, SoundcoreService.Pick(new HashSet<Guid> { Spp, match, Guid.NewGuid() }));
    }

    [Fact]
    public void Service_SeveralMatches_TakeTheLowest()
    {
        var low = Guid.Parse("0cf12d31-fac3-4553-bd80-d6832e700001");
        var high = Guid.Parse("0cf12d31-fac3-4553-bd80-d6832e7fffff");

        Assert.Equal(low, SoundcoreService.Pick(new HashSet<Guid> { high, low }));
    }

    [Fact]
    public void Service_NoMatch_FallsBackToSpp()
    {
        // SYNTHETIC: differs from the Soundcore base above the low 20 bits.
        Assert.Equal(Spp, SoundcoreService.Pick(new HashSet<Guid> { Guid.Parse("0cf12d31-fac3-4553-bd80-d6832e800000") }));
        Assert.Equal(Spp, SoundcoreService.Pick(new HashSet<Guid>()));
    }

    // ---- driver ----

    private static DriverContext Context(string entryId, FakeRfcommTransports transports, TimeProvider time, IReadOnlySet<Guid>? services = null) =>
        new(new DeviceSignals("Soundcore", ServiceUuids: services), new CatalogEntry(entryId, "Soundcore", "Soundcore", "soundcore-spp", new DeviceMatch(), false, [], new HashSet<FeatureId>()), transports, time);

    private sealed record Opened(IReadOnlyList<IFeature> Features, FakeRfcommChannel Channel, FakeRfcommTransports Transports, FakeTimeProvider Time);

    private static async Task<T> Drive<T>(Task<T> task, FakeTimeProvider time, TimeSpan total)
    {
        for (var elapsed = TimeSpan.Zero; elapsed < total && !task.IsCompleted; elapsed += TimeSpan.FromMilliseconds(100))
        {
            time.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(1);
        }

        return await task;
    }

    private static async Task<Opened> OpenAsync(string entry, byte[]? state, bool allowWrites = false)
    {
        var channel = new FakeRfcommChannel();
        if (state is not null)
        {
            channel.Emit(state);
        }

        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));
        var time = new FakeTimeProvider();
        var features = await new SoundcoreDriver(allowWrites).OpenAsync(Context(entry, transports, time));
        return new Opened(features, channel, transports, time);
    }

    private static Task<Opened> OpenQ30Async(bool allowWrites = false) =>
        OpenAsync("soundcore.life-q30", SoundcoreVectors.Hex(SoundcoreVectors.Q30Example), allowWrites);

    // SYNTHETIC: Liberty Air 2 Pro state body: status 2 bytes, L 3, R 4, L charging, R not, EQ 15 at 6..7.
    private static byte[] LibertyState => SoundcoreVectors.Inbound(1, 1, 0, 0, 3, 4, 1, 0, 15, 0);

    // SYNTHETIC: Space Q45 state body: level 4, nothing else.
    private static byte[] SpaceState => SoundcoreVectors.Inbound(1, 1, 4, 0);

    [Fact]
    public async Task UnknownEntry_ReturnsNothingAndOpensNoChannel()
    {
        var transports = new FakeRfcommTransports((_, _) => throw new InvalidOperationException("must not open"));

        var features = await new SoundcoreDriver().OpenAsync(Context("soundcore.nope", transports, new FakeTimeProvider()));

        Assert.Empty(features);
        Assert.Empty(transports.Opened);
    }

    [Fact]
    public async Task OpensTheSelectedService()
    {
        var match = Guid.Parse("0cf12d31-fac3-4553-bd80-d6832e700007");
        var channel = new FakeRfcommChannel();
        channel.Emit(SoundcoreVectors.Hex(SoundcoreVectors.Q30Example));
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));

        await new SoundcoreDriver().OpenAsync(Context("soundcore.life-q30", transports, new FakeTimeProvider(), new HashSet<Guid> { Spp, match }));

        Assert.Equal(match, Assert.Single(transports.Opened));
    }

    [Fact]
    public async Task WithoutASoundcoreService_OpensSpp()
    {
        var opened = await OpenQ30Async();

        Assert.Equal(Spp, Assert.Single(opened.Transports.Opened));
    }

    [Fact]
    public async Task ServiceNotAvailable_ReturnsNothing()
    {
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(null));

        Assert.Empty(await new SoundcoreDriver().OpenAsync(Context("soundcore.life-q30", transports, new FakeTimeProvider())));
    }

    [Fact]
    public async Task SilentChannel_SendsRequestStateThreeTimes_ThenReturnsNothingAndDisposes()
    {
        var channel = new FakeRfcommChannel();
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));
        var time = new FakeTimeProvider();

        var features = await Drive(new SoundcoreDriver().OpenAsync(Context("soundcore.life-q30", transports, time)), time, TimeSpan.FromSeconds(4));

        Assert.Empty(features);
        Assert.True(channel.Disposed);
        Assert.Equal(3, channel.Written.Count);
        Assert.All(channel.Written, f => Assert.Equal(RequestStateFrame, f));
    }

    [Fact]
    public async Task RequestStateAnsweredOnTheSecondTry_StillOpens()
    {
        var channel = new FakeRfcommChannel();
        var answer = SoundcoreVectors.Hex(SoundcoreVectors.Q30Example);
        channel.Respond = _ => channel.Written.Count == 2 ? [answer] : [];
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));
        var time = new FakeTimeProvider();

        var features = await Drive(new SoundcoreDriver().OpenAsync(Context("soundcore.life-q30", transports, time)), time, TimeSpan.FromSeconds(4));

        Assert.Equal(3, features.Count);
        Assert.Equal(2, channel.Written.Count);
    }

    [Fact]
    public async Task ClosedChannel_ReturnsNothing()
    {
        var channel = new FakeRfcommChannel();
        channel.Close();
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));

        Assert.Empty(await new SoundcoreDriver().OpenAsync(Context("soundcore.life-q30", transports, new FakeTimeProvider())));
        Assert.True(channel.Disposed);
    }

    [Fact]
    public async Task LifeQ30_GetsBatteryNoiseAndEqualizer_WithTheVectorValues()
    {
        var opened = await OpenQ30Async();

        Assert.Equal(3, opened.Features.Count);
        var battery = Assert.Single(opened.Features.OfType<IBatteryFeature>());
        Assert.Equal([BatteryComponent.Combined], battery.Components);
        Assert.True(battery.ComponentsAreExhaustive);
        Assert.Equal(CapabilitySource.RuntimeQuery, battery.Source);
        Assert.Equal(Verification.Unverified, battery.Verification);
        Assert.Equal(100, battery.Current.Get(BatteryComponent.Combined)!.Percent);
        Assert.Equal(ChargingState.NotCharging, battery.Current.Get(BatteryComponent.Combined)!.Charging);
        Assert.Equal(NoiseMode.Off, Assert.Single(opened.Features.OfType<INoiseControlFeature>()).Current.Mode);
        var eq = Assert.Single(opened.Features.OfType<IEqualizerFeature>());
        Assert.Null(eq.CurrentPresetId);
        Assert.Equal(22, eq.Presets.Count);
        Assert.Equal("Bass booster", eq.Presets.Single(p => p.Id == 2).Name);
        Assert.Equal("Treble reducer", eq.Presets.Single(p => p.Id == 21).Name);
    }

    [Fact]
    public async Task PushesInTheSameChunkAsTheState_AreNotLost()
    {
        byte[] chunk =
        [
            .. SoundcoreVectors.Hex(SoundcoreVectors.Q30Example),
            .. SoundcoreVectors.Hex(SoundcoreVectors.SoundModesNoiseCancelling),
            .. SoundcoreVectors.Hex(SoundcoreVectors.BatteryLevelSingle),
        ];

        var opened = await OpenAsync("soundcore.life-q30", chunk);

        Assert.Equal(NoiseMode.NoiseCancelling, opened.Features.OfType<INoiseControlFeature>().Single().Current.Mode);
        Assert.Equal(40, opened.Features.OfType<IBatteryFeature>().Single().Current.Get(BatteryComponent.Combined)!.Percent);
    }

    [Fact]
    public async Task PushesAfterTheState_ApplyInOrder_LastWins()
    {
        byte[] chunk =
        [
            .. SoundcoreVectors.Hex(SoundcoreVectors.Q30Example),
            .. SoundcoreVectors.Inbound(1, 3, 2),
            .. SoundcoreVectors.Inbound(1, 3, 1),
        ];

        var opened = await OpenAsync("soundcore.life-q30", chunk);

        Assert.Equal(20, opened.Features.OfType<IBatteryFeature>().Single().Current.Get(BatteryComponent.Combined)!.Percent);
    }

    [Fact]
    public async Task TruncatedHeadphoneState_IsRejected_SoNoFeatures()
    {
        var channel = new FakeRfcommChannel();
        channel.Emit(SoundcoreVectors.Inbound(1, 1, 5, 0));
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));
        var time = new FakeTimeProvider();

        var features = await Drive(new SoundcoreDriver().OpenAsync(Context("soundcore.life-q30", transports, time)), time, TimeSpan.FromSeconds(4));

        Assert.Empty(features);
    }

    [Fact]
    public async Task EarbudStateWithoutTheEqualizer_IsRejected()
    {
        var channel = new FakeRfcommChannel();
        channel.Emit(SoundcoreVectors.Inbound(1, 1, 0, 0, 3, 4, 1, 0));
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));
        var time = new FakeTimeProvider();

        var features = await Drive(new SoundcoreDriver().OpenAsync(Context("soundcore.liberty-air-2-pro", transports, time)), time, TimeSpan.FromSeconds(4));

        Assert.Empty(features);
    }

    [Fact]
    public async Task LifeQ30_Issue141_ReadsPianoAndNoiseCancelling()
    {
        var opened = await OpenAsync("soundcore.life-q30", SoundcoreVectors.Hex(SoundcoreVectors.Q30Issue141));

        Assert.Equal(14, opened.Features.OfType<IEqualizerFeature>().Single().CurrentPresetId);
        Assert.Equal("Piano", opened.Features.OfType<IEqualizerFeature>().Single().Presets.Single(p => p.Id == 14).Name);
        Assert.Equal(NoiseMode.NoiseCancelling, opened.Features.OfType<INoiseControlFeature>().Single().Current.Mode);
    }

    [Fact]
    public async Task LibertyAir2Pro_GetsLeftRightBatteryAndEqualizer_NoNoise()
    {
        var opened = await OpenAsync("soundcore.liberty-air-2-pro", LibertyState);

        Assert.Equal(2, opened.Features.Count);
        var battery = Assert.Single(opened.Features.OfType<IBatteryFeature>());
        Assert.Equal([BatteryComponent.Left, BatteryComponent.Right], battery.Components.Order());
        Assert.Equal(60, battery.Current.Get(BatteryComponent.Left)!.Percent);
        Assert.Equal(80, battery.Current.Get(BatteryComponent.Right)!.Percent);
        Assert.Equal(ChargingState.Charging, battery.Current.Get(BatteryComponent.Left)!.Charging);
        Assert.Equal(ChargingState.NotCharging, battery.Current.Get(BatteryComponent.Right)!.Charging);
        Assert.Equal(15, Assert.Single(opened.Features.OfType<IEqualizerFeature>()).CurrentPresetId);
        Assert.Empty(opened.Features.OfType<INoiseControlFeature>());
    }

    [Fact]
    public async Task SpaceQ45_GetsBatteryOnly_WithAnUnknownChargingState()
    {
        var opened = await OpenAsync("soundcore.space-q45", SpaceState);

        var battery = Assert.IsAssignableFrom<IBatteryFeature>(Assert.Single(opened.Features));
        Assert.Equal(80, battery.Current.Get(BatteryComponent.Combined)!.Percent);
        Assert.Equal(ChargingState.Unknown, battery.Current.Get(BatteryComponent.Combined)!.Charging);
    }

    [Fact]
    public async Task BatteryLevelPush_UpdatesAndRaisesChanged_Single()
    {
        var opened = await OpenQ30Async();
        var battery = opened.Features.OfType<IBatteryFeature>().Single();
        BatteryState? seen = null;
        battery.Changed += (_, s) => seen = s;

        opened.Channel.Emit(SoundcoreVectors.Hex(SoundcoreVectors.BatteryLevelSingle));

        Assert.NotNull(seen);
        Assert.Equal(40, battery.Current.Get(BatteryComponent.Combined)!.Percent);
    }

    [Fact]
    public async Task BatteryLevelPush_Dual_AcceptsTwoOrFourBytes()
    {
        var opened = await OpenAsync("soundcore.liberty-air-2-pro", LibertyState);
        var battery = opened.Features.OfType<IBatteryFeature>().Single();

        opened.Channel.Emit(SoundcoreVectors.Hex(SoundcoreVectors.BatteryLevelDual));
        Assert.Equal((60, 80), (battery.Current.Get(BatteryComponent.Left)!.Percent, battery.Current.Get(BatteryComponent.Right)!.Percent));

        opened.Channel.Emit(SoundcoreVectors.Hex(SoundcoreVectors.BatteryLevelDualWithExtras));
        Assert.Equal((80, 100), (battery.Current.Get(BatteryComponent.Left)!.Percent, battery.Current.Get(BatteryComponent.Right)!.Percent));
    }

    [Fact]
    public async Task ChargingPush_UpdatesDualAndSingle_AndKeepsTheLevel()
    {
        var dual = await OpenAsync("soundcore.liberty-air-2-pro", LibertyState);
        var single = await OpenQ30Async();

        dual.Channel.Emit(SoundcoreVectors.Hex(SoundcoreVectors.ChargingDual));
        single.Channel.Emit(SoundcoreVectors.Hex(SoundcoreVectors.ChargingSingle));

        var d = dual.Features.OfType<IBatteryFeature>().Single().Current;
        Assert.Equal(ChargingState.Charging, d.Get(BatteryComponent.Left)!.Charging);
        Assert.Equal(ChargingState.NotCharging, d.Get(BatteryComponent.Right)!.Charging);
        Assert.Equal(60, d.Get(BatteryComponent.Left)!.Percent);
        var s = single.Features.OfType<IBatteryFeature>().Single().Current.Get(BatteryComponent.Combined)!;
        Assert.Equal(ChargingState.Charging, s.Charging);
        Assert.Equal(100, s.Percent);
    }

    [Fact]
    public async Task ChargingPush_IsIgnoredOnLevelOnlyModels()
    {
        var opened = await OpenAsync("soundcore.space-q45", SpaceState);

        opened.Channel.Emit(SoundcoreVectors.Hex(SoundcoreVectors.ChargingSingle));

        Assert.Equal(ChargingState.Unknown, opened.Features.OfType<IBatteryFeature>().Single().Current.Get(BatteryComponent.Combined)!.Charging);
    }

    [Fact]
    public async Task SoundModesPush_UpdatesNoiseAndRaisesChanged()
    {
        var opened = await OpenQ30Async();
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();
        NoiseControlState? seen = null;
        noise.Changed += (_, s) => seen = s;

        opened.Channel.Emit(SoundcoreVectors.Hex(SoundcoreVectors.SoundModesNoiseCancelling));
        Assert.Equal(NoiseMode.NoiseCancelling, seen!.Mode);

        opened.Channel.Emit(SoundcoreVectors.Hex(SoundcoreVectors.SoundModesNormal));
        Assert.Equal(NoiseMode.Off, noise.Current.Mode);
    }

    [Fact]
    public async Task UnknownAmbientValue_KeepsThePreviousMode()
    {
        var opened = await OpenQ30Async();
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();
        opened.Channel.Emit(SoundcoreVectors.Inbound(6, 1, 0, 0, 0, 0));

        // SYNTHETIC
        opened.Channel.Emit(SoundcoreVectors.Inbound(6, 1, 7, 0, 0, 0));

        Assert.Equal(NoiseMode.NoiseCancelling, noise.Current.Mode);
        Assert.True(noise.OptionsAreExhaustive);
        Assert.False(noise.Options.Supports(NoiseMode.Adaptive));
    }

    [Fact]
    public async Task StatePush_UpdatesTheEqualizer_AndCustomReadsAsNoPreset()
    {
        var opened = await OpenQ30Async();
        var eq = opened.Features.OfType<IEqualizerFeature>().Single();
        int? seen = -1;
        eq.Changed += (_, id) => seen = id;

        opened.Channel.Emit(SoundcoreVectors.Hex(SoundcoreVectors.Q30PresetEq));
        Assert.Equal(1, eq.CurrentPresetId);
        Assert.Equal(1, seen);

        opened.Channel.Emit(SoundcoreVectors.Hex(SoundcoreVectors.Q30Example));
        Assert.Null(eq.CurrentPresetId);
    }

    [Fact]
    public async Task ShortOrUnknownMessages_AreIgnored()
    {
        var opened = await OpenQ30Async();
        var battery = opened.Features.OfType<IBatteryFeature>().Single();
        var before = battery.Current;

        opened.Channel.Emit(SoundcoreVectors.Inbound(1, 3), SoundcoreVectors.Inbound(1, 1, 5), SoundcoreVectors.Inbound(0x7F, 0x7F, 1, 2, 3));

        Assert.Same(before, battery.Current);
    }

    // ---- read-only default ----

    [Fact]
    public async Task ReadOnly_OnlyRequestStateIsEverWritten_AndSettersAreUnsupported()
    {
        var opened = await OpenQ30Async();
        opened.Channel.Emit(SoundcoreVectors.Hex(SoundcoreVectors.SoundModesNoiseCancelling));
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();
        var eq = opened.Features.OfType<IEqualizerFeature>().Single();

        opened.Channel.Emit(SoundcoreVectors.Hex(SoundcoreVectors.Q30Example), SoundcoreVectors.Hex(SoundcoreVectors.BatteryLevelSingle));
        var mode = await noise.SetModeAsync(NoiseMode.Transparency);
        var preset = await eq.SetPresetAsync(2);

        Assert.Equal(CapabilityAccess.ReadOnly, noise.Access);
        Assert.Equal(CommandStatus.Unsupported, mode.Status);
        Assert.Equal("Changing this on Soundcore devices isn't supported yet.", mode.Message);
        Assert.Equal(CommandStatus.Unsupported, preset.Status);
        Assert.Equal("Changing this on Soundcore devices isn't supported yet.", preset.Message);
        Assert.NotEmpty(opened.Channel.Written);
        Assert.All(opened.Channel.Written, f => Assert.Equal(RequestStateFrame, f));
    }

    [Fact]
    public async Task InvalidInputIsStillValidatedFirst()
    {
        var opened = await OpenQ30Async();

        Assert.Equal(CommandStatus.Unsupported, (await opened.Features.OfType<INoiseControlFeature>().Single().SetModeAsync(NoiseMode.Adaptive)).Status);
        Assert.Equal(CommandStatus.Rejected, (await opened.Features.OfType<IEqualizerFeature>().Single().SetPresetAsync(42)).Status);
    }

    [Fact]
    public async Task UnsupportedSetters_ReturnUnsupported()
    {
        var noise = (await OpenQ30Async(true)).Features.OfType<INoiseControlFeature>().Single();

        Assert.Equal(CommandStatus.Unsupported, (await noise.SetAncLevelAsync(1)).Status);
        Assert.Equal(CommandStatus.Unsupported, (await noise.SetAmbientLevelAsync(1)).Status);
        Assert.Equal(CommandStatus.Unsupported, (await noise.SetWindReductionAsync(true)).Status);
        Assert.Equal(CommandStatus.Unsupported, (await noise.SetConversationAwarenessAsync(true)).Status);
    }

    // ---- writes enabled ----

    [Fact]
    public async Task Writes_SetPresetIsStillUnsupported()
    {
        var opened = await OpenQ30Async(true);
        var writes = opened.Channel.Written.Count;

        var result = await opened.Features.OfType<IEqualizerFeature>().Single().SetPresetAsync(2);

        Assert.Equal(CommandStatus.Unsupported, result.Status);
        Assert.Equal(writes, opened.Channel.Written.Count);
    }

    [Fact]
    public async Task Writes_SetModeSendsTheExactFrameKeepingTheSubBytes_AndConfirmsOnAck()
    {
        var opened = await OpenQ30Async(true);
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();
        opened.Channel.Emit(SoundcoreVectors.Inbound(6, 1, 2, 3, 1, 7));
        var expected = SoundcoreFrame.Encode(0x06, 0x81, [1, 3, 1, 7]);
        opened.Channel.Respond = f => f.SequenceEqual(expected) ? [SoundcoreVectors.Inbound(6, 0x81)] : [];

        var result = await noise.SetModeAsync(NoiseMode.Transparency);

        Assert.Equal(CommandResult.Confirmed, result);
        Assert.Equal(expected, opened.Channel.Written[^1]);
        Assert.Equal(NoiseMode.Transparency, noise.Current.Mode);
        Assert.Equal(CapabilityAccess.ReadWrite, noise.Access);
    }

    [Fact]
    public async Task Writes_SetModeFromTheUpstreamExampleState_MatchesTheUpstreamFrame()
    {
        var opened = await OpenQ30Async(true);
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();
        var upstream = SoundcoreVectors.Hex("08ee000000 0681 0e00 02000100 8e");
        opened.Channel.Respond = f => f.SequenceEqual(upstream) ? [SoundcoreVectors.Inbound(6, 0x81)] : [];

        // Off (ambient 2) with the example's sub-bytes [0, 1, 0].
        var result = await noise.SetModeAsync(NoiseMode.Off);

        Assert.True(result.Succeeded);
        Assert.Equal(upstream, opened.Channel.Written[^1]);
    }

    [Fact]
    public async Task Writes_SetModeTimesOutWithoutAnAck()
    {
        var opened = await OpenQ30Async(true);
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();

        var result = await Drive(noise.SetModeAsync(NoiseMode.Transparency), opened.Time, TimeSpan.FromSeconds(3));

        Assert.Equal(CommandStatus.TimedOut, result.Status);
        Assert.Equal(NoiseMode.Off, noise.Current.Mode);
    }

    [Fact]
    public async Task Writes_AFailedWriteReportsFailed()
    {
        var opened = await OpenQ30Async(true);
        opened.Channel.WriteFails = new IOException("gone");

        var result = await opened.Features.OfType<INoiseControlFeature>().Single().SetModeAsync(NoiseMode.NoiseCancelling);

        Assert.Equal(CommandStatus.Failed, result.Status);
    }

    // ---- disposal ----

    [Fact]
    public async Task OnlyTheBatteryFeatureClosesTheChannel_AndDisposalIsIdempotent()
    {
        var opened = await OpenQ30Async();
        var others = opened.Features.Where(f => f is not IBatteryFeature).Cast<IAsyncDisposable>().ToList();

        foreach (var feature in others)
        {
            await feature.DisposeAsync();
            await feature.DisposeAsync();
        }

        Assert.False(opened.Channel.Disposed);
        var battery = (IAsyncDisposable)opened.Features.OfType<IBatteryFeature>().Single();
        await battery.DisposeAsync();
        await battery.DisposeAsync();
        Assert.True(opened.Channel.Disposed);
    }

    // ---- model table and catalog ----

    private static readonly string Devices = Path.Combine(RepoRoot(), "devices");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Samaat.slnx")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    [Fact]
    public void ModelTable_MatchesTheShippedCatalog()
    {
        var shipped = DeviceCatalog.FromDirectory(Devices).Entries.Where(e => e.Driver == SoundcoreDriver.DriverId).Select(e => e.Id);

        Assert.Equal(shipped.Order(), SoundcoreModels.Ids.Order());
        Assert.Equal(shipped.Order(), Directory.EnumerateFiles(Path.Combine(Devices, "soundcore"), "*.json").Select(ReadId).Order());
    }

    [Fact]
    public void CatalogFeatures_MatchTheModelTable()
    {
        foreach (var entry in DeviceCatalog.FromDirectory(Devices).Entries.Where(e => e.Driver == SoundcoreDriver.DriverId))
        {
            var model = ModelOf(entry.Id);
            var expected = new HashSet<string>(model.Battery == SoundcoreBattery.Dual ? ["battery.left", "battery.right"] : ["battery.combined"]);
            if (model.SoundModesOffset is not null)
            {
                expected.UnionWith(["noise.anc", "noise.transparency"]);
            }

            if (model.EqOffset is not null)
            {
                expected.Add("eq.presets");
            }

            Assert.Equal(expected.Order(), entry.Features.Select(f => f.Value).Order());
        }
    }

    private static string ReadId(string path) =>
        System.Text.Json.JsonDocument.Parse(File.ReadAllText(path), new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })
            .RootElement.GetProperty("id").GetString()!;

    [Theory]
    [InlineData("Soundcore Life Tune Pro", "soundcore.life-tune-pro")]
    [InlineData("Soundcore Life Tune", "soundcore.life-tune")]
    [InlineData("Soundcore Liberty Air 2 Pro", "soundcore.liberty-air-2-pro")]
    [InlineData("Soundcore Life Q30", "soundcore.life-q30")]
    [InlineData("Soundcore Space Q45", "soundcore.space-q45")]
    public void SampleNames_ResolveToTheSoundcoreDriver(string name, string entryId)
    {
        var resolution = BuiltInDrivers.CreateRegistry(Devices).Resolve(new DeviceSignals(name));

        Assert.Equal(entryId, resolution.Entry?.Id);
        Assert.Equal(SoundcoreDriver.DriverId, resolution.Driver?.Id);
    }
}

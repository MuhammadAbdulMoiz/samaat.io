// SPDX-License-Identifier: GPL-3.0-or-later
// Written for Samaat from docs/protocols/sony.md (clean-room: behaviour learned from Gadgetbridge, no code copied).

using Microsoft.Extensions.Time.Testing;
using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Core.NoiseControl;
using Samaat.Drivers;
using Samaat.Protocols.Sony;
using Samaat.Transport;

namespace Samaat.Protocols.Tests;

/// <summary>
/// Everything here is SYNTHETIC: built from docs/protocols/sony.md and from the set-command serializer in
/// SonyHeadphonesClient @ 5620e8ed (MIT). The init reply 01 00 40 10 is the example given in the protocol note.
/// </summary>
public sealed class SonyTests
{
    private const string ReadOnlyText = "Changing this on Sony headphones isn't supported yet.";

    private static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    private static byte[] Wire(byte kind, byte seq, params byte[] payload) => SonyFrame.Encode(kind, seq, payload);

    private static IReadOnlyList<SonyMessage> Decode(params byte[][] chunks)
    {
        var reader = new SonyReader();
        return chunks.SelectMany(c => reader.Feed(c)).ToList();
    }

    // ---- codec ----

    [Fact]
    public void Encode_MatchesTheHandVector()
    {
        // 0C + 02 + 66 + 02 = 0x76
        Assert.Equal(Hex("3E 0C 00 00 00 00 02 66 02 76 3C"), SonyFrame.Encode(0x0C, 0, [0x66, 0x02]));
    }

    [Fact]
    public void Encode_ChecksumWrapsAt8Bits()
    {
        Assert.Equal(Hex("3E 0C 00 00 00 00 03 FF FF FF 0C 3C"), SonyFrame.Encode(0x0C, 0, [0xFF, 0xFF, 0xFF]));
    }

    [Fact]
    public void Encode_AckHasAnEmptyPayload()
    {
        Assert.Equal(Hex("3E 01 00 00 00 00 00 01 3C"), SonyFrame.Encode(0x01, 0, []));
    }

    [Fact]
    public void Encode_EscapesMarkersAndRoundTrips()
    {
        byte[] payload = [0x3C, 0x3D, 0x3E, 0x01];

        var wire = SonyFrame.Encode(0x0C, 1, payload);

        Assert.Equal(0x3E, wire[0]);
        Assert.Equal(0x3C, wire[^1]);
        Assert.DoesNotContain((byte)0x3C, wire[1..^1]);
        Assert.DoesNotContain((byte)0x3E, wire[1..^1]);
        var message = Assert.Single(Decode(wire));
        Assert.Equal((0x0C, 1), (message.Kind, message.Seq));
        Assert.Equal(payload, message.Payload);
    }

    [Fact]
    public void Encode_EscapesAChecksumThatIsAMarker()
    {
        // Kind 0C, seq 0, len 1, payload p: sum = 0x0D + p. p = 0x2F makes the checksum 0x3C.
        var wire = SonyFrame.Encode(0x0C, 0, [0x2F]);

        Assert.Equal(Hex("3E 0C 00 00 00 00 01 2F 3D 2C 3C"), wire);
        Assert.Equal([0x2F], Assert.Single(Decode(wire)).Payload);
    }

    [Fact]
    public void Reader_ReassemblesSplitChunks()
    {
        var wire = Wire(0x0C, 0, 1, 2, 3);
        var reader = new SonyReader();

        Assert.Empty(reader.Feed(wire.AsSpan(0, 3)));
        Assert.Empty(reader.Feed(wire.AsSpan(3, 4)));
        Assert.Equal([1, 2, 3], Assert.Single(reader.Feed(wire.AsSpan(7))).Payload);
    }

    [Fact]
    public void Reader_ReturnsTwoFramesFromOneChunk()
    {
        var messages = Decode([.. Wire(0x0C, 0, 1), .. Wire(0x01, 1)]);

        Assert.Equal([(0x0C, 0), (0x01, 1)], messages.Select(m => ((int)m.Kind, (int)m.Seq)));
    }

    [Fact]
    public void Reader_SkipsGarbageBeforeAFrame()
    {
        var messages = Decode([0x01, 0x02, 0x3C, 0x3D, .. Wire(0x0C, 0, 9)]);

        Assert.Equal([9], Assert.Single(messages).Payload);
    }

    [Fact]
    public void Reader_DropsABadChecksum_ThenRecovers()
    {
        var bad = Wire(0x0C, 0, 1, 2);
        bad[^2] ^= 0xFF;

        var messages = Decode([.. bad, .. Wire(0x0C, 0, 7)]);

        Assert.Equal([7], Assert.Single(messages).Payload);
    }

    [Fact]
    public void Reader_DropsABadEscape_ThenRecovers()
    {
        byte[] badEscape = [0x3E, 0x0C, 0x3D, 0x00, 0x00, 0x3C];

        var messages = Decode([.. badEscape, .. Wire(0x0C, 0, 7)]);

        Assert.Equal([7], Assert.Single(messages).Payload);
    }

    [Fact]
    public void Reader_DropsALengthThatDoesNotMatch_ThenRecovers()
    {
        // The length field says 5 but one payload byte follows; the checksum is right for these bytes.
        byte[] content = [0x0C, 0x00, 0x00, 0x00, 0x00, 0x05, 0x01];
        byte[] wrong = [0x3E, .. content, (byte)content.Sum(b => b), 0x3C];

        Assert.Equal([7], Assert.Single(Decode([.. wrong, .. Wire(0x0C, 0, 7)])).Payload);
    }

    [Fact]
    public void Reader_DropsAnUnfinishedFrameOver2048Bytes_ThenRecovers()
    {
        var reader = new SonyReader();

        Assert.Empty(reader.Feed([0x3E, .. new byte[3000]]));
        Assert.Equal([7], Assert.Single(reader.Feed(Wire(0x0C, 0, 7))).Payload);
    }

    // ---- set command (translated from SonyHeadphonesClient's serializer) ----

    [Fact]
    public void NcAsm_TransparencyAtLevel5WithVoice()
    {
        Assert.Equal(Hex("68 02 11 01 00 01 01 05"), SonyNcAsmCommand.Build(0x11, voice: 1, level: 5));
    }

    [Fact]
    public void NcAsm_Level0IsDual_Level1IsSingle()
    {
        Assert.Equal(Hex("68 02 11 01 02 01 00 00"), SonyNcAsmCommand.Build(0x11, 0, 0));
        Assert.Equal(Hex("68 02 11 01 01 01 00 01"), SonyNcAsmCommand.Build(0x11, 0, 1));
    }

    [Fact]
    public void NcAsm_NoiseCancellingUsesEffect0AndLevelFF()
    {
        Assert.Equal(Hex("68 02 00 01 00 01 00 FF"), SonyNcAsmCommand.Build(0x00, 0, 0xFF));
    }

    [Fact]
    public void NcAsm_RejectsALevelAbove19ThatIsNotFF()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SonyNcAsmCommand.Build(0x11, 0, 20));
    }

    // ---- driver ----

    private static readonly byte[] InitPayload = [0x00, 0x00];

    private static DriverContext Context(string entryId, FakeRfcommTransports transports, TimeProvider time) =>
        new(new DeviceSignals("Sony"), new CatalogEntry(entryId, "Sony", "Sony", SonyDriver.DriverId, new DeviceMatch(), false, [], new HashSet<FeatureId>()), transports, time);

    private sealed record Opened(IReadOnlyList<IFeature> Features, FakeRfcommChannel Channel, FakeRfcommTransports Transports, FakeTimeProvider Time)
    {
        public IReadOnlyList<SonyMessage> Sent => Channel.Written.Select(f => Assert.Single(Decode(f))).ToList();

        public IReadOnlyList<SonyMessage> SentCommands => Sent.Where(m => m.Kind == 0x0C).ToList();
    }

    private static async Task<T> Drive<T>(Task<T> task, FakeTimeProvider time, TimeSpan total)
    {
        for (var elapsed = TimeSpan.Zero; elapsed < total && !task.IsCompleted; elapsed += TimeSpan.FromMilliseconds(100))
        {
            time.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(1);
        }

        return await task;
    }

    /// <summary>A device: ACKs every host command with the seq toggled, then answers per the script (host payload hex to device payload).</summary>
    private static Func<byte[], byte[][]> Device(IReadOnlyDictionary<string, byte[]> script) => frame =>
    {
        var message = Assert.Single(Decode(frame));
        if (message.Kind != 0x0C)
        {
            return [];
        }

        var chunks = new List<byte[]> { Wire(0x01, (byte)(1 - message.Seq)) };
        if (script.TryGetValue(Convert.ToHexString(message.Payload), out var reply))
        {
            chunks.Add(Wire(0x0C, 0, reply));
        }

        return [.. chunks];
    };

    private static readonly Dictionary<string, byte[]> V1Init = new() { ["0000"] = Hex("01 00 40 10") };

    // XM3: 70 % charging; noise cancelling, focus on voice, ambient level 5.
    private static Dictionary<string, byte[]> Xm3Script => new(V1Init)
    {
        ["1000"] = Hex("11 00 46 01"),
        ["6602"] = Hex("67 02 01 00 01 00 01 05"),
    };

    // WF-1000XM3: left 80 % not charging, right level 0 (not connected), case 40 % charging; noise off.
    private static Dictionary<string, byte[]> BudsScript => new(V1Init)
    {
        ["1001"] = Hex("11 01 50 00 00 00"),
        ["1002"] = Hex("11 02 28 01"),
        ["6602"] = Hex("67 02 00 00 00 00 00 00"),
    };

    private static async Task<Opened> OpenAsync(string entry, Dictionary<string, byte[]> script, bool allowWrites = false)
    {
        var channel = new FakeRfcommChannel { Respond = Device(script) };
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));
        var time = new FakeTimeProvider();
        var features = await Drive(new SonyDriver(allowWrites).OpenAsync(Context(entry, transports, time)), time, TimeSpan.FromSeconds(15));
        return new Opened(features, channel, transports, time);
    }

    private static Task<Opened> OpenXm3Async(bool allowWrites = false) => OpenAsync("sony.wh-1000xm3", Xm3Script, allowWrites);

    [Fact]
    public async Task AReplyWithoutItsAck_IsNotAccepted()
    {
        // SYNTHETIC: the device answers the battery query but never ACKs it, so the seq handoff never happened.
        var device = Device(Xm3Script);
        var channel = new FakeRfcommChannel
        {
            Respond = frame => Convert.ToHexString(Assert.Single(Decode(frame)).Payload) == "1000"
                ? device(frame).Where(c => Assert.Single(Decode(c)).Kind != 0x01).ToArray()
                : device(frame),
        };
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));
        var time = new FakeTimeProvider();

        var features = await Drive(new SonyDriver().OpenAsync(Context("sony.wh-1000xm3", transports, time)), time, TimeSpan.FromSeconds(15));

        Assert.Empty(features.OfType<IBatteryFeature>());
    }

    [Fact]
    public async Task UnknownEntry_ReturnsNothingAndOpensNoChannel()
    {
        var transports = new FakeRfcommTransports((_, _) => throw new InvalidOperationException("must not open"));

        var features = await new SonyDriver().OpenAsync(Context("sony.nope", transports, new FakeTimeProvider()));

        Assert.Empty(features);
        Assert.Empty(transports.Opened);
    }

    [Fact]
    public async Task OpensSonysService_AndReturnsNothingWhenItIsMissing()
    {
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(null));

        Assert.Empty(await new SonyDriver().OpenAsync(Context("sony.wh-1000xm3", transports, new FakeTimeProvider())));
        Assert.Equal(Guid.Parse("96CC203E-5068-46AD-B32D-E316F5E069BA"), Assert.Single(transports.Opened));
    }

    [Fact]
    public async Task Xm3_GetsCombinedBatteryAndNoise()
    {
        var opened = await OpenXm3Async();

        Assert.Equal(2, opened.Features.Count);
        var battery = Assert.Single(opened.Features.OfType<IBatteryFeature>());
        Assert.Equal([BatteryComponent.Combined], battery.Components);
        Assert.Equal(CapabilitySource.RuntimeQuery, battery.Source);
        Assert.Equal(Verification.Unverified, battery.Verification);
        var reading = battery.Current.Get(BatteryComponent.Combined)!;
        Assert.Equal(70, reading.Percent);
        Assert.Equal(ChargingState.Charging, reading.Charging);
        var noise = Assert.Single(opened.Features.OfType<INoiseControlFeature>());
        Assert.Equal(NoiseMode.NoiseCancelling, noise.Current.Mode);
        Assert.Equal(CapabilityAccess.ReadOnly, noise.Access);
        Assert.True(noise.Options.Supports(NoiseMode.Off));
        Assert.True(noise.Options.Supports(NoiseMode.NoiseCancelling));
        Assert.True(noise.Options.Supports(NoiseMode.Transparency));
        Assert.False(noise.Options.Supports(NoiseMode.Adaptive));
    }

    [Fact]
    public async Task Wf1000Xm3_GetsLeftRightCase_AndAnAbsentBudIsDisconnected()
    {
        var opened = await OpenAsync("sony.wf-1000xm3", BudsScript);

        var battery = Assert.Single(opened.Features.OfType<IBatteryFeature>());
        Assert.Equal([BatteryComponent.Left, BatteryComponent.Right, BatteryComponent.Case], battery.Components.Order());
        Assert.Equal(80, battery.Current.Get(BatteryComponent.Left)!.Percent);
        Assert.Equal(ChargingState.NotCharging, battery.Current.Get(BatteryComponent.Left)!.Charging);
        var right = battery.Current.Get(BatteryComponent.Right)!;
        Assert.Null(right.Percent);
        Assert.Equal(BudStatus.Disconnected, right.Status);
        Assert.Equal(40, battery.Current.Get(BatteryComponent.Case)!.Percent);
        Assert.Equal(ChargingState.Charging, battery.Current.Get(BatteryComponent.Case)!.Charging);
        Assert.Equal(NoiseMode.Off, Assert.Single(opened.Features.OfType<INoiseControlFeature>()).Current.Mode);
    }

    [Fact]
    public async Task Buds_WithoutACaseReply_GetLeftAndRightOnly()
    {
        var script = BudsScript;
        script.Remove("1002");

        var opened = await OpenAsync("sony.wf-1000xm3", script);

        var battery = Assert.Single(opened.Features.OfType<IBatteryFeature>());
        Assert.Equal([BatteryComponent.Left, BatteryComponent.Right], battery.Components.Order());
    }

    [Fact]
    public async Task XB950BT_GetsBatteryOnly_AndIsNeverAskedForNoise()
    {
        var opened = await OpenAsync("sony.mdr-xb950bt", Xm3Script);

        Assert.IsAssignableFrom<IBatteryFeature>(Assert.Single(opened.Features));
        Assert.DoesNotContain(opened.SentCommands, m => m.Payload[0] == 0x66);
    }

    [Fact]
    public async Task NoiseUnanswered_GivesBatteryOnly()
    {
        var script = Xm3Script;
        script.Remove("6602");

        var opened = await OpenAsync("sony.wh-1000xm3", script);

        Assert.IsAssignableFrom<IBatteryFeature>(Assert.Single(opened.Features));
    }

    [Fact]
    public async Task MalformedNoiseReply_IsIgnored()
    {
        var script = Xm3Script;
        script["6602"] = Hex("67 02 01 00 07 00 01 05");

        var opened = await OpenAsync("sony.wh-1000xm3", script);

        Assert.IsAssignableFrom<IBatteryFeature>(Assert.Single(opened.Features));
    }

    [Fact]
    public async Task NothingAnswersTheBatteryQuery_GivesNothingAndDisposes()
    {
        var script = Xm3Script;
        script.Remove("1000");

        var opened = await OpenAsync("sony.wh-1000xm3", script);

        Assert.Empty(opened.Features);
        Assert.True(opened.Channel.Disposed);
    }

    [Fact]
    public async Task AProtocolV2InitReply_GivesNothingAndDisposes()
    {
        var script = new Dictionary<string, byte[]> { ["0000"] = Hex("01 00 00 00 00 00 00 00") };

        var opened = await OpenAsync("sony.wh-1000xm3", script);

        Assert.Empty(opened.Features);
        Assert.True(opened.Channel.Disposed);
        Assert.Single(opened.SentCommands);
    }

    [Fact]
    public async Task SilentDevice_GetsInitTwice_ThenNothingAndDisposes()
    {
        var channel = new FakeRfcommChannel();
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));
        var time = new FakeTimeProvider();

        var features = await Drive(new SonyDriver().OpenAsync(Context("sony.wh-1000xm3", transports, time)), time, TimeSpan.FromSeconds(20));

        Assert.Empty(features);
        Assert.True(channel.Disposed);
        Assert.Equal(2, channel.Written.Count);
        Assert.All(channel.Written, f => Assert.Equal(InitPayload, Assert.Single(Decode(f)).Payload));
    }

    [Fact]
    public async Task ClosedChannel_ReturnsNothing()
    {
        var channel = new FakeRfcommChannel();
        channel.Close();
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));
        var time = new FakeTimeProvider();

        Assert.Empty(await Drive(new SonyDriver().OpenAsync(Context("sony.wh-1000xm3", transports, time)), time, TimeSpan.FromSeconds(20)));
        Assert.True(channel.Disposed);
    }

    [Fact]
    public async Task BatteryPush_UpdatesAndRaisesChanged_AndIsAckedWithTheOtherSeq()
    {
        var opened = await OpenXm3Async();
        var battery = opened.Features.OfType<IBatteryFeature>().Single();
        BatteryState? seen = null;
        battery.Changed += (_, s) => seen = s;

        opened.Channel.Emit(Wire(0x0C, 1, 0x13, 0x00, 0x32, 0x00));

        Assert.NotNull(seen);
        Assert.Equal(50, battery.Current.Get(BatteryComponent.Combined)!.Percent);
        Assert.Equal(ChargingState.NotCharging, battery.Current.Get(BatteryComponent.Combined)!.Charging);
        Assert.Equal(Wire(0x01, 0), opened.Channel.Written[^1]);
    }

    [Fact]
    public async Task BatteryLevelOver100_HasNoPercent()
    {
        var opened = await OpenXm3Async();
        var battery = opened.Features.OfType<IBatteryFeature>().Single();

        opened.Channel.Emit(Wire(0x0C, 0, 0x13, 0x00, 0x65, 0x00));

        Assert.Null(battery.Current.Get(BatteryComponent.Combined)!.Percent);
    }

    [Fact]
    public async Task BudsPush_UpdatesTheCaseAndABudComingBack()
    {
        var opened = await OpenAsync("sony.wf-1000xm3", BudsScript);
        var battery = opened.Features.OfType<IBatteryFeature>().Single();

        opened.Channel.Emit(Wire(0x0C, 0, 0x13, 0x01, 0x50, 0x00, 0x1E, 0x01), Wire(0x0C, 1, 0x13, 0x02, 0x0A, 0x00));

        Assert.Equal(30, battery.Current.Get(BatteryComponent.Right)!.Percent);
        Assert.Equal(ChargingState.Charging, battery.Current.Get(BatteryComponent.Right)!.Charging);
        Assert.Equal(10, battery.Current.Get(BatteryComponent.Case)!.Percent);
    }

    [Fact]
    public async Task NoisePush_UpdatesAndRaisesChanged_WithTheModeMapping()
    {
        var opened = await OpenXm3Async();
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();
        NoiseControlState? seen = null;
        noise.Changed += (_, s) => seen = s;

        opened.Channel.Emit(Wire(0x0C, 0, 0x69, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00));
        Assert.Equal(NoiseMode.Off, seen!.Mode);

        opened.Channel.Emit(Wire(0x0C, 1, 0x69, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00, 0x03));
        Assert.Equal(NoiseMode.Transparency, noise.Current.Mode);

        opened.Channel.Emit(Wire(0x0C, 0, 0x69, 0x02, 0x01, 0x02, 0x01, 0x00, 0x00, 0x00));
        Assert.Equal(NoiseMode.NoiseCancelling, noise.Current.Mode);

        opened.Channel.Emit(Wire(0x0C, 1, 0x69, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00));
        opened.Channel.Emit(Wire(0x0C, 0, 0x69, 0x02, 0x01, 0x02, 0x02, 0x00, 0x00, 0x00));
        Assert.Equal(NoiseMode.NoiseCancelling, noise.Current.Mode);
    }

    [Fact]
    public async Task UnknownNoiseValues_KeepThePreviousMode()
    {
        var opened = await OpenXm3Async();
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();

        opened.Channel.Emit(
            Wire(0x0C, 0, 0x69, 0x02, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00),
            Wire(0x0C, 1, 0x69, 0x02, 0x01, 0x00, 0x09, 0x00, 0x00, 0x00),
            Wire(0x0C, 0, 0x69, 0x02, 0x01, 0x00),
            Wire(0x0C, 1, 0x69, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00));

        Assert.Equal(NoiseMode.NoiseCancelling, noise.Current.Mode);
    }

    [Fact]
    public async Task EveryHostFrameIsInitAQueryOrAnAck_AndInboundCommandsAreAckedWithTheOtherSeq()
    {
        var opened = await OpenAsync("sony.wf-1000xm3", BudsScript);

        var commands = opened.SentCommands.Select(m => Convert.ToHexString(m.Payload)).ToList();
        Assert.Equal(["0000", "1001", "1002", "6602"], commands);
        Assert.All(opened.Sent, m => Assert.True(m.Kind is 0x01 or 0x0C));
        // Four replies came in, all with seq 0: four ACKs with seq 1 and no payload.
        var acks = opened.Sent.Where(m => m.Kind == 0x01).ToList();
        Assert.Equal(4, acks.Count);
        Assert.All(acks, a => Assert.Equal((1, 0), ((int)a.Seq, a.Payload.Length)));
        Assert.Contains(opened.Channel.Written, f => f.SequenceEqual(Wire(0x01, 1)));
    }

    [Fact]
    public async Task EachCommandCarriesTheSeqFromTheLastAck()
    {
        var opened = await OpenAsync("sony.wf-1000xm3", BudsScript);

        // The device ACKs seq s with 1 - s, so the host alternates 0, 1, 0, 1.
        Assert.Equal([0, 1, 0, 1], opened.SentCommands.Select(m => (int)m.Seq));
    }

    [Fact]
    public async Task ReadOnly_SetModeIsUnsupportedAndWritesNothing()
    {
        var opened = await OpenXm3Async();
        var writes = opened.Channel.Written.Count;
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();

        var result = await noise.SetModeAsync(NoiseMode.Transparency);

        Assert.Equal(CommandStatus.Unsupported, result.Status);
        Assert.Equal(ReadOnlyText, result.Message);
        Assert.Equal(writes, opened.Channel.Written.Count);
    }

    [Fact]
    public async Task SetMode_ValidatesFirst()
    {
        var opened = await OpenXm3Async();

        var result = await opened.Features.OfType<INoiseControlFeature>().Single().SetModeAsync(NoiseMode.Adaptive);

        Assert.Equal(CommandStatus.Unsupported, result.Status);
        Assert.NotEqual(ReadOnlyText, result.Message);
    }

    [Fact]
    public async Task UnsupportedSetters_ReturnUnsupported()
    {
        var noise = (await OpenXm3Async(true)).Features.OfType<INoiseControlFeature>().Single();

        Assert.Equal(CommandStatus.Unsupported, (await noise.SetAncLevelAsync(1)).Status);
        Assert.Equal(CommandStatus.Unsupported, (await noise.SetAmbientLevelAsync(1)).Status);
        Assert.Equal(CommandStatus.Unsupported, (await noise.SetWindReductionAsync(true)).Status);
        Assert.Equal(CommandStatus.Unsupported, (await noise.SetConversationAwarenessAsync(true)).Status);
    }

    [Fact]
    public async Task SessionRefusesTheSetCommandWhenWritesAreOff()
    {
        var channel = new FakeRfcommChannel();
        await using var session = new SonySession(channel, false, new FakeTimeProvider(), TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SendCommandAsync(SonyNcAsmCommand.Build(0x11, 0, 5), CancellationToken.None));
        Assert.Empty(channel.Written);
    }

    [Fact]
    public async Task Writes_TransparencySendsTheExactFrameKeepingVoiceAndLevel_AndConfirmsOnAck()
    {
        var opened = await OpenXm3Async(true);
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();
        var seq = (byte)(1 - opened.SentCommands[^1].Seq);

        var result = await noise.SetModeAsync(NoiseMode.Transparency);

        Assert.Equal(CommandResult.Confirmed, result);
        Assert.Equal(CapabilityAccess.ReadWrite, noise.Access);
        var expected = Wire(0x0C, seq, 0x68, 0x02, 0x11, 0x01, 0x00, 0x01, 0x01, 0x05);
        Assert.Contains(opened.Channel.Written, f => f.SequenceEqual(expected));
        Assert.Equal(NoiseMode.Transparency, noise.Current.Mode);
    }

    [Fact]
    public async Task Writes_NoiseCancellingSendsEffect0AndLevelFF()
    {
        var script = Xm3Script;
        script["6602"] = Hex("67 02 00 00 00 00 00 00");
        var opened = await OpenAsync("sony.wh-1000xm3", script, allowWrites: true);
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();

        var result = await noise.SetModeAsync(NoiseMode.NoiseCancelling);

        Assert.True(result.Succeeded);
        Assert.Equal(Hex("68 02 00 01 00 01 00 FF"), opened.SentCommands[^1].Payload);
        Assert.Equal(NoiseMode.NoiseCancelling, noise.Current.Mode);
    }

    [Fact]
    public async Task Writes_OffIsUnsupportedAndWritesNothing()
    {
        var opened = await OpenXm3Async(true);
        var writes = opened.Channel.Written.Count;

        var result = await opened.Features.OfType<INoiseControlFeature>().Single().SetModeAsync(NoiseMode.Off);

        Assert.Equal(CommandStatus.Unsupported, result.Status);
        Assert.Equal("Turning noise control off isn't supported yet.", result.Message);
        Assert.Equal(writes, opened.Channel.Written.Count);
    }

    [Fact]
    public async Task Writes_TimesOutWithoutAnAck_AndKeepsTheMode()
    {
        var opened = await OpenXm3Async(true);
        opened.Channel.Respond = _ => [];
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();

        var result = await Drive(noise.SetModeAsync(NoiseMode.Transparency), opened.Time, TimeSpan.FromSeconds(3));

        Assert.Equal(CommandStatus.TimedOut, result.Status);
        Assert.Equal(NoiseMode.NoiseCancelling, noise.Current.Mode);
    }

    [Fact]
    public async Task Writes_AFailedWriteReportsFailed()
    {
        var opened = await OpenXm3Async(true);
        opened.Channel.WriteFails = new IOException("gone");

        var result = await opened.Features.OfType<INoiseControlFeature>().Single().SetModeAsync(NoiseMode.Transparency);

        Assert.Equal(CommandStatus.Failed, result.Status);
    }

    [Fact]
    public async Task OnlyTheBatteryFeatureClosesTheChannel_AndDisposalIsIdempotent()
    {
        var opened = await OpenXm3Async();
        var noise = (IAsyncDisposable)opened.Features.OfType<INoiseControlFeature>().Single();

        await noise.DisposeAsync();
        await noise.DisposeAsync();
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

    private static string ReadId(string path) =>
        System.Text.Json.JsonDocument.Parse(File.ReadAllText(path), new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })
            .RootElement.GetProperty("id").GetString()!;

    [Fact]
    public void ModelTable_MatchesTheShippedCatalog()
    {
        var shipped = DeviceCatalog.FromDirectory(Devices).Entries.Where(e => e.Driver == SonyDriver.DriverId).Select(e => e.Id);

        Assert.Equal(shipped.Order(), SonyModels.Ids.Order());
        Assert.Equal(shipped.Order(), Directory.EnumerateFiles(Path.Combine(Devices, "sony"), "*.json").Select(ReadId).Order());
    }

    [Fact]
    public void CatalogFeatures_MatchTheModelTable()
    {
        foreach (var entry in DeviceCatalog.FromDirectory(Devices).Entries.Where(e => e.Driver == SonyDriver.DriverId))
        {
            Assert.True(SonyModels.TryGet(entry.Id, out var model));
            var expected = new HashSet<string>(model.Battery == SonyBatteryKind.BudsAndCase ? ["battery.left", "battery.right", "battery.case"] : ["battery.combined"]);
            if (model.Noise)
            {
                expected.UnionWith(["noise.anc", "noise.transparency"]);
            }

            Assert.Equal(expected.Order(), entry.Features.Select(f => f.Value).Order());
        }
    }

    [Theory]
    [InlineData("WH-1000XM3", "sony.wh-1000xm3")]
    [InlineData("WH-1000XM4", "sony.wh-1000xm4")]
    [InlineData("WF-1000XM3", "sony.wf-1000xm3")]
    [InlineData("MDR-XB950BT", "sony.mdr-xb950bt")]
    public void SampleNames_ResolveToTheSonyDriver(string name, string entryId)
    {
        var resolution = BuiltInDrivers.CreateRegistry(Devices).Resolve(new DeviceSignals(name));

        Assert.Equal(entryId, resolution.Entry?.Id);
        Assert.Equal(SonyDriver.DriverId, resolution.Driver?.Id);
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/maniacx/BudsLink @ 0eb4d07dcd7eafabf425d98c607e02fc9519c94b, src/lib/devices/nothingBuds/{nothingBudsSocket.js,nothingBudsConfig.js,deviceConfigs/CMFBuds.js}, GPL-3.0 (BudsLink credits radiance-project/ear-web).
// Changes: rewritten in C# onto Samaat.Transport and Samaat.Core; ANC read offset and noise-cancelling byte corrected from Samaat's own CMF Buds captures (docs/hardware-tests/2026-11-06-cmf-buds.md).

using Microsoft.Extensions.Time.Testing;
using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Core.Equalizer;
using Samaat.Core.NoiseControl;
using Samaat.Core.Profiles;
using Samaat.Drivers;
using Samaat.Protocols.Nothing;
using Samaat.Transport;

namespace Samaat.Protocols.Tests;

/// <summary>Samaat capture, CMF Buds fw 1.0.5.050, 2026-11-06 (docs/hardware-tests/2026-11-06-cmf-buds.md).</summary>
internal static class NothingCaptured
{
    public const string TxModel = "5560011CC000000288DC";
    public const string RxModel = "5560011C4002000268B1EE04";
    public const string TxBattery = "55600107C00000046CDC";
    public const string RxBattery = "556001074007000403025A0364043C32A5";
    public const string TxAnc = "5560011EC0000006F0DF";
    public const string RxAncOff = "5560011E400600060201000105006EA9";
    public const string RxAncTransparency = "5560011E4006000A0201000107006FC9";
    public const string RxAncNc1 = "5560011E4006000A0201000101006C69";
    public const string TxEq = "55600150C00000081915";
    public const string RxEqCustom = "5560015040010008063F42";
    public const string RxEqRock = "5560015040010010017E80";
    public const string TxAncSetTransparency = "5560010FF0030009010700F817";
    public const string TxAncSetNc1 = "5560010FF0030009010100FBB7";
    public const string RxAncAck = "5560010F70000009FFFF";
    public const string PushTransparency = "55000103E0030000010700";
    public const string PushNc1 = "55000103E0030000010100";
    public const string TxEqSet6 = "5560011DF002000F0600990A";
    public const string TxEqSet1 = "5560011DF002000F01009B3A";
    public const string RxEqAck = "5560011D7000000FFFFF";
    public const string RxVersionReply = "5560010140060001312E302E310A16A2";

    public static byte[] Hex(string hex) => Convert.FromHexString(hex);
}

public sealed class NothingTests
{
    private static readonly Guid Service = Guid.Parse("aeac4a03-dff5-498f-843a-34487cf133eb");

    // ---- codec ----

    [Theory]
    [InlineData(NothingCommands.ModelId, 2, "", NothingCaptured.TxModel)]
    [InlineData(NothingCommands.Battery, 4, "", NothingCaptured.TxBattery)]
    [InlineData(NothingCommands.AncRead, 6, "", NothingCaptured.TxAnc)]
    [InlineData(NothingCommands.EqRead, 8, "", NothingCaptured.TxEq)]
    [InlineData(NothingCommands.AncSet, 9, "010700", NothingCaptured.TxAncSetTransparency)]
    [InlineData(NothingCommands.AncSet, 9, "010100", NothingCaptured.TxAncSetNc1)]
    [InlineData(NothingCommands.EqSet, 15, "0600", NothingCaptured.TxEqSet6)]
    [InlineData(NothingCommands.EqSet, 15, "0100", NothingCaptured.TxEqSet1)]
    public void Encoder_ReproducesTheCapturedFrames(ushort command, int seq, string payload, string expected)
    {
        Assert.Equal(NothingCaptured.Hex(expected), NothingFrame.Encode(command, (byte)seq, NothingCaptured.Hex(payload)));
    }

    [Theory]
    [InlineData(NothingCaptured.RxModel, 0x401C, 2, "68B1")]
    [InlineData(NothingCaptured.RxBattery, 0x4007, 4, "03025A0364043C")]
    [InlineData(NothingCaptured.RxAncOff, 0x401E, 6, "020100010500")]
    [InlineData(NothingCaptured.RxAncTransparency, 0x401E, 10, "020100010700")]
    [InlineData(NothingCaptured.RxAncNc1, 0x401E, 10, "020100010100")]
    [InlineData(NothingCaptured.RxEqCustom, 0x4050, 8, "06")]
    [InlineData(NothingCaptured.RxEqRock, 0x4050, 16, "01")]
    [InlineData(NothingCaptured.RxAncAck, 0x700F, 9, "")]
    [InlineData(NothingCaptured.RxEqAck, 0x701D, 15, "")]
    [InlineData(NothingCaptured.PushTransparency, 0xE003, 0, "010700")]
    [InlineData(NothingCaptured.RxVersionReply, 0x4001, 1, "312E302E310A")]
    public void Reader_ParsesTheCapturedFrames(string hex, int command, int seq, string payload)
    {
        var message = Assert.Single(new NothingReader().Feed(NothingCaptured.Hex(hex)));

        Assert.Equal((ushort)command, message.Command);
        Assert.Equal((byte)seq, message.Seq);
        Assert.Equal(NothingCaptured.Hex(payload), message.Payload);
    }

    [Fact]
    public void Reader_ReassemblesSplitChunks()
    {
        var frame = NothingCaptured.Hex(NothingCaptured.RxBattery);
        var reader = new NothingReader();

        Assert.Empty(reader.Feed(frame.AsSpan(0, 3)));
        Assert.Empty(reader.Feed(frame.AsSpan(3, 8)));
        Assert.Single(reader.Feed(frame.AsSpan(11)));
    }

    [Fact]
    public void Reader_ReturnsTwoFramesFromOneChunk()
    {
        var chunk = NothingCaptured.Hex(NothingCaptured.RxAncAck + NothingCaptured.PushNc1);

        Assert.Equal<ushort>([0x700F, 0xE003], new NothingReader().Feed(chunk).Select(m => m.Command));
    }

    [Fact]
    public void Reader_SkipsGarbageBeforeAFrame()
    {
        var chunk = NothingCaptured.Hex("0102FF" + NothingCaptured.RxEqCustom);

        Assert.Equal<ushort>(0x4050, Assert.Single(new NothingReader().Feed(chunk)).Command);
    }

    [Fact]
    public void Reader_ParsesAPushWithoutACrc()
    {
        var message = Assert.Single(new NothingReader().Feed(NothingCaptured.Hex(NothingCaptured.PushNc1)));

        Assert.Equal([0x01, 0x01, 0x00], message.Payload);
    }

    [Fact]
    public void Reader_DropsAnOversizedFrameAndStillFindsTheNextOne()
    {
        var reader = new NothingReader();
        byte[] junk = [0x55, 0x60, 0x01, 0x1E, 0x40, 0xFF, 0xFF, 0x01];

        var messages = reader.Feed([.. junk, .. new byte[5000], .. NothingCaptured.Hex(NothingCaptured.RxEqCustom)]);

        Assert.Equal<ushort>(0x4050, Assert.Single(messages).Command);
        Assert.True(reader.Buffered < NothingReader.MaxBuffered);
    }

    [Fact]
    public void Reader_CapsTheBufferOnAnUnfinishedFlood()
    {
        var reader = new NothingReader();

        Assert.Empty(reader.Feed([.. Enumerable.Repeat((byte)0x55, 9000)]));
        Assert.True(reader.Buffered <= NothingReader.MaxBuffered);
    }

    [Fact]
    public async Task Session_SequenceWrapsFrom250ToOne()
    {
        var channel = new FakeRfcommChannel();
        var seqs = new List<byte>();
        channel.Respond = frame => { seqs.Add(frame[7]); return [Rx(0x401C, frame[7], 0x68, 0xB1)]; };
        var session = new NothingSession(channel, true, new FakeTimeProvider(), TimeSpan.FromSeconds(1));
        session.Attach();

        for (var i = 0; i < 252; i++)
        {
            await session.RequestAsync(NothingCommands.ModelId, [], CancellationToken.None);
        }

        Assert.Equal(1, seqs[0]);
        Assert.Equal(250, seqs[249]);
        Assert.Equal(1, seqs[250]);
        Assert.Equal(2, seqs[251]);
    }

    // ---- driver ----

    private static byte[] Rx(ushort command, byte seq, params byte[] payload)
    {
        byte[] frame = [0x55, 0x60, 0x01, (byte)command, (byte)(command >> 8), (byte)payload.Length, (byte)(payload.Length >> 8), seq, .. payload, 0xFF, 0xFF];
        return frame;
    }

    /// <summary>A CMF Buds that answers like the captures: L90 R100 case60, ANC off, EQ custom. Acks writes and tracks their effect.</summary>
    private sealed class Buds
    {
        public byte Anc = 0x05;
        public byte Eq = 0x06;
        public bool SkipAncRead;
        public ushort ModelId = 0xB168;
        public byte[] Model => [(byte)ModelId, (byte)(ModelId >> 8)];

        public byte[][] Reply(byte[] frame)
        {
            var command = (ushort)(frame[3] | (frame[4] << 8));
            var seq = frame[7];

            switch (command)
            {
                case NothingCommands.ModelId:
                    return [Rx(0x401C, seq, Model)];
                case NothingCommands.Battery:
                    return [Rx(0x4007, seq, 0x03, 0x02, 0x5A, 0x03, 0x64, 0x04, 0x3C)];
                case NothingCommands.AncRead:
                    return SkipAncRead ? [] : [Rx(0x401E, seq, 0x02, 0x01, 0x00, 0x01, Anc, 0x00)];
                case NothingCommands.EqRead:
                    return [Rx(0x4050, seq, Eq)];
                case NothingCommands.AncSet:
                    Anc = frame[9];
                    return [Rx(0x700F, seq), Rx(0xE003, 0, 0x01, Anc, 0x00)];
                case NothingCommands.EqSet:
                    Eq = frame[8];
                    return [Rx(0x701D, seq)];
                default:
                    return [];
            }
        }
    }

    private sealed record Opened(IReadOnlyList<IFeature> Features, FakeRfcommChannel Channel, FakeRfcommTransports Transports, Buds Buds);

    private static DriverContext Context(FakeRfcommTransports transports, TimeProvider time) =>
        new(new DeviceSignals("CMF Buds"), new CatalogEntry("nothing.cmf-buds", "CMF Buds", "Nothing", "nothing-spp", new DeviceMatch(), true, [], new HashSet<FeatureId>()), transports, time);

    private static async Task<Opened> OpenAsync(Buds? buds = null, bool allowWrites = true)
    {
        buds ??= new Buds();
        var channel = new FakeRfcommChannel { Respond = buds.Reply };
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));
        var driver = BuiltInDrivers.Create().OfType<NothingDriver>().Single();
        if (!allowWrites)
        {
            driver = new NothingDriver(allowWrites: false);
        }

        var features = await driver.OpenAsync(Context(transports, new FakeTimeProvider()));
        return new Opened(features, channel, transports, buds);
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

    [Fact]
    public async Task ServiceNotAvailable_ReturnsNothing()
    {
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(null));

        Assert.Empty(await new NothingDriver().OpenAsync(Context(transports, new FakeTimeProvider())));
        Assert.Equal(Service, Assert.Single(transports.Opened));
    }

    [Fact]
    public async Task SilentChannel_AsksTheModelTwice_ThenReturnsNothingAndDisposes()
    {
        var channel = new FakeRfcommChannel();
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));
        var time = new FakeTimeProvider();

        var features = await Drive(new NothingDriver().OpenAsync(Context(transports, time)), time, TimeSpan.FromSeconds(5));

        Assert.Empty(features);
        Assert.True(channel.Disposed);
        Assert.Equal(2, channel.Written.Count);
        Assert.Equal(NothingCaptured.Hex(NothingCaptured.TxModel)[..7], channel.Written[0][..7]);
    }

    [Fact]
    public async Task ClosedChannel_ReturnsNothing()
    {
        var channel = new FakeRfcommChannel();
        channel.Close();
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));

        Assert.Empty(await new NothingDriver().OpenAsync(Context(transports, new FakeTimeProvider())));
        Assert.True(channel.Disposed);
    }

    [Fact]
    public async Task UnknownModelId_ReturnsNothingAndDisposes()
    {
        var opened = await OpenAsync(new Buds { ModelId = 0x1234 });

        Assert.Empty(opened.Features);
        Assert.True(opened.Channel.Disposed);
    }

    [Fact]
    public async Task CmfBuds_GetBatteryNoiseAndEqualizer_WithTheCapturedValues()
    {
        var opened = await OpenAsync();

        Assert.Equal(3, opened.Features.Count);
        var battery = Assert.Single(opened.Features.OfType<IBatteryFeature>());
        Assert.Equal(90, battery.Current.Get(BatteryComponent.Left)!.Percent);
        Assert.Equal(100, battery.Current.Get(BatteryComponent.Right)!.Percent);
        Assert.Equal(60, battery.Current.Get(BatteryComponent.Case)!.Percent);
        Assert.Equal(Verification.Verified, battery.Verification);
        var noise = Assert.Single(opened.Features.OfType<INoiseControlFeature>());
        Assert.Equal(NoiseMode.Off, noise.Current.Mode);
        Assert.Equal(CapabilityAccess.ReadWrite, noise.Access);
        Assert.Equal([NoiseMode.Off, NoiseMode.NoiseCancelling, NoiseMode.Transparency], noise.Options.Modes);
        Assert.True(noise.OptionsAreExhaustive);
        var eq = Assert.Single(opened.Features.OfType<IEqualizerFeature>());
        Assert.Equal(6, eq.CurrentPresetId);
        Assert.Equal(7, eq.Presets.Count);
    }

    [Fact]
    public async Task BatteryChargingBit_IsReadFromTheHighBit()
    {
        var opened = await OpenAsync();
        var battery = opened.Features.OfType<IBatteryFeature>().Single();

        opened.Channel.Emit(Rx(0xE001, 0, 0x01, 0x02, 0xDC));

        Assert.Equal(92, battery.Current.Get(BatteryComponent.Left)!.Percent);
        Assert.True(battery.Current.Get(BatteryComponent.Left)!.IsCharging);
    }

    [Fact]
    public async Task MissingAncReply_OmitsOnlyTheNoiseFeature()
    {
        var channel = new FakeRfcommChannel();
        var buds = new Buds { SkipAncRead = true };
        channel.Respond = buds.Reply;
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));
        var time = new FakeTimeProvider();

        var features = await Drive(new NothingDriver().OpenAsync(Context(transports, time)), time, TimeSpan.FromSeconds(3));

        Assert.Empty(features.OfType<INoiseControlFeature>());
        Assert.Equal(2, features.Count);
    }

    [Fact]
    public async Task Setters_ReportFailed_WhenTheWriteThrows()
    {
        var opened = await OpenAsync();
        opened.Channel.WriteFails = new IOException("link dropped");

        var mode = await opened.Features.OfType<INoiseControlFeature>().Single().SetModeAsync(NoiseMode.Transparency);
        var preset = await opened.Features.OfType<IEqualizerFeature>().Single().SetPresetAsync(1);

        Assert.Equal(CommandStatus.Failed, mode.Status);
        Assert.Equal(CommandStatus.Failed, preset.Status);
    }

    [Fact]
    public async Task SetMode_Transparency_WritesTheCapturedFrame_AndSucceedsOnAck()
    {
        var opened = await OpenAsync();
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();
        var changes = new List<NoiseControlState>();
        noise.Changed += (_, s) => changes.Add(s);

        var result = await noise.SetModeAsync(NoiseMode.Transparency);

        Assert.True(result.Succeeded);
        var write = opened.Channel.Written.Single(f => f[3] == 0x0F && f[4] == 0xF0);
        Assert.Equal(NothingCaptured.Hex("010700"), write[8..11]);
        Assert.Equal(NoiseMode.Transparency, noise.Current.Mode);
        Assert.Single(changes);
    }

    [Fact]
    public async Task SetPreset_One_WritesTheCapturedPayload_AndSucceeds()
    {
        var opened = await OpenAsync();
        var eq = opened.Features.OfType<IEqualizerFeature>().Single();

        var result = await eq.SetPresetAsync(1);

        Assert.True(result.Succeeded);
        var write = opened.Channel.Written.Single(f => f[3] == 0x1D && f[4] == 0xF0);
        Assert.Equal(NothingCaptured.Hex("0100"), write[8..10]);
        Assert.Equal(1, eq.CurrentPresetId);
        Assert.Equal(CommandStatus.Rejected, (await eq.SetPresetAsync(9)).Status);
    }

    [Fact]
    public async Task SetMode_WithNoAck_TimesOut()
    {
        var channel = new FakeRfcommChannel();
        var buds = new Buds();
        channel.Respond = f => f[4] == 0xF0 ? [] : buds.Reply(f);
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));
        var time = new FakeTimeProvider();
        var noise = (await new NothingDriver().OpenAsync(Context(transports, time))).OfType<INoiseControlFeature>().Single();

        var result = await Drive(noise.SetModeAsync(NoiseMode.Transparency), time, TimeSpan.FromSeconds(3));

        Assert.Equal(CommandStatus.TimedOut, result.Status);
    }

    [Fact]
    public async Task NoiseCancellingLevel_IsRemembered()
    {
        var opened = await OpenAsync();
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();

        opened.Channel.Emit(Rx(0xE003, 0, 0x01, 0x03, 0x00));
        opened.Channel.Emit(Rx(0xE003, 0, 0x01, 0x05, 0x00));
        Assert.Equal(NoiseMode.Off, noise.Current.Mode);
        await noise.SetModeAsync(NoiseMode.NoiseCancelling);

        var write = opened.Channel.Written.Single(f => f[3] == 0x0F && f[4] == 0xF0);
        Assert.Equal(NothingCaptured.Hex("010300"), write[8..11]);
    }

    [Fact]
    public async Task PushE003_UpdatesCurrentAndRaisesChanged()
    {
        var opened = await OpenAsync();
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();
        NoiseControlState? seen = null;
        noise.Changed += (_, s) => seen = s;

        opened.Channel.Emit(NothingCaptured.Hex(NothingCaptured.PushTransparency));
        opened.Channel.Emit(Rx(0xE003, 0, 0x01, 0x04, 0x00));

        Assert.Equal(NoiseMode.Transparency, seen?.Mode);
        Assert.Equal(NoiseMode.Transparency, noise.Current.Mode);
    }

    [Fact]
    public async Task WritesOff_SettersDoNotWrite_AndAccessIsReadOnly()
    {
        var opened = await OpenAsync(allowWrites: false);
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();
        var eq = opened.Features.OfType<IEqualizerFeature>().Single();
        var before = opened.Channel.Written.Count;

        Assert.Equal(CapabilityAccess.ReadOnly, noise.Access);
        Assert.Equal(CommandStatus.Unsupported, (await noise.SetModeAsync(NoiseMode.Transparency)).Status);
        Assert.Equal(CommandStatus.Unsupported, (await eq.SetPresetAsync(1)).Status);
        Assert.Equal(before, opened.Channel.Written.Count);
        Assert.All(opened.Channel.Written, f => Assert.False(NothingCommands.IsWrite((ushort)(f[3] | (f[4] << 8)))));
    }

    [Fact]
    public async Task OnlyTheFirstFeatureOwnsTheChannel()
    {
        var opened = await OpenAsync();

        foreach (var feature in opened.Features.OfType<IAsyncDisposable>().Skip(1))
        {
            await feature.DisposeAsync();
        }

        Assert.False(opened.Channel.Disposed);
        await ((IAsyncDisposable)opened.Features[0]).DisposeAsync();
        Assert.True(opened.Channel.Disposed);
    }

    // ---- catalog ----

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
    public void CmfBuds_WithItsVendorIdAndService_ResolvesToTheNothingDriver()
    {
        var resolution = BuiltInDrivers.CreateRegistry(Devices).Resolve(
            new DeviceSignals("CMF Buds", VendorId: 0x02B0, ServiceUuids: new HashSet<Guid> { Service }));

        Assert.Equal("nothing.cmf-buds", resolution.Entry!.Id);
        Assert.Equal(NothingDriver.DriverId, resolution.Driver!.Id);
        Assert.NotEqual("generic.fast-pair", resolution.Entry.Id);
    }

    [Fact]
    public void CmfBuds_WithAnotherVendorOrService_DoesNotMatch()
    {
        var other = new HashSet<Guid> { Guid.Parse("df21fe2c-2515-4fdb-8886-f12c4d67927c") };
        var catalog = DeviceCatalog.FromDirectory(Devices);

        Assert.Empty(catalog.Errors);
        Assert.NotEqual("nothing.cmf-buds", catalog.Match(new DeviceSignals("CMF Buds", VendorId: 0x05D6, ServiceUuids: other))?.Id);
        Assert.NotEqual("nothing.cmf-buds", catalog.Match(new DeviceSignals("CMF Buds", VendorId: 0x02B0, ServiceUuids: other))?.Id);
        Assert.NotEqual("nothing.cmf-buds", catalog.Match(new DeviceSignals("CMF Buds", VendorId: 0x05D6, ServiceUuids: new HashSet<Guid> { Service }))?.Id);

        var db = DeviceProfileDb.Load(Devices);
        Assert.Empty(db.Errors);
        var wrong = new DeviceIdentity("id", "CMF Buds", Details: new DeviceDetails { VendorId = 0x05D6, ServiceUuids = [Service] });
        Assert.NotEqual("nothing.cmf-buds", db.Match(wrong)?.Id);
        var noService = new DeviceIdentity("id", "CMF Buds", Details: new DeviceDetails { VendorId = 0x02B0, ServiceUuids = [] });
        Assert.NotEqual("nothing.cmf-buds", db.Match(noService)?.Id);
        var right = new DeviceIdentity("id", "CMF Buds", Details: new DeviceDetails { VendorId = 0x02B0, ServiceUuids = [Service] });
        Assert.Equal("nothing.cmf-buds", db.Match(right)!.Id);
    }
}

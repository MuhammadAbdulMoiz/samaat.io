using Microsoft.Extensions.Time.Testing;
using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Core.Equalizer;
using Samaat.Core.NoiseControl;
using Samaat.Drivers;
using Samaat.Protocols.Samsung;
using Samaat.Transport;

namespace Samaat.Protocols.Tests;

/// <summary>
/// Upstream test vectors from GalaxyBudsClient.Tests/TestData/ExtendedStatusUpdate (GPL-3.0, @ 754b6fdf); not Samaat captures.
/// Synthetic frames are labelled SYNTHETIC.
/// </summary>
internal static class SamsungVectors
{
    public sealed record Vector(string Entry, string Hex, int Left, int Right, int? CasePercent, SamsungPlacement PlacementLeft, SamsungPlacement PlacementRight, int? Eq, int? Noise);

    private const SamsungPlacement Wear = SamsungPlacement.Wearing;
    private const SamsungPlacement Case = SamsungPlacement.InCase;

    public static readonly IReadOnlyDictionary<string, Vector> All = new Dictionary<string, Vector>
    {
        ["Buds_rev3"] = new("samsung.galaxy-buds", "fe0112610300465f0100000000020107002201a575ee", 70, 95, null, SamsungPlacement.Unknown, SamsungPlacement.Unknown, null, null),
        ["BudsPlus_rev13"] = new("samsung.galaxy-buds-plus", "fd1b10610d00575401001165000000030022010401040100000003001e55dd", 87, 84, null, Wear, Wear, 3, null),
        ["BudsLive_rev9"] = new("samsung.galaxy-buds-live", "fd1c106109010f610100116400000022010017011701000003001000000281dd", 15, 97, 100, Wear, Wear, 0, null),
        ["BudsPro_rev10"] = new("samsung.galaxy-buds-pro", "fd290c610a02595e010113640001002200002a012a0101000355000201000002001000010000220200009d24dd", 89, 94, 100, Wear, Case, 1, 0),
        ["Buds2_rev10"] = new("samsung.galaxy-buds2", "fd2810610a0364640101113300033f2201003c013c0102000366000100100000000011020101000000bea3dd", 100, 100, 51, Wear, Wear, 3, 1),
        ["Buds2Pro_rev13"] = new("samsung.galaxy-buds2-pro", "fd3100610d046464010033580001bf220001460146010000033500030010010101013203010101000cae0100030001000101b637dd", 100, 100, 88, Case, Case, 1, 0),
        ["BudsFe_rev2"] = new("samsung.galaxy-buds-fe", "fd31106102063a590100110000008f3300004a014a01030004650001001000000101110300000000e80300009d0e1d005000c2cfdd", 58, 89, 0, Wear, Wear, 0, 0),
    };

    public static byte[] Frame(string name) => Convert.FromHexString(All[name].Hex);
}

public sealed class SamsungTests
{
    private static readonly byte[] ManagerInfoFrame = SppFrame.Encode(SamsungIds.ManagerInfo, [0x01, 0x01, 34], false);

    private static byte[] Frame(byte id, byte[] payload, bool legacy = false) => SppFrame.Encode(id, payload, legacy);

    private static SamsungModel ModelOf(string entry)
    {
        Assert.True(SamsungModels.TryGet(entry, out var model));
        return model;
    }

    public static TheoryData<string> VectorNames => [.. SamsungVectors.All.Keys];

    // ---- codec ----

    [Fact]
    public void Crc16_MatchesTheUpstreamExample()
    {
        byte[] body = [0x61, 0x02, 0x00, 0x4B, 0x5F, 0x01, 0x00, 0x00, 0x00, 0x01, 0x05, 0x00, 0x02, 0x00, 0x13];

        var crc = SppFrame.Crc16(body);

        Assert.Equal(0x0F, crc & 0xFF);
        Assert.Equal(0xF3, crc >> 8);
    }

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void Vector_DecodesToTheExpectedStatus(string name)
    {
        var vector = SamsungVectors.All[name];
        var model = ModelOf(vector.Entry);

        var message = Assert.Single(new SppReader(model.Legacy).Feed(SamsungVectors.Frame(name)));
        var status = SamsungStatus.TryParse(message, model);

        Assert.NotNull(status);
        Assert.Equal(vector.Left, status.LeftPercent);
        Assert.Equal(vector.Right, status.RightPercent);
        Assert.Equal(vector.CasePercent, status.CasePercent);
        Assert.Equal(vector.PlacementLeft, status.PlacementLeft);
        Assert.Equal(vector.PlacementRight, status.PlacementRight);
        Assert.Equal(vector.Eq, status.EqRaw);
        Assert.Equal(vector.Noise, status.NoiseRaw);
    }

    [Fact]
    public void BudsPlus_CaseRaw101_IsUnknown()
    {
        var model = ModelOf("samsung.galaxy-buds-plus");
        var message = Assert.Single(new SppReader(false).Feed(SamsungVectors.Frame("BudsPlus_rev13")));

        Assert.Equal(101, SamsungStatus.TryParse(message, model)!.CaseRaw);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EncodeThenDecode_RoundTrips(bool legacy)
    {
        // SYNTHETIC
        byte[] payload = [1, 2, 3, 0xFD, 0xDD];

        var message = Assert.Single(new SppReader(legacy).Feed(SppFrame.Encode(0x42, payload, legacy)));

        Assert.Equal(0x42, message.Id);
        Assert.Equal(payload, message.Payload);
    }

    [Fact]
    public void Reader_ReassemblesSplitChunks()
    {
        var frame = Frame(0x42, [0x78, 0x02]);
        var reader = new SppReader(false);

        Assert.Empty(reader.Feed(frame.AsSpan(0, 4)));
        Assert.Empty(reader.Feed(frame.AsSpan(4, 3)));
        Assert.Single(reader.Feed(frame.AsSpan(7)));
    }

    [Fact]
    public void Reader_ReturnsTwoFramesFromOneChunk()
    {
        var messages = new SppReader(false).Feed([.. Frame(0x42, [1, 2]), .. Frame(0x77, [2])]);

        Assert.Equal([0x42, 0x77], messages.Select(m => m.Id));
    }

    [Fact]
    public void Reader_SkipsLeadingGarbage()
    {
        var messages = new SppReader(false).Feed([0x00, 0x11, 0x22, .. Frame(0x77, [2])]);

        Assert.Equal(0x77, Assert.Single(messages).Id);
    }

    [Fact]
    public void Reader_ResyncsAfterABadCrc()
    {
        var bad = Frame(0x42, [1, 2]);
        bad[^3] ^= 0xFF;

        var messages = new SppReader(false).Feed([.. bad, .. Frame(0x77, [2])]);

        Assert.Equal(0x77, Assert.Single(messages).Id);
    }

    [Fact]
    public void Reader_DropsFragmentedFrames()
    {
        // SYNTHETIC: header bit 0x2000 set, CRC still valid.
        var fragment = Frame(0x42, [1, 2]);
        fragment[2] |= 0x20;

        var messages = new SppReader(false).Feed([.. fragment, .. Frame(0x77, [2])]);

        Assert.Equal(0x77, Assert.Single(messages).Id);
    }

    [Fact]
    public void Reader_AcceptsTheBudsTypeBit()
    {
        var frame = Frame(0x42, [1, 2]);
        frame[2] |= 0x10;

        Assert.Single(new SppReader(false).Feed(frame));
    }

    [Fact]
    public void Reader_SurvivesAnOversizedJunkChunk_AndStillFindsTheNextFrame()
    {
        var reader = new SppReader(false);

        Assert.Empty(reader.Feed(new byte[10_000]));
        Assert.Single(reader.Feed(Frame(0x77, [2])));
    }

    // ---- driver ----

    private static DriverContext Context(string entryId, FakeRfcommTransports transports, TimeProvider time) =>
        new(new DeviceSignals("Galaxy Buds"), new CatalogEntry(entryId, "Galaxy Buds", "Samsung", "samsung-spp", new DeviceMatch(), false, [], new HashSet<FeatureId>()), transports, time);

    private sealed record Opened(IReadOnlyList<IFeature> Features, FakeRfcommChannel Channel, FakeRfcommTransports Transports, FakeTimeProvider Time);

    private static async Task<Opened> OpenAsync(string vector, bool allowWrites = false, params byte[][] morePushes)
    {
        var channel = new FakeRfcommChannel();
        channel.Emit([SamsungVectors.Frame(vector), .. morePushes]);
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));
        var time = new FakeTimeProvider();
        var features = await new SamsungDriver(allowWrites).OpenAsync(Context(SamsungVectors.All[vector].Entry, transports, time));
        return new Opened(features, channel, transports, time);
    }

    [Fact]
    public async Task UnknownEntry_ReturnsNothingAndOpensNoChannel()
    {
        var transports = new FakeRfcommTransports((_, _) => throw new InvalidOperationException("must not open"));

        var features = await new SamsungDriver().OpenAsync(Context("samsung.nope", transports, new FakeTimeProvider()));

        Assert.Empty(features);
        Assert.Empty(transports.Opened);
    }

    [Fact]
    public async Task OpensTheModelsServiceUuid()
    {
        var plus = await OpenAsync("BudsPlus_rev13");
        var buds2 = await OpenAsync("Buds2_rev10");
        var legacy = await OpenAsync("Buds_rev3");

        Assert.Equal(Guid.Parse("00001101-0000-1000-8000-00805f9b34fb"), Assert.Single(plus.Transports.Opened));
        Assert.Equal(Guid.Parse("2e73a4ad-332d-41fc-90e2-16bef06523f2"), Assert.Single(buds2.Transports.Opened));
        Assert.Equal(Guid.Parse("00001102-0000-1000-8000-00805f9b34fd"), Assert.Single(legacy.Transports.Opened));
    }

    [Fact]
    public async Task ServiceNotAvailable_ReturnsNothing()
    {
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(null));

        Assert.Empty(await new SamsungDriver().OpenAsync(Context("samsung.galaxy-buds2", transports, new FakeTimeProvider())));
    }

    [Fact]
    public async Task SilentChannel_ReturnsNothingAfterTheTimeoutAndDisposesTheChannel()
    {
        var channel = new FakeRfcommChannel();
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));
        var time = new FakeTimeProvider();

        var open = new SamsungDriver().OpenAsync(Context("samsung.galaxy-buds2", transports, time));
        time.Advance(TimeSpan.FromSeconds(4));

        Assert.Empty(await open);
        Assert.True(channel.Disposed);
        Assert.Empty(channel.Written);
    }

    [Fact]
    public async Task ClosedChannel_ReturnsNothing()
    {
        var channel = new FakeRfcommChannel();
        channel.Close();
        var transports = new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));

        Assert.Empty(await new SamsungDriver().OpenAsync(Context("samsung.galaxy-buds2", transports, new FakeTimeProvider())));
        Assert.True(channel.Disposed);
    }

    [Fact]
    public async Task Buds2_GetsBatteryNoiseAndEqualizer()
    {
        var opened = await OpenAsync("Buds2_rev10");

        Assert.Equal(3, opened.Features.Count);
        var battery = Assert.Single(opened.Features.OfType<IBatteryFeature>());
        Assert.Equal(100, battery.Current.Get(BatteryComponent.Left)!.Percent);
        Assert.Equal(51, battery.Current.Get(BatteryComponent.Case)!.Percent);
        Assert.Equal(NoiseMode.NoiseCancelling, Assert.Single(opened.Features.OfType<INoiseControlFeature>()).Current.Mode);
        Assert.Equal(3, Assert.Single(opened.Features.OfType<IEqualizerFeature>()).CurrentPresetId);
    }

    [Fact]
    public async Task BudsPlus_GetsBatteryAndEqualizerOnly()
    {
        var opened = await OpenAsync("BudsPlus_rev13");

        Assert.Equal(2, opened.Features.Count);
        Assert.Single(opened.Features.OfType<IBatteryFeature>());
        Assert.Equal(3, Assert.Single(opened.Features.OfType<IEqualizerFeature>()).CurrentPresetId);
    }

    [Fact]
    public async Task LegacyBuds_GetBatteryOnly_WithoutACase()
    {
        var opened = await OpenAsync("Buds_rev3");

        var battery = Assert.IsAssignableFrom<IBatteryFeature>(Assert.Single(opened.Features));
        Assert.Equal([BatteryComponent.Left, BatteryComponent.Right], battery.Components.Order());
        Assert.True(battery.ComponentsAreExhaustive);
        Assert.Null(battery.Current.Get(BatteryComponent.Case));
        Assert.Equal(70, battery.Current.Get(BatteryComponent.Left)!.Percent);
    }

    [Fact]
    public async Task BatteryReadings_CarryPlacementAndAnUnknownCase()
    {
        var pro = await OpenAsync("BudsPro_rev10");
        var plus = await OpenAsync("BudsPlus_rev13");

        var proBattery = pro.Features.OfType<IBatteryFeature>().Single().Current;
        Assert.Equal(BudStatus.Connected, proBattery.Get(BatteryComponent.Left)!.Status);
        Assert.Equal(BudStatus.InCase, proBattery.Get(BatteryComponent.Right)!.Status);
        Assert.Equal(ChargingState.Unknown, proBattery.Get(BatteryComponent.Right)!.Charging);
        Assert.Null(plus.Features.OfType<IBatteryFeature>().Single().Current.Get(BatteryComponent.Case)!.Percent);
    }

    [Fact]
    public async Task StatusUpdate_UpdatesBatteryAndRaisesChanged()
    {
        var opened = await OpenAsync("Buds2_rev10");
        var battery = opened.Features.OfType<IBatteryFeature>().Single();
        BatteryState? seen = null;
        battery.Changed += (_, s) => seen = s;

        // SYNTHETIC: StatusUpdate [rev, L, R, _, _, placement, case]
        opened.Channel.Emit(Frame(SamsungIds.StatusUpdate, [0, 50, 60, 0, 0, 0x11, 101]));

        Assert.NotNull(seen);
        Assert.Equal(50, battery.Current.Get(BatteryComponent.Left)!.Percent);
        Assert.Equal(60, battery.Current.Get(BatteryComponent.Right)!.Percent);
        Assert.Null(battery.Current.Get(BatteryComponent.Case)!.Percent);
    }

    [Fact]
    public async Task ShortOrUnknownMessages_AreIgnored()
    {
        var opened = await OpenAsync("Buds2_rev10");
        var battery = opened.Features.OfType<IBatteryFeature>().Single();
        var before = battery.Current;

        opened.Channel.Emit(Frame(SamsungIds.ExtendedStatus, [1]), Frame(SamsungIds.StatusUpdate, []), Frame(0x99, [1, 2, 3]));

        Assert.Same(before, battery.Current);
    }

    [Fact]
    public async Task PushedNoiseControlsUpdate_RaisesChanged()
    {
        var opened = await OpenAsync("Buds2_rev10");
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();
        NoiseControlState? seen = null;
        noise.Changed += (_, s) => seen = s;

        // SYNTHETIC
        opened.Channel.Emit(Frame(SamsungIds.NoiseControlsUpdate, [2]));

        Assert.Equal(NoiseMode.Transparency, seen!.Mode);
        Assert.Equal(NoiseMode.Transparency, noise.Current.Mode);
    }

    [Fact]
    public async Task AdaptiveNoiseMode_KeepsThePreviousMode()
    {
        var opened = await OpenAsync("Buds2_rev10");
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();

        opened.Channel.Emit(Frame(SamsungIds.NoiseControlsUpdate, [3]));

        Assert.Equal(NoiseMode.NoiseCancelling, noise.Current.Mode);
        Assert.False(noise.Options.Supports(NoiseMode.Adaptive));
        Assert.True(noise.OptionsAreExhaustive);
    }

    [Fact]
    public async Task UnknownEqValue_ReadsAsNoCurrentPreset()
    {
        var opened = await OpenAsync("BudsPlus_rev13");
        var eq = opened.Features.OfType<IEqualizerFeature>().Single();
        int? seen = -1;
        eq.Changed += (_, id) => seen = id;

        // SYNTHETIC: Equalizer ack carrying 7 (custom).
        opened.Channel.Emit(Frame(SamsungIds.Ack, [SamsungIds.Equalizer, 7]));

        Assert.Null(eq.CurrentPresetId);
        Assert.Null(seen);
        Assert.Equal(["Normal", "Bass boost", "Soft", "Dynamic", "Clear", "Treble boost"], eq.Presets.Select(p => p.Name));
    }

    // ---- read-only default ----

    [Fact]
    public async Task ReadOnly_NeverWrites_AndSettersAreUnsupported()
    {
        var opened = await OpenAsync("Buds2_rev10", false, SamsungVectors.Frame("Buds2_rev10"));
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();
        var eq = opened.Features.OfType<IEqualizerFeature>().Single();
        Assert.Empty(opened.Channel.Written);

        opened.Channel.Emit(SamsungVectors.Frame("Buds2_rev10"));
        var mode = await noise.SetModeAsync(NoiseMode.Transparency);
        var preset = await eq.SetPresetAsync(2);

        Assert.Equal(CapabilityAccess.ReadOnly, noise.Access);
        Assert.Equal(CommandStatus.Unsupported, mode.Status);
        Assert.Equal("Changing this on Galaxy Buds isn't supported yet.", mode.Message);
        Assert.Equal(CommandStatus.Unsupported, preset.Status);
        Assert.Empty(opened.Channel.Written);
    }

    [Fact]
    public async Task ReadOnly_InvalidInputIsStillValidatedFirst()
    {
        var opened = await OpenAsync("Buds2_rev10");

        Assert.Equal(CommandStatus.Unsupported, (await opened.Features.OfType<INoiseControlFeature>().Single().SetModeAsync(NoiseMode.Adaptive)).Status);
        Assert.Equal(CommandStatus.Rejected, (await opened.Features.OfType<IEqualizerFeature>().Single().SetPresetAsync(42)).Status);
    }

    [Fact]
    public async Task UnsupportedSetters_ReturnUnsupported()
    {
        var noise = (await OpenAsync("Buds2_rev10", true)).Features.OfType<INoiseControlFeature>().Single();

        Assert.Equal(CommandStatus.Unsupported, (await noise.SetAncLevelAsync(1)).Status);
        Assert.Equal(CommandStatus.Unsupported, (await noise.SetAmbientLevelAsync(1)).Status);
        Assert.Equal(CommandStatus.Unsupported, (await noise.SetWindReductionAsync(true)).Status);
        Assert.Equal(CommandStatus.Unsupported, (await noise.SetConversationAwarenessAsync(true)).Status);
    }

    // ---- writes enabled ----

    [Fact]
    public async Task Writes_ManagerInfoIsSentAfterEachExtendedStatus()
    {
        var opened = await OpenAsync("Buds2_rev10", true);

        Assert.Equal(ManagerInfoFrame, Assert.Single(opened.Channel.Written));

        opened.Channel.Emit(SamsungVectors.Frame("Buds2_rev10"));

        Assert.Equal(2, opened.Channel.Written.Count);
        Assert.All(opened.Channel.Written, f => Assert.Equal(ManagerInfoFrame, f));
    }

    [Fact]
    public async Task Writes_SetModeConfirmsOnAck()
    {
        var opened = await OpenAsync("Buds2_rev10", true);
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();
        var expected = Frame(SamsungIds.NoiseControlsSet, [2]);
        opened.Channel.Respond = f => f.SequenceEqual(expected) ? [Frame(SamsungIds.Ack, [SamsungIds.NoiseControlsSet, 2])] : [];

        var result = await noise.SetModeAsync(NoiseMode.Transparency);

        Assert.Equal(CommandResult.Confirmed, result);
        Assert.Equal(expected, opened.Channel.Written[^1]);
        Assert.Equal(NoiseMode.Transparency, noise.Current.Mode);
        Assert.Equal(CapabilityAccess.ReadWrite, noise.Access);
    }

    [Fact]
    public async Task Writes_SetModeTimesOutWithoutAnAck()
    {
        var opened = await OpenAsync("Buds2_rev10", true);
        var noise = opened.Features.OfType<INoiseControlFeature>().Single();

        var pending = noise.SetModeAsync(NoiseMode.Transparency);
        opened.Time.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(CommandStatus.TimedOut, (await pending).Status);
        Assert.Equal(NoiseMode.NoiseCancelling, noise.Current.Mode);
    }

    [Fact]
    public async Task Writes_SetPresetConfirmsOnAck()
    {
        var opened = await OpenAsync("Buds2_rev10", true);
        var eq = opened.Features.OfType<IEqualizerFeature>().Single();
        var expected = Frame(SamsungIds.Equalizer, [4]);
        opened.Channel.Respond = f => f.SequenceEqual(expected) ? [Frame(SamsungIds.Ack, [SamsungIds.Equalizer, 4])] : [];

        var result = await eq.SetPresetAsync(4);

        Assert.True(result.Succeeded);
        Assert.Equal(expected, opened.Channel.Written[^1]);
        Assert.Equal(4, eq.CurrentPresetId);
    }

    [Fact]
    public async Task Writes_InvalidModeAndPresetAreRejectedWithoutAWrite()
    {
        var opened = await OpenAsync("Buds2_rev10", true);
        var writes = opened.Channel.Written.Count;

        var mode = await opened.Features.OfType<INoiseControlFeature>().Single().SetModeAsync(NoiseMode.Adaptive);
        var preset = await opened.Features.OfType<IEqualizerFeature>().Single().SetPresetAsync(9);

        Assert.False(mode.Succeeded);
        Assert.Equal(CommandStatus.Rejected, preset.Status);
        Assert.Equal(writes, opened.Channel.Written.Count);
    }

    [Fact]
    public async Task Writes_AFailedWriteReportsFailed()
    {
        var opened = await OpenAsync("Buds2_rev10", true);
        opened.Channel.WriteFails = new IOException("gone");

        var result = await opened.Features.OfType<IEqualizerFeature>().Single().SetPresetAsync(1);

        Assert.Equal(CommandStatus.Failed, result.Status);
    }

    // ---- disposal ----

    [Fact]
    public async Task OnlyTheBatteryFeatureClosesTheChannel_AndDisposalIsIdempotent()
    {
        var opened = await OpenAsync("Buds2_rev10");
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
        var shipped = DeviceCatalog.FromDirectory(Devices).Entries.Where(e => e.Driver == SamsungDriver.DriverId).Select(e => e.Id);

        Assert.Equal(shipped.Order(), SamsungModels.Ids.Order());
        Assert.Equal(shipped.Order(), Directory.EnumerateFiles(Path.Combine(Devices, "samsung"), "*.json").Select(ReadId).Order());
    }

    private static string ReadId(string path) =>
        System.Text.Json.JsonDocument.Parse(File.ReadAllText(path), new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })
            .RootElement.GetProperty("id").GetString()!;

    [Theory]
    [InlineData("Galaxy Buds2 Pro (A1B2)", "samsung.galaxy-buds2-pro")]
    [InlineData("Galaxy Buds2 (A1B2)", "samsung.galaxy-buds2")]
    [InlineData("Galaxy Buds3 (X)", "samsung.galaxy-buds3")]
    [InlineData("Galaxy Buds (X)", "samsung.galaxy-buds")]
    [InlineData("Galaxy Buds+ (X)", "samsung.galaxy-buds-plus")]
    public void SampleNames_ResolveToTheSamsungDriver(string name, string entryId)
    {
        var resolution = BuiltInDrivers.CreateRegistry(Devices).Resolve(new DeviceSignals(name));

        Assert.Equal(entryId, resolution.Entry?.Id);
        Assert.Equal(SamsungDriver.DriverId, resolution.Driver?.Id);
    }
}

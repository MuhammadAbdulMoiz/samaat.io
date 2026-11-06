using Microsoft.Extensions.Time.Testing;
using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Core.NoiseControl;
using Samaat.Protocols.Bose;
using Samaat.Transport;

namespace Samaat.Protocols.Tests;

/// <summary>Frames recorded from the Bose QC Headphones (docs/hardware-tests/2026-11-05-bose-qc-headphones.md), plus labelled synthetic ones.</summary>
internal static class BoseFrames
{
    public static readonly byte[] GetMode = Convert.FromHexString("1F030100");

    public static readonly byte[] ModeQuiet = Convert.FromHexString("1F03030100");

    public static readonly byte[] Firmware = Convert.FromHexString("00050310312E302E362D38302B66356632313962");

    public static readonly byte[] Battery = Convert.FromHexString("0202030414FFFF00");

    // SYNTHETIC: same observed format, index 1 (Aware).
    public static readonly byte[] ModeAware = Convert.FromHexString("1F03030101");

    // SYNTHETIC: same observed format, index 2 (a user-configured mode).
    public static readonly byte[] ModeCustom = Convert.FromHexString("1F03030102");

    // SYNTHETIC: error operator for the audio-mode function.
    public static readonly byte[] ModeError = Convert.FromHexString("1F03040104");
}

internal sealed class FakeBoseTransports(Func<byte, FakeRfcommChannel?> open) : IDeviceTransports
{
    public List<byte> Opened { get; } = [];

    public IGattClient? Gatt => null;

    public Task<IRfcommChannel?> OpenRfcommAsync(Guid serviceUuid, CancellationToken cancellationToken = default) =>
        Task.FromResult<IRfcommChannel?>(null);

    public Task<IRfcommChannel?> OpenRfcommChannelAsync(byte channel, CancellationToken cancellationToken = default)
    {
        Opened.Add(channel);
        return Task.FromResult<IRfcommChannel?>(open(channel));
    }
}

public sealed class BoseBmapTests
{
    // Every channel any test opens, so the allowlist can be checked over all of them.
    private static readonly List<FakeRfcommChannel> AllChannels = [];

    private static FakeRfcommChannel Answering(params byte[][] reply)
    {
        var channel = new FakeRfcommChannel { Respond = _ => reply };
        AllChannels.Add(channel);
        return channel;
    }

    private static FakeRfcommChannel Silent()
    {
        var channel = new FakeRfcommChannel();
        AllChannels.Add(channel);
        return channel;
    }

    private static DriverContext Context(IDeviceTransports transports, TimeProvider time) =>
        new(new DeviceSignals("Bose QC Headphones"), new CatalogEntry("bose.qc-headphones", "Bose QC Headphones", "Bose", "bose-bmap", new DeviceMatch(), false, [], new HashSet<FeatureId>()), transports, time);

    private static async Task<(IReadOnlyList<IFeature> Features, FakeBoseTransports Transports)> OpenAsync(Func<byte, FakeRfcommChannel?> open)
    {
        var transports = new FakeBoseTransports(open);
        var features = await new BoseBmapDriver(TimeSpan.FromSeconds(1)).OpenAsync(Context(transports, new FakeTimeProvider()));
        return (features, transports);
    }

    [Fact]
    public void Reader_ParsesTheRecordedReplies()
    {
        var reader = new BmapReader();

        var mode = Assert.Single(reader.Feed(BoseFrames.ModeQuiet));
        var firmware = Assert.Single(reader.Feed(BoseFrames.Firmware));
        var battery = Assert.Single(reader.Feed(BoseFrames.Battery));

        Assert.Equal((byte)0x1F, mode.Block);
        Assert.Equal((byte)0x03, mode.Function);
        Assert.Equal(BmapFrame.OperatorStatus, mode.Operator);
        Assert.Equal(new byte[] { 0x00 }, mode.Payload);
        Assert.Equal("1.0.6-80+f5f219b", System.Text.Encoding.ASCII.GetString(firmware.Payload));
        Assert.Equal(new byte[] { 0x14, 0xFF, 0xFF, 0x00 }, battery.Payload);
    }

    [Fact]
    public void Reader_JoinsAReplySplitAcrossChunks()
    {
        var reader = new BmapReader();

        Assert.Empty(reader.Feed(BoseFrames.Firmware.AsSpan(0, 6)));
        var packet = Assert.Single(reader.Feed(BoseFrames.Firmware.AsSpan(6)));

        Assert.Equal(16, packet.Payload.Length);
    }

    [Fact]
    public void Reader_SplitsTwoRepliesInOneChunk()
    {
        var packets = new BmapReader().Feed([.. BoseFrames.ModeQuiet, .. BoseFrames.Battery]);

        Assert.Equal([(byte)0x1F, (byte)0x02], packets.Select(p => p.Block));
    }

    [Fact]
    public async Task Driver_ReadsTheRecordedModeFromChannel8()
    {
        var channel8 = Answering(BoseFrames.ModeQuiet);

        var (features, transports) = await OpenAsync(c => c == 8 ? channel8 : null);

        var feature = Assert.IsType<BoseNoiseControlFeature>(Assert.Single(features));
        Assert.Equal([(byte)8], transports.Opened);
        Assert.Equal(NoiseMode.NoiseCancelling, feature.Current.Mode);
        Assert.Equal([NoiseMode.NoiseCancelling, NoiseMode.Transparency], feature.Options.Modes);
        Assert.False(feature.OptionsAreExhaustive);
        Assert.Equal(CapabilityAccess.ReadOnly, feature.Access);
        Assert.False(channel8.Disposed);
    }

    [Fact]
    public async Task Driver_FallsBackToChannel9WhenChannel8WontOpen()
    {
        var channel9 = Answering(BoseFrames.ModeQuiet);

        var (features, transports) = await OpenAsync(c => c == 9 ? channel9 : null);

        Assert.Single(features);
        Assert.Equal([(byte)8, (byte)9], transports.Opened);
    }

    [Fact]
    public async Task Driver_GivesUpOnSilence_AndDisposesTheChannels()
    {
        var channels = new[] { Silent(), Silent() };
        var time = new FakeTimeProvider();
        var transports = new FakeBoseTransports(c => channels[c - 8]);

        var open = new BoseBmapDriver(TimeSpan.FromSeconds(1)).OpenAsync(Context(transports, time));
        while (!open.IsCompleted)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10);
        }

        Assert.Empty(await open);
        Assert.All(channels, c => Assert.True(c.Disposed));
    }

    [Fact]
    public async Task Driver_TreatsAnErrorReplyAsNoFeature_ThenTriesChannel9()
    {
        var channel8 = Answering(BoseFrames.ModeError);
        var channel9 = Answering(BoseFrames.ModeQuiet);

        var (features, transports) = await OpenAsync(c => c == 8 ? channel8 : channel9);

        Assert.True(channel8.Disposed);
        Assert.Equal([(byte)8, (byte)9], transports.Opened);
        Assert.Single(features);
    }

    [Fact]
    public async Task Driver_ExposesNothingForAnUnknownInitialMode()
    {
        var channel = Answering(BoseFrames.ModeCustom);

        var (features, _) = await OpenAsync(_ => channel);

        Assert.Empty(features);
        Assert.True(channel.Disposed);
    }

    [Fact]
    public async Task LaterStatus_UpdatesTheMode_AndUnknownIndexesAreIgnored()
    {
        var channel = Answering(BoseFrames.ModeQuiet);
        var (features, _) = await OpenAsync(c => c == 8 ? channel : null);
        var feature = (BoseNoiseControlFeature)features[0];
        var changes = new List<NoiseControlState>();
        feature.Changed += (_, s) => changes.Add(s);

        channel.Emit(BoseFrames.ModeAware);
        channel.Emit(BoseFrames.ModeCustom);

        Assert.Equal(NoiseMode.Transparency, feature.Current.Mode);
        Assert.Equal([NoiseMode.Transparency], changes.Select(s => s.Mode));
    }

    [Fact]
    public async Task SettingTheMode_IsUnsupported_AndWritesNothing()
    {
        var channel = Answering(BoseFrames.ModeQuiet);
        var (features, _) = await OpenAsync(c => c == 8 ? channel : null);
        var feature = (BoseNoiseControlFeature)features[0];
        var writesBefore = channel.Writes;

        var transparency = await feature.SetModeAsync(NoiseMode.Transparency);
        var off = await feature.SetModeAsync(NoiseMode.Off);

        Assert.Equal(CommandStatus.Unsupported, transparency.Status);
        Assert.Equal(CommandStatus.Unsupported, off.Status);
        Assert.Equal(writesBefore, channel.Writes);
    }

    [Fact]
    public async Task OnlyTheAudioModeGetIsEverWritten()
    {
        await Driver_ReadsTheRecordedModeFromChannel8();
        await Driver_FallsBackToChannel9WhenChannel8WontOpen();
        await Driver_TreatsAnErrorReplyAsNoFeature_ThenTriesChannel9();
        await SettingTheMode_IsUnsupported_AndWritesNothing();

        Assert.NotEmpty(AllChannels.SelectMany(c => c.Written));
        Assert.All(AllChannels.SelectMany(c => c.Written), frame => Assert.Equal(BoseFrames.GetMode, frame));
    }

    [Fact]
    public void ShippedCatalog_ResolvesTheQcHeadphonesToTheBmapDriver()
    {
        var registry = new DriverRegistry(DeviceCatalog.FromDirectory(Path.Combine(RepoRoot(), "devices")));
        registry.Register(new BoseBmapDriver());

        var resolution = registry.Resolve(new DeviceSignals("Bose QC Headphones", VendorId: 0x009E, ProductId: 0x4075));

        Assert.Equal(DriverResolutionKind.Driver, resolution.Kind);
        Assert.Equal("bose-bmap", resolution.Driver!.Id);
        Assert.Equal("bose.qc-headphones", resolution.Entry!.Id);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Samaat.slnx")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }
}

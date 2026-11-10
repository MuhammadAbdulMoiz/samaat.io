using System.Diagnostics;
using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Core.NoiseControl;
using Samaat.Protocols.Bose;
using Samaat.Protocols.Nothing;
using Samaat.Protocols.Samsung;
using Samaat.Protocols.Sony;
using Samaat.Protocols.Soundcore;
using Samaat.Transport;

namespace Samaat.Protocols.Tests;

/// <summary>
/// The sequence the platform runs when earbuds drop: the RFCOMM channel closes, the features are disposed, and the
/// driver is opened again on a fresh channel. Real time throughout: the command timeout is long on purpose, so a
/// command that waits for it instead of noticing the closed link fails the bound below.
/// </summary>
public sealed class ReconnectTests
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PromptBound = TimeSpan.FromMilliseconds(500);
    private const int StressCycles = 25;

    /// <summary>One driver plus a factory for a channel scripted like the recorded device.</summary>
    private sealed record Rig(string Entry, IEarbudDriver Driver, Func<FakeRfcommChannel> NewChannel, Func<FakeRfcommChannel, IDeviceTransports>? Transports = null)
    {
        public async Task<(IReadOnlyList<IFeature> Features, FakeRfcommChannel Channel)> OpenAsync()
        {
            var channel = NewChannel();
            IDeviceTransports transports = Transports?.Invoke(channel) ?? new FakeRfcommTransports((_, _) => Task.FromResult<IRfcommChannel?>(channel));
            var entry = new CatalogEntry(Entry, "Test", "Test", Driver.Id, new DeviceMatch(), false, [], new HashSet<FeatureId>());
            var context = new DriverContext(new DeviceSignals("Test"), entry, transports, TimeProvider.System);
            return (await Driver.OpenAsync(context), channel);
        }
    }

    // A dropped link answers nothing: stop the scripted replies, then close.
    private static void Drop(FakeRfcommChannel channel)
    {
        channel.Respond = null;
        channel.Close();
    }

    private static byte[] NothingRx(ushort command, byte seq, params byte[] payload) => NothingFrame.Encode(command, seq, payload);

    private static byte[][] NothingReply(byte[] frame)
    {
        var command = (ushort)(frame[3] | (frame[4] << 8));
        var seq = frame[7];
        return command switch
        {
            NothingCommands.ModelId => [NothingRx(0x401C, seq, 0x68, 0xB1)],
            NothingCommands.Battery => [NothingRx(0x4007, seq, 0x03, 0x02, 0x5A, 0x03, 0x64, 0x04, 0x3C)],
            NothingCommands.AncRead => [NothingRx(0x401E, seq, 0x02, 0x01, 0x00, 0x01, 0x05, 0x00)],
            NothingCommands.EqRead => [NothingRx(0x4050, seq, 0x06)],
            NothingCommands.AncSet => [NothingRx(0x700F, seq)],
            _ => [],
        };
    }

    // Same script as SonyTests.Xm3Script: 70 % charging, noise cancelling.
    private static byte[][] SonyReply(byte[] frame)
    {
        var message = Assert.Single(new SonyReader().Feed(frame));
        if (message.Kind != 0x0C)
        {
            return [];
        }

        var chunks = new List<byte[]> { SonyFrame.Encode(0x01, (byte)(1 - message.Seq), []) };
        var script = new Dictionary<string, byte[]>
        {
            ["0000"] = [0x01, 0x00, 0x40, 0x10],
            ["1000"] = [0x11, 0x00, 0x46, 0x01],
            ["6602"] = [0x67, 0x02, 0x01, 0x00, 0x01, 0x00, 0x01, 0x05],
        };
        if (script.TryGetValue(Convert.ToHexString(message.Payload), out var reply))
        {
            chunks.Add(SonyFrame.Encode(0x0C, 0, reply));
        }

        return [.. chunks];
    }

    private static readonly Dictionary<string, Rig> Rigs = new()
    {
        ["Nothing"] = new("nothing.cmf-buds", new NothingDriver(true, CommandTimeout), () => new FakeRfcommChannel { Respond = NothingReply }),
        ["Sony"] = new("sony.wh-1000xm3", new SonyDriver(true, CommandTimeout), () => new FakeRfcommChannel { Respond = SonyReply }),
        ["Soundcore"] = new("soundcore.life-q30", new SoundcoreDriver(true, CommandTimeout), () =>
        {
            var channel = new FakeRfcommChannel();
            channel.Emit(SoundcoreVectors.Hex(SoundcoreVectors.Q30Example));
            return channel;
        }),
        ["Samsung"] = new("samsung.galaxy-buds2", new SamsungDriver(true, commandTimeout: CommandTimeout), () =>
        {
            var channel = new FakeRfcommChannel();
            channel.Emit(SamsungVectors.Frame("Buds2_rev10"));
            return channel;
        }),
        ["Bose"] = new("bose.qc-headphones", new BoseBmapDriver(TimeSpan.FromSeconds(1)), () => new FakeRfcommChannel { Respond = _ => [BoseFrames.ModeQuiet] },
            channel => new FakeBoseTransports(number => number == 8 ? channel : null)),
    };

    public static TheoryData<string> WritableDrivers => ["Nothing", "Sony", "Soundcore", "Samsung"];

    public static TheoryData<string> AllDrivers => ["Nothing", "Sony", "Soundcore", "Samsung", "Bose"];

    private static async Task DisposeAllTwiceAsync(IReadOnlyList<IFeature> features)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var feature in features)
            {
                switch (feature)
                {
                    case IAsyncDisposable asyncDisposable:
                        await asyncDisposable.DisposeAsync();
                        break;
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            }
        }
    }

    private static void AssertWorking(IReadOnlyList<IFeature> features, bool hasBattery)
    {
        Assert.NotEmpty(features.OfType<INoiseControlFeature>());
        if (hasBattery)
        {
            Assert.NotNull(Assert.Single(features.OfType<IBatteryFeature>()).Current.SummaryPercent);
        }
    }

    [Theory]
    [MemberData(nameof(WritableDrivers))]
    public async Task ACommandOnADroppedLink_FailsPromptly_AndDisposalIsSafe(string name)
    {
        var (features, channel) = await Rigs[name].OpenAsync();
        AssertWorking(features, hasBattery: true);
        Drop(channel);

        var started = Stopwatch.StartNew();
        var result = await features.OfType<INoiseControlFeature>().Single().SetModeAsync(NoiseMode.Transparency);

        Assert.True(started.Elapsed < PromptBound, $"took {started.Elapsed}");
        Assert.Equal(CommandStatus.Failed, result.Status);
        await DisposeAllTwiceAsync(features);
    }

    [Theory]
    [MemberData(nameof(WritableDrivers))]
    public async Task ALinkThatDropsWhileACommandWaits_FailsItPromptly(string name)
    {
        var (features, channel) = await Rigs[name].OpenAsync();
        channel.Respond = null; // the earbuds go quiet, so the command is left waiting for its reply
        var writesBefore = channel.Writes;

        var command = features.OfType<INoiseControlFeature>().Single().SetModeAsync(NoiseMode.Transparency);
        var sent = Stopwatch.StartNew();
        while (channel.Writes == writesBefore && sent.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(5);
        }

        Assert.True(channel.Writes > writesBefore, "the command was never written");
        Assert.False(command.IsCompleted);
        var dropped = Stopwatch.StartNew();
        channel.Close();
        var result = await command;

        Assert.True(dropped.Elapsed < PromptBound, $"took {dropped.Elapsed}");
        Assert.Equal(CommandStatus.Failed, result.Status);
        await DisposeAllTwiceAsync(features);
    }

    [Theory]
    [MemberData(nameof(WritableDrivers))]
    public async Task ReopeningOnAFreshChannel_GivesWorkingFeaturesWithABatteryReading(string name)
    {
        var rig = Rigs[name];
        var (features, channel) = await rig.OpenAsync();
        Drop(channel);
        await DisposeAllTwiceAsync(features);

        var (reopened, _) = await rig.OpenAsync();

        AssertWorking(reopened, hasBattery: true);
    }

    [Fact]
    public async Task Bose_DisposingAfterTheLinkDrops_IsSafe_AndReopeningReadsTheModeAgain()
    {
        var rig = Rigs["Bose"];
        var (features, channel) = await rig.OpenAsync();
        Drop(channel);
        await DisposeAllTwiceAsync(features);

        var (reopened, _) = await rig.OpenAsync();

        Assert.Equal(NoiseMode.NoiseCancelling, reopened.OfType<INoiseControlFeature>().Single().Current.Mode);
    }

    [Theory]
    [MemberData(nameof(AllDrivers))]
    public async Task OpenCloseDisposeReopen_Repeated_NeverThrowsOrLeaksAnUnobservedTaskFailure(string name)
    {
        var rig = Rigs[name];
        var unobserved = new List<Exception>();
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e) => unobserved.Add(e.Exception);
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            var (features, channel) = await rig.OpenAsync();
            for (var cycle = 0; cycle < StressCycles; cycle++)
            {
                Drop(channel);
                await DisposeAllTwiceAsync(features);
                (features, channel) = await rig.OpenAsync();
            }

            AssertWorking(features, hasBattery: name != "Bose");
            Drop(channel);
            await DisposeAllTwiceAsync(features);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }

        Assert.Empty(unobserved);
    }
}

using Samaat.Transport;
using Samaat.Protocols.FastPair;

namespace Samaat.Protocols.Tests;

public sealed class MessageStreamParserTests
{
    [Fact]
    public void CapturedGreetingInTwoChunks_YieldsFiveMessages()
    {
        var parser = new MessageStreamParser();

        var first = parser.Push(FastPairCaptured.Chunk1);
        var second = parser.Push(FastPairCaptured.Chunk2);

        Assert.Equal(3, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Equal([(byte)1, 2, 3, 9, 10], first.Concat(second).Select(m => m.Code));
        Assert.All(first.Concat(second), m => Assert.Equal(3, m.Group));
        Assert.Equal(Convert.FromHexString("67B851"), first[0].Data);
    }

    [Fact]
    public void GluedChunks_YieldFiveMessages() =>
        Assert.Equal(5, new MessageStreamParser().Push(FastPairCaptured.Greeting).Count);

    [Fact]
    public void OneByteAtATime_YieldsTheSameFiveMessages()
    {
        var parser = new MessageStreamParser();

        var messages = FastPairCaptured.Greeting.SelectMany(b => parser.Push([b])).ToList();

        Assert.Equal(5, messages.Count);
        Assert.Equal("1.0.0", System.Text.Encoding.ASCII.GetString(messages[3].Data));
    }

    [Fact]
    public void TruncatedMessage_WaitsForTheRest()
    {
        var parser = new MessageStreamParser();

        Assert.Empty(parser.Push(FastPairCaptured.Chunk2[..6]));
        Assert.Single(parser.Push(FastPairCaptured.Chunk2[6..9]));
    }

    [Fact]
    public void OversizeLength_DropsTheBufferAndRecovers()
    {
        var parser = new MessageStreamParser();

        Assert.Empty(parser.Push([0x03, 0x01, 0xFF, 0xFF, 0x01, 0x02]));

        Assert.Equal(3, parser.Push(FastPairCaptured.Chunk1).Count);
    }

    [Fact]
    public void Garbage_NeverThrows()
    {
        var parser = new MessageStreamParser();
        var random = new Random(7);

        for (var i = 0; i < 200; i++)
        {
            var junk = new byte[random.Next(0, 40)];
            random.NextBytes(junk);
            parser.Push(junk);
        }
    }

    [Fact]
    public void ZeroLengthMessage_IsDelivered()
    {
        var message = Assert.Single(new MessageStreamParser().Push([0x03, 0x20, 0x00, 0x00]));

        Assert.Equal(0x20, message.Code);
        Assert.Empty(message.Data);
    }
}

public sealed class FastPairMessagesTests
{
    private static StreamMessage Msg(byte code, string hex) => new(3, code, Convert.FromHexString(hex));

    [Fact]
    public void Battery_LeftNinetyRightSeventyCaseUnknown()
    {
        Assert.True(FastPairMessages.TryBattery(Msg(3, "5A46FF"), out var battery));

        Assert.Equal(new FastPairLevel(90, false), battery.Left);
        Assert.Equal(new FastPairLevel(70, false), battery.Right);
        Assert.Null(battery.Case);
    }

    [Fact]
    public void Battery_HighBitMeansCharging()
    {
        Assert.True(FastPairMessages.TryBattery(Msg(3, "DA46E4"), out var battery));

        Assert.Equal(new FastPairLevel(90, true), battery.Left);
        Assert.Equal(new FastPairLevel(70, false), battery.Right);
        Assert.Equal(new FastPairLevel(100, true), battery.Case);
    }

    [Fact]
    public void Battery_SevenFIsUnknown()
    {
        Assert.True(FastPairMessages.TryBattery(Msg(3, "7F7F64"), out var battery));

        Assert.Null(battery.Left);
        Assert.Null(battery.Right);
        Assert.Equal(new FastPairLevel(100, false), battery.Case);
    }

    [Theory]
    [InlineData("5A46")]
    [InlineData("")]
    [InlineData("5A46FF00")]
    public void Battery_WrongLength_IsRejected(string hex) => Assert.False(FastPairMessages.TryBattery(Msg(3, hex), out _));

    [Fact]
    public void Battery_FromAnotherGroupOrCode_IsRejected()
    {
        Assert.False(FastPairMessages.TryBattery(new StreamMessage(1, 3, [0x5A, 0x46, 0xFF]), out _));
        Assert.False(FastPairMessages.TryBattery(Msg(4, "5A46FF"), out _));
    }

    [Fact]
    public void Firmware_IsAscii()
    {
        Assert.True(FastPairMessages.TryFirmware(Msg(9, "312E302E30"), out var firmware));
        Assert.Equal("1.0.0", firmware);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0102")]
    [InlineData("312E00")]
    public void Firmware_EmptyOrNotPrintable_IsRejected(string hex) => Assert.False(FastPairMessages.TryFirmware(Msg(9, hex), out _));

    [Fact]
    public void ModelId_IsThreeBytesOfHex()
    {
        Assert.True(FastPairMessages.TryModelId(Msg(1, "67B851"), out var model));
        Assert.Equal("67B851", model);
        Assert.False(FastPairMessages.TryModelId(Msg(1, "67B8"), out _));
    }

    [Fact]
    public void BleAddress_IsSixBytesColonSeparated()
    {
        Assert.True(FastPairMessages.TryBleAddress(Msg(2, "F7A0C67986C6"), out var address));
        Assert.Equal("F7:A0:C6:79:86:C6", address);
        Assert.False(FastPairMessages.TryBleAddress(Msg(2, "F7A0"), out _));
    }

    [Fact]
    public void UnknownCodes_AreIgnored()
    {
        var session = Msg(0x0A, "0102030405060708");

        Assert.False(FastPairMessages.TryBattery(session, out _));
        Assert.False(FastPairMessages.TryFirmware(session, out _));
        Assert.False(FastPairMessages.TryModelId(session, out _));
        Assert.False(FastPairMessages.TryBleAddress(session, out _));
    }
}

public sealed class RfcommReceiveBufferTests
{
    private static byte[] Chunk(int i) => BitConverter.GetBytes(i);

    [Fact]
    public void ChunksBeforeTheFirstSubscriber_AreReplayedInOrder()
    {
        var buffer = new RfcommReceiveBuffer(this);
        buffer.Publish(Chunk(1));
        buffer.Publish(Chunk(2));
        var seen = new List<int>();

        buffer.Add((_, c) => seen.Add(BitConverter.ToInt32(c.Span)));
        buffer.Publish(Chunk(3));

        Assert.Equal([1, 2, 3], seen);
    }

    [Fact]
    public void ReplayHappensOnlyForTheFirstSubscriber()
    {
        var buffer = new RfcommReceiveBuffer(this);
        buffer.Publish(Chunk(1));
        var first = new List<int>();
        var second = new List<int>();

        buffer.Add((_, c) => first.Add(BitConverter.ToInt32(c.Span)));
        buffer.Add((_, c) => second.Add(BitConverter.ToInt32(c.Span)));
        buffer.Publish(Chunk(2));

        Assert.Equal([1, 2], first);
        Assert.Equal([2], second);
    }

    [Fact]
    public void Buffer_IsBounded_KeepingTheNewestChunks()
    {
        var buffer = new RfcommReceiveBuffer(this, capacity: 8);
        for (var i = 1; i <= 5; i++)
        {
            buffer.Publish(Chunk(i));
        }

        var seen = new List<int>();
        buffer.Add((_, c) => seen.Add(BitConverter.ToInt32(c.Span)));

        Assert.Equal([4, 5], seen);
    }

    [Fact]
    public void AfterAllSubscribersLeave_ChunksAreDropped_NotReplayedLater()
    {
        var buffer = new RfcommReceiveBuffer(this);
        EventHandler<ReadOnlyMemory<byte>> handler = (_, _) => { };
        buffer.Add(handler);
        buffer.Remove(handler);
        buffer.Publish(Chunk(1));
        var seen = new List<int>();

        buffer.Add((_, c) => seen.Add(BitConverter.ToInt32(c.Span)));

        Assert.Empty(seen);
    }

    [Fact]
    public async Task ABackgroundReaderWhileSubscribing_EveryChunkOnceInOrderNeverConcurrently()
    {
        const int Total = 600;
        var buffer = new RfcommReceiveBuffer(this);
        var seen = new List<int>();
        var inHandler = 0;
        var overlapped = false;
        var started = new ManualResetEventSlim();

        var reader = Task.Run(() =>
        {
            for (var i = 0; i < Total; i++)
            {
                buffer.Publish(Chunk(i));
                if (i == 50)
                {
                    started.Set();
                }
            }
        });
        started.Wait();
        buffer.Add((_, c) =>
        {
            if (Interlocked.Increment(ref inHandler) > 1)
            {
                overlapped = true;
            }

            seen.Add(BitConverter.ToInt32(c.Span));
            Interlocked.Decrement(ref inHandler);
        });
        await reader;

        Assert.False(overlapped);
        Assert.Equal(Enumerable.Range(0, Total), seen);
    }
}

/// <summary>SASS decoders. Built from the spec, not captured: the realme T200 Lite doesn't implement SASS.</summary>
public sealed class SassMessageTests
{
    private static StreamMessage Msg(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        return new StreamMessage(bytes[0], bytes[1], bytes[4..]);
    }

    [Fact]
    public void WriteFrames_AreExactlyTheTwoGetRequests()
    {
        Assert.Equal(Convert.FromHexString("07100000"), FastPairMessages.SassGetCapability);
        Assert.Equal(Convert.FromHexString("07330000"), FastPairMessages.SassGetConnectionStatus);
    }

    [Fact]
    public void Capability_DecodesVersionAndFlags()
    {
        Assert.True(FastPairMessages.TrySassCapability(Msg("07110004" + "0102" + "E000"), out var cap));
        Assert.Equal(new SassCapability(0x0102, true, true, true), cap);
        Assert.True(FastPairMessages.TrySassCapability(Msg("07110004" + "0102" + "4000"), out cap));
        Assert.Equal(new SassCapability(0x0102, false, true, false), cap);
    }

    [Fact]
    public void Capability_Version0Decodes_ShortDataAndWrongGroupAreRejected()
    {
        Assert.True(FastPairMessages.TrySassCapability(Msg("07110004" + "0000" + "0000"), out var cap));
        Assert.Equal(0, cap.Version);
        Assert.False(FastPairMessages.TrySassCapability(Msg("07110003" + "010280"), out _));
        Assert.False(FastPairMessages.TrySassCapability(Msg("07110005" + "0102E00000"), out _)); // provider replies are exactly 4 bytes
        Assert.False(FastPairMessages.TrySassCapability(Msg("03110004" + "0102E000"), out _));
        Assert.False(FastPairMessages.TrySassCapability(Msg("07100004" + "0102E000"), out _));
    }

    [Theory]
    [InlineData("00", SassActiveDevice.OtherSeeker)]
    [InlineData("01", SassActiveDevice.ThisSeeker)]
    [InlineData("02", SassActiveDevice.OtherDevice)]
    public void ConnectionStatus_DecodesTheActiveFlag_IgnoringTheEncryptedRest(string first, SassActiveDevice expected)
    {
        Assert.True(FastPairMessages.TrySassConnectionStatus(Msg("07340003" + first + "AABB"), out var active));
        Assert.Equal(expected, active);
    }

    [Fact]
    public void ConnectionStatus_RejectsUnknownFlagAndEmptyData()
    {
        Assert.False(FastPairMessages.TrySassConnectionStatus(Msg("07340001" + "03"), out _));
        Assert.False(FastPairMessages.TrySassConnectionStatus(Msg("07340000"), out _));
        Assert.False(FastPairMessages.TrySassConnectionStatus(Msg("03340001" + "01"), out _));
    }

    [Fact]
    public void SwitchEvent_DecodesReasonTargetAndName()
    {
        Assert.True(FastPairMessages.TrySassSwitchEvent(Msg("07320007" + "03" + "02" + Convert.ToHexString("Pixel"u8)), out var e));
        Assert.Equal(new SassSwitchEvent(3, false, "Pixel"), e);
        Assert.True(FastPairMessages.TrySassSwitchEvent(Msg("07320002" + "01" + "01"), out e));
        Assert.Equal(new SassSwitchEvent(1, true, ""), e);
    }

    [Theory]
    [InlineData("07320002" + "0103")]
    [InlineData("07320001" + "01")]
    [InlineData("07320004" + "0102" + "C328")] // malformed UTF-8
    [InlineData("03320002" + "0101")]
    public void SwitchEvent_RejectsBadInput(string hex) =>
        Assert.False(FastPairMessages.TrySassSwitchEvent(Msg(hex), out _));
}

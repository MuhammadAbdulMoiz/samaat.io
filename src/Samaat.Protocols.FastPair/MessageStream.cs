namespace Samaat.Protocols.FastPair;

/// <summary>One Message Stream message: <c>group(1) code(1) length(2, big-endian) data</c>.</summary>
public readonly record struct StreamMessage(byte Group, byte Code, byte[] Data);

/// <summary>
/// Reassembles Message Stream messages from RFCOMM chunks, which can split or glue them. Not thread-safe:
/// the channel delivers chunks one at a time. Never throws.
/// </summary>
public sealed class MessageStreamParser
{
    public const int MaxDataLength = 1024;

    private const int HeaderLength = 4;

    private readonly List<byte> _buffer = [];

    /// <summary>
    /// Returns the messages completed by <paramref name="chunk"/>. A length over <see cref="MaxDataLength"/>
    /// means the stream is out of step, so the buffer is dropped and parsing restarts with the next chunk.
    /// </summary>
    public IReadOnlyList<StreamMessage> Push(ReadOnlySpan<byte> chunk)
    {
        var messages = new List<StreamMessage>();
        _buffer.AddRange(chunk);
        var offset = 0;
        while (_buffer.Count - offset >= HeaderLength)
        {
            var length = (_buffer[offset + 2] << 8) | _buffer[offset + 3];
            if (length > MaxDataLength)
            {
                _buffer.Clear();
                return messages;
            }

            if (_buffer.Count - offset < HeaderLength + length)
            {
                break;
            }

            messages.Add(new StreamMessage(_buffer[offset], _buffer[offset + 1], _buffer.GetRange(offset + HeaderLength, length).ToArray()));
            offset += HeaderLength + length;
        }

        _buffer.RemoveRange(0, offset);
        return messages;
    }
}

/// <summary>A battery level: percent and whether that part is charging.</summary>
public readonly record struct FastPairLevel(int Percent, bool Charging);

/// <summary>Left bud, right bud and case; null means that part reported "unknown".</summary>
public readonly record struct FastPairBattery(FastPairLevel? Left, FastPairLevel? Right, FastPairLevel? Case);

/// <summary>Decoders for the Device Information group (0x03). Anything that doesn't fit is rejected, never thrown.</summary>
public static class FastPairMessages
{
    public const byte DeviceInformation = 0x03;

    private const byte ModelIdCode = 0x01;
    private const byte BleAddressCode = 0x02;
    private const byte BatteryCode = 0x03;
    private const byte FirmwareCode = 0x09;
    private const byte UnknownLevel = 0x7F;

    public static bool TryBattery(StreamMessage message, out FastPairBattery battery)
    {
        battery = default;
        if (!Is(message, BatteryCode) || message.Data.Length != 3)
        {
            return false;
        }

        battery = new FastPairBattery(Level(message.Data[0]), Level(message.Data[1]), Level(message.Data[2]));
        return true;
    }

    public static bool TryFirmware(StreamMessage message, out string firmware)
    {
        firmware = "";
        if (!Is(message, FirmwareCode) || message.Data.Length == 0 || message.Data.Any(b => b is < 0x20 or > 0x7E))
        {
            return false;
        }

        firmware = System.Text.Encoding.ASCII.GetString(message.Data).Trim();
        return firmware.Length > 0;
    }

    /// <summary>The 3-byte Fast Pair model ID as hex, e.g. "67B851".</summary>
    public static bool TryModelId(StreamMessage message, out string modelId)
    {
        modelId = "";
        if (!Is(message, ModelIdCode) || message.Data.Length != 3)
        {
            return false;
        }

        modelId = Convert.ToHexString(message.Data);
        return true;
    }

    /// <summary>The BLE address as "AA:BB:CC:DD:EE:FF".</summary>
    public static bool TryBleAddress(StreamMessage message, out string address)
    {
        address = "";
        if (!Is(message, BleAddressCode) || message.Data.Length != 6)
        {
            return false;
        }

        address = string.Join(':', message.Data.Select(b => b.ToString("X2")));
        return true;
    }

    private static bool Is(StreamMessage message, byte code) => message is { Group: DeviceInformation } && message.Code == code;

    // Low 7 bits are the percent, bit 7 is charging; 0x7F (or anything above 100) is "unknown".
    private static FastPairLevel? Level(byte value)
    {
        var percent = value & 0x7F;
        return percent is UnknownLevel or > 100 ? null : new FastPairLevel(percent, (value & 0x80) != 0);
    }
}

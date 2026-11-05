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

/// <summary>SASS capability: version (0 = unsupported) and the flags Samaat reads.</summary>
public readonly record struct SassCapability(int Version, bool SassOn, bool MultipointToggleable, bool MultipointOn);

/// <summary>Who is the active audio source, from the plain first byte of a SASS connection status.</summary>
public enum SassActiveDevice
{
    /// <summary>Passive; the active device is another seeker on the same account.</summary>
    OtherSeeker,

    ThisSeeker,

    /// <summary>Passive; the active device is not a SASS seeker.</summary>
    OtherDevice,
}

/// <summary>A multipoint switch the provider announced: why, whether it went to this device, and the target's name.</summary>
public readonly record struct SassSwitchEvent(byte Reason, bool ToThisDevice, string Name);

/// <summary>Decoders for the Device Information (0x03) and SASS (0x07) groups. Anything that doesn't fit is rejected, never thrown.</summary>
public static class FastPairMessages
{
    public const byte DeviceInformation = 0x03;
    public const byte Sass = 0x07;

    /// <summary>SASS get capability. With <see cref="SassGetConnectionStatus"/>, the only messages Samaat ever writes.</summary>
    public static readonly byte[] SassGetCapability = [0x07, 0x10, 0x00, 0x00];

    public static readonly byte[] SassGetConnectionStatus = [0x07, 0x33, 0x00, 0x00];

    private const byte ModelIdCode = 0x01;
    private const byte BleAddressCode = 0x02;
    private const byte BatteryCode = 0x03;
    private const byte FirmwareCode = 0x09;
    private const byte UnknownLevel = 0x7F;
    private const byte SassCapabilityCode = 0x11;
    private const byte SassSwitchEventCode = 0x32;
    private const byte SassConnectionStatusCode = 0x34;

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

    /// <summary>Flags are MSB first: 0x80 SASS on, 0x40 multipoint toggleable, 0x20 multipoint on.</summary>
    public static bool TrySassCapability(StreamMessage message, out SassCapability capability)
    {
        capability = default;
        if (!IsSass(message, SassCapabilityCode) || message.Data.Length != 4)
        {
            return false;
        }

        var flags = message.Data[2];
        capability = new SassCapability((message.Data[0] << 8) | message.Data[1], (flags & 0x80) != 0, (flags & 0x40) != 0, (flags & 0x20) != 0);
        return true;
    }

    /// <summary>Only the plain first byte; the rest is encrypted with an account key this PC doesn't have.</summary>
    public static bool TrySassConnectionStatus(StreamMessage message, out SassActiveDevice active)
    {
        active = default;
        if (!IsSass(message, SassConnectionStatusCode) || message.Data.Length < 1 || message.Data[0] > 2)
        {
            return false;
        }

        active = (SassActiveDevice)message.Data[0];
        return true;
    }

    public static bool TrySassSwitchEvent(StreamMessage message, out SassSwitchEvent switchEvent)
    {
        switchEvent = default;
        if (!IsSass(message, SassSwitchEventCode) || message.Data.Length < 2 || message.Data[1] is not (1 or 2))
        {
            return false;
        }

        try
        {
            var name = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(message.Data, 2, message.Data.Length - 2);
            switchEvent = new SassSwitchEvent(message.Data[0], message.Data[1] == 1, name);
            return true;
        }
        catch (Exception e) when (e is System.Text.DecoderFallbackException or ArgumentException)
        {
            return false;
        }
    }

    private static bool Is(StreamMessage message, byte code) => message is { Group: DeviceInformation } && message.Code == code;

    private static bool IsSass(StreamMessage message, byte code) => message is { Group: Sass } && message.Code == code;

    // Low 7 bits are the percent, bit 7 is charging; 0x7F (or anything above 100) is "unknown".
    private static FastPairLevel? Level(byte value)
    {
        var percent = value & 0x7F;
        return percent is UnknownLevel or > 100 ? null : new FastPairLevel(percent, (value & 0x80) != 0);
    }
}

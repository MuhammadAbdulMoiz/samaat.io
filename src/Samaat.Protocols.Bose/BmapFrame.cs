namespace Samaat.Protocols.Bose;

/// <summary>One BMAP packet: [block, function, flags, payload length, payload...]; Operator is the low nibble of flags.</summary>
internal readonly record struct BmapPacket(byte Block, byte Function, byte Operator, byte[] Payload);

internal static class BmapFrame
{
    public const byte OperatorGet = 0x01;
    public const byte OperatorStatus = 0x03;
    public const byte OperatorError = 0x04;

    /// <summary>The audio-mode block and function. Reading it is the only thing Samaat sends.</summary>
    public const byte AudioBlock = 0x1F;
    public const byte CurrentModeFunction = 0x03;

    public static byte[] Get(byte block, byte function) => [block, function, OperatorGet, 0x00];
}

/// <summary>Reassembles packets from RFCOMM chunks, which can split a packet or carry several.</summary>
internal sealed class BmapReader
{
    private const int HeaderLength = 4;
    private const int MaxBuffered = 4096;

    private readonly List<byte> _buffer = [];

    public IReadOnlyList<BmapPacket> Feed(ReadOnlySpan<byte> chunk)
    {
        _buffer.AddRange(chunk);
        var packets = new List<BmapPacket>();
        while (_buffer.Count >= HeaderLength && _buffer.Count >= HeaderLength + _buffer[3])
        {
            var length = _buffer[3];
            packets.Add(new BmapPacket(_buffer[0], _buffer[1], (byte)(_buffer[2] & 0x0F), _buffer.GetRange(HeaderLength, length).ToArray()));
            _buffer.RemoveRange(0, HeaderLength + length);
        }

        if (_buffer.Count > MaxBuffered)
        {
            _buffer.Clear();
        }

        return packets;
    }
}

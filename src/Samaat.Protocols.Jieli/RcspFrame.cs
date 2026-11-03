namespace Samaat.Protocols.Jieli;

/// <summary>One RCSP packet. <see cref="Params"/> excludes the sequence number.</summary>
public sealed record RcspPacket(byte Flags, byte Op, byte Sn, byte[] Params);

/// <summary>RCSP wire format: <c>FE DC BA | flags | op | len(2, BE) | sn | params | EF</c>, where len counts sn and params.</summary>
public static class RcspFrame
{
    public const byte Terminator = 0xEF;

    public const int MaxPayload = 2048;

    /// <summary>Builds a command frame. The adapter itself never sends one; see <see cref="RcspAuth"/>.</summary>
    public static byte[] Build(byte flags, byte op, byte sn, ReadOnlySpan<byte> parameters)
    {
        var length = parameters.Length + 1;
        var frame = new byte[8 + parameters.Length + 1];
        frame[0] = 0xFE;
        frame[1] = 0xDC;
        frame[2] = 0xBA;
        frame[3] = flags;
        frame[4] = op;
        frame[5] = (byte)(length >> 8);
        frame[6] = (byte)length;
        frame[7] = sn;
        parameters.CopyTo(frame.AsSpan(8));
        frame[^1] = Terminator;
        return frame;
    }
}

/// <summary>
/// Reassembles RCSP packets from BLE notification chunks, which may split or glue frames. Bad input
/// is skipped, never thrown on.
/// </summary>
public sealed class RcspFrameParser
{
    private const int HeaderLength = 7;

    private readonly List<byte> _buffer = [];

    public IReadOnlyList<RcspPacket> Feed(ReadOnlySpan<byte> chunk)
    {
        _buffer.AddRange(chunk);
        var packets = new List<RcspPacket>();

        while (true)
        {
            var start = IndexOfPreamble();
            if (start < 0)
            {
                KeepPossiblePreamblePrefix();
                break;
            }

            _buffer.RemoveRange(0, start);
            if (_buffer.Count < HeaderLength)
            {
                break;
            }

            var length = (_buffer[5] << 8) | _buffer[6];
            if (length is 0 or > RcspFrame.MaxPayload)
            {
                _buffer.RemoveAt(0);
                continue;
            }

            var total = HeaderLength + length + 1;
            if (_buffer.Count < total)
            {
                break;
            }

            if (_buffer[total - 1] != RcspFrame.Terminator)
            {
                _buffer.RemoveAt(0);
                continue;
            }

            packets.Add(new RcspPacket(_buffer[3], _buffer[4], _buffer[HeaderLength], _buffer.GetRange(HeaderLength + 1, length - 1).ToArray()));
            _buffer.RemoveRange(0, total);
        }

        return packets;
    }

    private int IndexOfPreamble()
    {
        for (var i = 0; i + 2 < _buffer.Count; i++)
        {
            if (_buffer[i] == 0xFE && _buffer[i + 1] == 0xDC && _buffer[i + 2] == 0xBA)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>With no preamble in the buffer, only a trailing partial one (FE or FE DC) can still become a frame.</summary>
    private void KeepPossiblePreamblePrefix()
    {
        var keep = 0;
        if (_buffer.Count >= 2 && _buffer[^2] == 0xFE && _buffer[^1] == 0xDC)
        {
            keep = 2;
        }
        else if (_buffer.Count >= 1 && _buffer[^1] == 0xFE)
        {
            keep = 1;
        }

        _buffer.RemoveRange(0, _buffer.Count - keep);
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/timschneeb/GalaxyBudsClient @ 754b6fdfb1c151b022cece775c8aaad4eb185afa, GalaxyBudsClient/Message/SppMessage.cs and GalaxyBudsClient/Utils/Crc16.cs, Copyright (c) 2019-2026 Tim Schneeberger and contributors, GPL-3.0.
// Changes: rewritten onto Samaat.Transport (IRfcommChannel) and Samaat.Core features.

namespace Samaat.Protocols.Samsung;

/// <summary>One parsed message: Samsung's one-byte id and the payload between the id and the CRC.</summary>
internal readonly record struct SppMessage(byte Id, byte[] Payload);

/// <summary>
/// Galaxy Buds framing. New: [0xFD][header lo][header hi][id][payload][crc lo][crc hi][0xDD], with the size
/// (id + payload + crc) in the low 10 bits of the header. Legacy (2019 Galaxy Buds): [0xFE][type][size][id][payload][crc][0xEE].
/// </summary>
internal static class SppFrame
{
    public const byte NewStart = 0xFD;
    public const byte NewEnd = 0xDD;
    public const byte LegacyStart = 0xFE;
    public const byte LegacyEnd = 0xEE;

    /// <summary>CRC-16/XMODEM (polynomial 0x1021, initial 0, no reflection).</summary>
    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (var b in data)
        {
            crc ^= (ushort)(b << 8);
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
            }
        }

        return crc;
    }

    /// <summary>A host request: the new header has no type bit set; the legacy type byte is 0 (request).</summary>
    public static byte[] Encode(byte id, ReadOnlySpan<byte> payload, bool legacy)
    {
        var size = payload.Length + 3;
        if (size > (legacy ? byte.MaxValue : 0x3FF))
        {
            throw new ArgumentException("Payload too long for one frame.", nameof(payload));
        }

        byte[] body = [id, .. payload];
        var crc = Crc16(body);
        byte[] head = legacy ? [LegacyStart, 0, (byte)size] : [NewStart, (byte)size, (byte)(size >> 8)];
        return [.. head, .. body, (byte)crc, (byte)(crc >> 8), legacy ? LegacyEnd : NewEnd];
    }
}

/// <summary>Reassembles messages from RFCOMM chunks, which can split a frame or carry several. One reader per framing.</summary>
internal sealed class SppReader(bool legacy)
{
    private const int HeaderLength = 3;
    private const int MinSize = 3;
    private const int FragmentBit = 0x2000;
    private const int MaxBuffered = 4096;

    private readonly List<byte> _buffer = [];
    private readonly byte _start = legacy ? SppFrame.LegacyStart : SppFrame.NewStart;
    private readonly byte _end = legacy ? SppFrame.LegacyEnd : SppFrame.NewEnd;

    public IReadOnlyList<SppMessage> Feed(ReadOnlySpan<byte> chunk)
    {
        _buffer.AddRange(chunk);
        var messages = new List<SppMessage>();
        while (true)
        {
            var start = _buffer.IndexOf(_start);
            if (start < 0)
            {
                _buffer.Clear();
                break;
            }

            _buffer.RemoveRange(0, start);
            if (_buffer.Count < HeaderLength)
            {
                break;
            }

            var header = _buffer[1] | (_buffer[2] << 8);
            var size = legacy ? _buffer[2] : header & 0x3FF;
            if (size < MinSize)
            {
                _buffer.RemoveAt(0);
                continue;
            }

            var total = HeaderLength + size + 1;
            if (_buffer.Count < total)
            {
                break;
            }

            if (_buffer[total - 1] != _end || !CrcMatches(total))
            {
                // Not a frame: skip this start byte and look for the next one.
                _buffer.RemoveAt(0);
                continue;
            }

            if (legacy || (header & FragmentBit) == 0)
            {
                messages.Add(new SppMessage(_buffer[3], _buffer.GetRange(4, size - 3).ToArray()));
            }

            _buffer.RemoveRange(0, total);
        }

        // Safety net: a frame is at most ~1 KB, so this only trips if the loop's invariants are broken.
        if (_buffer.Count > MaxBuffered)
        {
            _buffer.Clear();
        }

        return messages;
    }

    private bool CrcMatches(int total)
    {
        var body = _buffer.GetRange(3, total - 6).ToArray();
        return SppFrame.Crc16(body) == (_buffer[total - 3] | (_buffer[total - 2] << 8));
    }
}

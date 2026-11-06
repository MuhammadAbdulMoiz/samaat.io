// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/Oppzippy/OpenSCQ30 @ e75a68b70de8eaf2cf0ca364e132085a097557c5, lib/src/devices/soundcore/common/packet.rs and lib/src/devices/soundcore/common/packet/checksum.rs, Copyright (c) 2023-2026 Kyle Scheuing and contributors, GPL-3.0.
// Changes: rewritten in C# onto Samaat.Transport (IRfcommChannel) and Samaat.Core features.

namespace Samaat.Protocols.Soundcore;

/// <summary>One parsed inbound message: the two command bytes and the body between the length field and the checksum.</summary>
internal readonly record struct SoundcoreMessage(byte C0, byte C1, byte[] Body);

/// <summary>
/// Soundcore framing: <c>direction(5) | command(2) | length u16 LE | body | checksum(1)</c>. The length counts the whole
/// frame, header and checksum included; the checksum is the 8-bit wrapping sum of every byte before it.
/// </summary>
internal static class SoundcoreFrame
{
    public const int HeaderLength = 9;
    public const int MinLength = HeaderLength + 1;

    public static readonly byte[] OutboundDirection = [0x08, 0xEE, 0x00, 0x00, 0x00];
    public static readonly byte[] InboundDirection = [0x09, 0xFF, 0x00, 0x00, 0x01];

    public static byte Checksum(ReadOnlySpan<byte> data)
    {
        byte sum = 0;
        foreach (var b in data)
        {
            sum += b;
        }

        return sum;
    }

    /// <summary>A host request.</summary>
    public static byte[] Encode(byte c0, byte c1, ReadOnlySpan<byte> body)
    {
        var length = MinLength + body.Length;
        if (length > ushort.MaxValue)
        {
            throw new ArgumentException("Body too long for one frame.", nameof(body));
        }

        byte[] frame = [.. OutboundDirection, c0, c1, (byte)length, (byte)(length >> 8), .. body, 0];
        frame[^1] = Checksum(frame.AsSpan(0, frame.Length - 1));
        return frame;
    }
}

/// <summary>Reassembles inbound messages from RFCOMM chunks, which can split a frame or carry several.</summary>
internal sealed class SoundcoreReader
{
    private const int MaxBuffered = 4096;

    private readonly List<byte> _buffer = [];

    public IReadOnlyList<SoundcoreMessage> Feed(ReadOnlySpan<byte> chunk)
    {
        _buffer.AddRange(chunk);
        var messages = new List<SoundcoreMessage>();
        while (true)
        {
            var start = FindHeader();
            if (start < 0)
            {
                _buffer.Clear();
                break;
            }

            _buffer.RemoveRange(0, start);
            if (_buffer.Count < SoundcoreFrame.HeaderLength)
            {
                break;
            }

            var length = _buffer[7] | (_buffer[8] << 8);
            if (length < SoundcoreFrame.MinLength || length > MaxBuffered)
            {
                _buffer.RemoveAt(0);
                continue;
            }

            if (_buffer.Count < length)
            {
                break;
            }

            var frame = _buffer.GetRange(0, length).ToArray();
            if (SoundcoreFrame.Checksum(frame.AsSpan(0, length - 1)) != frame[^1])
            {
                _buffer.RemoveAt(0);
                continue;
            }

            messages.Add(new SoundcoreMessage(frame[5], frame[6], frame[SoundcoreFrame.HeaderLength..^1]));
            _buffer.RemoveRange(0, length);
        }

        // Safety net: the loop above never leaves more than one frame buffered.
        if (_buffer.Count > MaxBuffered)
        {
            _buffer.Clear();
        }

        return messages;
    }

    /// <summary>The first index holding a full inbound header, or a prefix of one at the end (more bytes may follow); -1 if none.</summary>
    private int FindHeader()
    {
        var header = SoundcoreFrame.InboundDirection;
        for (var i = 0; i < _buffer.Count; i++)
        {
            var available = Math.Min(header.Length, _buffer.Count - i);
            var matches = true;
            for (var j = 0; j < available && matches; j++)
            {
                matches = _buffer[i + j] == header[j];
            }

            if (matches)
            {
                return i;
            }
        }

        return -1;
    }
}

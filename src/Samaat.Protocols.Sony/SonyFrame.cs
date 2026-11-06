// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/Plutoberth/SonyHeadphonesClient @ 5620e8ed5deccb957338b54e371b215146080819, Client/CommandSerializer.cpp, Client/CommandSerializer.h, Client/Constants.h, Client/ByteMagic.cpp, Copyright (c) 2020 Nir Harel, Mor Gal, Sem Visscher, jimzrt, guilhermealbm, and other contributors, MIT.
// Changes: rewritten in C# onto Samaat.Transport (IRfcommChannel).

namespace Samaat.Protocols.Sony;

/// <summary>One parsed frame: its kind (0x01 acknowledgement, 0x0C command), sequence number and payload.</summary>
internal readonly record struct SonyMessage(byte Kind, byte Seq, byte[] Payload);

/// <summary>
/// Sony framing: <c>3E | escape(kind, seq, length u32 BE, payload, checksum) | 3C</c>. The checksum is the 8-bit sum of
/// every content byte before it; 3C, 3D and 3E inside the content are sent as 3D followed by 2C, 2D or 2E.
/// </summary>
internal static class SonyFrame
{
    public const byte Start = 0x3E;
    public const byte End = 0x3C;
    public const byte Escape = 0x3D;
    public const byte KindAck = 0x01;
    public const byte KindCommand = 0x0C;
    public const int MaxFrame = 2048;

    private const int HeaderLength = 6;

    public static byte[] Encode(byte kind, byte seq, ReadOnlySpan<byte> payload)
    {
        List<byte> content = [kind, seq, (byte)(payload.Length >> 24), (byte)(payload.Length >> 16), (byte)(payload.Length >> 8), (byte)payload.Length];
        content.AddRange(payload);
        content.Add(Sum(content));

        List<byte> frame = [Start];
        foreach (var b in content)
        {
            if (b is End or Escape or Start)
            {
                frame.Add(Escape);
                frame.Add((byte)(b - 0x10));
            }
            else
            {
                frame.Add(b);
            }
        }

        frame.Add(End);
        if (frame.Count > MaxFrame)
        {
            throw new ArgumentException("Payload too long for one frame.", nameof(payload));
        }

        return [.. frame];
    }

    /// <summary>The message in the bytes between the markers, or null for a bad escape, length or checksum.</summary>
    public static SonyMessage? Decode(IReadOnlyList<byte> escaped)
    {
        var content = new List<byte>(escaped.Count);
        for (var i = 0; i < escaped.Count; i++)
        {
            if (escaped[i] != Escape)
            {
                content.Add(escaped[i]);
                continue;
            }

            if (++i == escaped.Count || escaped[i] is not (0x2C or 0x2D or 0x2E))
            {
                return null;
            }

            content.Add((byte)(escaped[i] + 0x10));
        }

        if (content.Count < HeaderLength + 1)
        {
            return null;
        }

        var length = ((long)content[2] << 24) | ((long)content[3] << 16) | ((long)content[4] << 8) | content[5];
        if (length != content.Count - HeaderLength - 1 || Sum(content.GetRange(0, content.Count - 1)) != content[^1])
        {
            return null;
        }

        return new SonyMessage(content[0], content[1], content.GetRange(HeaderLength, (int)length).ToArray());
    }

    private static byte Sum(IEnumerable<byte> data)
    {
        byte sum = 0;
        foreach (var b in data)
        {
            sum += b;
        }

        return sum;
    }
}

/// <summary>Reassembles inbound messages from RFCOMM chunks, which can split a frame or carry several.</summary>
internal sealed class SonyReader
{
    private readonly List<byte> _buffer = [];
    private bool _inFrame;

    public IReadOnlyList<SonyMessage> Feed(ReadOnlySpan<byte> chunk)
    {
        var messages = new List<SonyMessage>();
        foreach (var b in chunk)
        {
            if (b == SonyFrame.Start)
            {
                // A bare start marker can't be inside a frame, so it always begins a new one.
                _buffer.Clear();
                _inFrame = true;
            }
            else if (!_inFrame)
            {
                continue;
            }
            else if (b == SonyFrame.End)
            {
                _inFrame = false;
                if (SonyFrame.Decode(_buffer) is { } message)
                {
                    messages.Add(message);
                }
            }
            else if (_buffer.Count >= SonyFrame.MaxFrame)
            {
                _inFrame = false;
            }
            else
            {
                _buffer.Add(b);
            }
        }

        return messages;
    }
}

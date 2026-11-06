// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/maniacx/BudsLink @ 0eb4d07dcd7eafabf425d98c607e02fc9519c94b, src/lib/devices/nothingBuds/{nothingBudsSocket.js,nothingBudsConfig.js,deviceConfigs/CMFBuds.js}, GPL-3.0 (BudsLink credits radiance-project/ear-web).
// Changes: rewritten in C# onto Samaat.Transport and Samaat.Core; ANC read offset and noise-cancelling byte corrected from Samaat's own CMF Buds captures (docs/hardware-tests/2026-11-06-cmf-buds.md).

namespace Samaat.Protocols.Nothing;

/// <summary>One parsed inbound frame. <see cref="Seq"/> is echoed from the request for replies and acks; pushes carry 0.</summary>
internal readonly record struct NothingMessage(ushort Command, byte Seq, byte[] Payload);

/// <summary>Command ids. 0xCxxx reads, 0xFxxx writes; the device answers 0x4xxx (reply) or 0x7xxx (ack) with the same low 12 bits.</summary>
internal static class NothingCommands
{
    public const ushort ModelId = 0xC01C;
    public const ushort Battery = 0xC007;
    public const ushort AncRead = 0xC01E;
    public const ushort AncSet = 0xF00F;
    public const ushort EqRead = 0xC050;
    public const ushort EqSet = 0xF01D;

    public const ushort BatteryReply = 0x4007;
    public const ushort BatteryPush = 0xE001;
    public const ushort AncReply = 0x401E;
    public const ushort AncPush = 0xE003;
    public const ushort EqReply = 0x4050;

    public static bool IsWrite(ushort command) => (command & 0xF000) == 0xF000;
}

/// <summary>
/// Framing: <c>55 | c0 c1 | cmd u16 LE | len u16 LE | seq | payload | [crc u16 LE]</c>. We always send <c>60 01</c> with a
/// CRC16/MODBUS over every earlier byte. Inbound frames carry a CRC only when bit 0x20 of the first flag byte is set; it isn't checked
/// (acks carry <c>FF FF</c>).
/// </summary>
internal static class NothingFrame
{
    public const byte Start = 0x55;
    public const int HeaderLength = 8;

    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
        }

        return crc;
    }

    public static byte[] Encode(ushort command, byte seq, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > ushort.MaxValue)
        {
            throw new ArgumentException("Payload too long for one frame.", nameof(payload));
        }

        byte[] frame = [Start, 0x60, 0x01, (byte)command, (byte)(command >> 8), (byte)payload.Length, (byte)(payload.Length >> 8), seq, .. payload, 0, 0];
        var crc = Crc16(frame.AsSpan(0, frame.Length - 2));
        frame[^2] = (byte)crc;
        frame[^1] = (byte)(crc >> 8);
        return frame;
    }
}

/// <summary>Reassembles inbound frames from RFCOMM chunks, which can split a frame or carry several.</summary>
internal sealed class NothingReader
{
    internal const int MaxBuffered = 4096;

    private readonly List<byte> _buffer = [];

    public int Buffered => _buffer.Count;

    public IReadOnlyList<NothingMessage> Feed(ReadOnlySpan<byte> chunk)
    {
        _buffer.AddRange(chunk);
        var messages = new List<NothingMessage>();
        while (true)
        {
            var start = _buffer.IndexOf(NothingFrame.Start);
            if (start < 0)
            {
                _buffer.Clear();
                break;
            }

            _buffer.RemoveRange(0, start);
            if (_buffer.Count < NothingFrame.HeaderLength)
            {
                break;
            }

            var length = _buffer[5] | (_buffer[6] << 8);
            var total = NothingFrame.HeaderLength + length + ((_buffer[1] & 0x20) != 0 ? 2 : 0);
            if (total > MaxBuffered)
            {
                _buffer.RemoveAt(0);
                continue;
            }

            if (_buffer.Count < total)
            {
                break;
            }

            var command = (ushort)(_buffer[3] | (_buffer[4] << 8));
            messages.Add(new NothingMessage(command, _buffer[7], _buffer.GetRange(NothingFrame.HeaderLength, length).ToArray()));
            _buffer.RemoveRange(0, total);
        }

        if (_buffer.Count > MaxBuffered)
        {
            _buffer.Clear();
        }

        return messages;
    }
}

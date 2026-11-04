using System.Buffers.Binary;

namespace Samaat.Handoff.Protocol;

/// <summary>
/// Length-prefixed frames over a byte stream: a 4-byte big-endian length, then the payload.
/// Reads and writes are each meant for one caller at a time; <see cref="Link.PeerChannel"/> serialises them.
/// </summary>
public sealed class FrameStream(Stream stream) : IAsyncDisposable
{
    /// <summary>Far above any real message; stops a bad peer from making us allocate gigabytes.</summary>
    public const int MaxFrameLength = 256 * 1024;

    public Stream Inner { get; } = stream;

    public async Task WriteAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        if (payload.Length > MaxFrameLength)
        {
            throw new InvalidDataException($"Frame of {payload.Length} bytes is over the {MaxFrameLength}-byte limit.");
        }

        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, payload.Length);
        payload.CopyTo(frame.AsMemory(4));
        await Inner.WriteAsync(frame, cancellationToken);
        await Inner.FlushAsync(cancellationToken);
    }

    /// <summary>Null when the stream ended cleanly between frames.</summary>
    public async Task<byte[]?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var header = new byte[4];
        if (!await ReadExactlyOrEndAsync(header, cancellationToken))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 0 or > MaxFrameLength)
        {
            throw new InvalidDataException($"Frame length {length} is out of range.");
        }

        var payload = new byte[length];
        if (length > 0 && !await ReadExactlyOrEndAsync(payload, cancellationToken))
        {
            throw new EndOfStreamException("The connection closed in the middle of a frame.");
        }

        return payload;
    }

    private async Task<bool> ReadExactlyOrEndAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await Inner.ReadAsync(buffer[read..], cancellationToken);
            if (n == 0)
            {
                return read == 0 ? false : throw new EndOfStreamException("The connection closed in the middle of a frame.");
            }

            read += n;
        }

        return true;
    }

    public ValueTask DisposeAsync() => Inner.DisposeAsync();
}

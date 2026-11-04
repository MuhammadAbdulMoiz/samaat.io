using System.Buffers.Binary;

namespace Samaat.Core.Audio;

public enum SampleEncoding { Pcm, Float }

/// <summary>Converts raw capture bytes to float samples and remaps channel counts.</summary>
public static class SampleConvert
{
    /// <summary>Converts whole samples of PCM 16/24/32 or float 32 to floats; returns the count written.</summary>
    public static int ToFloat(ReadOnlySpan<byte> raw, SampleEncoding encoding, int bitsPerSample, Span<float> dest)
    {
        var width = (encoding, bitsPerSample) switch
        {
            (SampleEncoding.Pcm, 16) => 2,
            (SampleEncoding.Pcm, 24) => 3,
            (SampleEncoding.Pcm, 32) => 4,
            (SampleEncoding.Float, 32) => 4,
            _ => throw new NotSupportedException($"{encoding} {bitsPerSample}-bit audio is not supported."),
        };
        var count = Math.Min(raw.Length / width, dest.Length);
        for (var i = 0; i < count; i++)
        {
            var s = raw.Slice(i * width, width);
            dest[i] = (encoding, width) switch
            {
                (SampleEncoding.Float, _) => BitConverter.ToSingle(s),
                (_, 2) => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768f,
                (_, 3) => ((s[0] << 8 | s[1] << 16 | s[2] << 24) >> 8) / 8388608f,
                _ => BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648f,
            };
        }
        return count;
    }

    /// <summary>Maps interleaved frames to mono (average of the first two channels) or stereo (first two, mono duplicated).</summary>
    public static int MapChannels(ReadOnlySpan<float> src, int srcChannels, Span<float> dest, int destChannels)
    {
        if (destChannels is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(destChannels));
        var frames = Math.Min(src.Length / srcChannels, dest.Length / destChannels);
        for (var f = 0; f < frames; f++)
        {
            var l = src[f * srcChannels];
            var r = srcChannels > 1 ? src[f * srcChannels + 1] : l;
            if (destChannels == 1) dest[f] = (l + r) / 2;
            else { dest[f * 2] = l; dest[f * 2 + 1] = r; }
        }
        return frames * destChannels;
    }
}

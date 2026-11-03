using Samaat.Core.Audio;

namespace Samaat.Core.Tests;

public class SampleConvertTests
{
    [Fact]
    public void Pcm16_ToFloat()
    {
        byte[] raw = [0xFF, 0x7F, 0x00, 0x80, 0x00, 0x00];
        var dest = new float[3];
        Assert.Equal(3, SampleConvert.ToFloat(raw, SampleEncoding.Pcm, 16, dest));
        Assert.Equal(1.0f, dest[0], 3);
        Assert.Equal(-1.0f, dest[1]);
        Assert.Equal(0f, dest[2]);
    }

    [Fact]
    public void Pcm24_ToFloat()
    {
        byte[] raw = [0xFF, 0xFF, 0x7F, 0x00, 0x00, 0x80];
        var dest = new float[2];
        Assert.Equal(2, SampleConvert.ToFloat(raw, SampleEncoding.Pcm, 24, dest));
        Assert.Equal(1.0f, dest[0], 5);
        Assert.Equal(-1.0f, dest[1]);
    }

    [Fact]
    public void Pcm32_ToFloat()
    {
        byte[] raw = [0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x80];
        var dest = new float[2];
        Assert.Equal(2, SampleConvert.ToFloat(raw, SampleEncoding.Pcm, 32, dest));
        Assert.Equal(0.5f, dest[0], 5);
        Assert.Equal(-1.0f, dest[1]);
    }

    [Fact]
    public void Float32_PassesThrough()
    {
        float[] src = [0.25f, -0.5f];
        var raw = new byte[8];
        Buffer.BlockCopy(src, 0, raw, 0, 8);
        var dest = new float[2];
        Assert.Equal(2, SampleConvert.ToFloat(raw, SampleEncoding.Float, 32, dest));
        Assert.Equal(src, dest);
    }

    [Fact]
    public void PartialTrailingSample_Ignored()
    {
        var dest = new float[5];
        Assert.Equal(2, SampleConvert.ToFloat(new byte[5], SampleEncoding.Pcm, 16, dest));
    }

    [Fact]
    public void Pcm8_Throws() =>
        Assert.Throws<NotSupportedException>(() =>
            SampleConvert.ToFloat(new byte[4], SampleEncoding.Pcm, 8, new float[4]));

    [Fact]
    public void Map_SixToStereo_TakesFirstTwo()
    {
        float[] src = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        var dest = new float[4];
        Assert.Equal(4, SampleConvert.MapChannels(src, 6, dest, 2));
        Assert.Equal(new float[] { 1, 2, 7, 8 }, dest);
    }

    [Fact]
    public void Map_StereoToMono_Averages()
    {
        var dest = new float[2];
        Assert.Equal(2, SampleConvert.MapChannels([1f, 3f, 2f, 4f], 2, dest, 1));
        Assert.Equal(new float[] { 2, 3 }, dest);
    }

    [Fact]
    public void Map_MonoToStereo_Duplicates()
    {
        var dest = new float[4];
        Assert.Equal(4, SampleConvert.MapChannels([1f, 2f], 1, dest, 2));
        Assert.Equal(new float[] { 1, 1, 2, 2 }, dest);
    }
}

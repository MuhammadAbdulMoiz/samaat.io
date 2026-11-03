using Samaat.Core.Audio;

namespace Samaat.Core.Tests;

public class PlayoutBufferTests
{
    private const int Rate = 48000;
    private const int Ch = 2;

    private static float[] Ramp(int startFrame, int frames)
    {
        var a = new float[frames * Ch];
        for (var i = 0; i < frames; i++) { a[i * Ch] = startFrame + i + 1; a[i * Ch + 1] = startFrame + i + 1; }
        return a;
    }

    private static int Frames(int ms) => ms * Rate / 1000;

    [Fact]
    public void Primes_UntilTargetReached()
    {
        var b = new PlayoutBuffer(Rate, Ch);
        b.Write(Ramp(0, Frames(50)));
        var o = new float[Frames(10) * Ch];
        b.Read(o);
        Assert.All(o, s => Assert.Equal(0f, s));

        b.Write(Ramp(Frames(50), Frames(20)));
        var r = new float[20 * Ch];
        Assert.Equal(r.Length, b.Read(r));
        for (var i = 0; i < 20; i++) Assert.Equal(i + 1, r[i * Ch]);
    }

    [Fact]
    public void Underrun_PlaysSilence_AndRefillsToTarget()
    {
        var b = new PlayoutBuffer(Rate, Ch);
        b.Write(Ramp(0, Frames(60)));
        var all = new float[Frames(200) * Ch]; // longer than the content, so it underruns
        b.Read(all);
        Assert.Equal(1f, all[0]);

        var o = new float[Frames(10) * Ch];
        b.Read(o);
        Assert.All(o, s => Assert.Equal(0f, s));

        b.Write(Ramp(0, Frames(30)));
        b.Read(o);
        Assert.All(o, s => Assert.Equal(0f, s));

        b.Write(Ramp(0, Frames(40)));
        b.Read(o);
        Assert.Equal(1f, o[0]);
    }

    [Fact]
    public void Overfull_DropsFrames()
    {
        var b = new PlayoutBuffer(Rate, Ch);
        var chunk = Ramp(0, Frames(10));
        b.Write(Ramp(0, Frames(160)));
        var o = new float[chunk.Length];
        // 1 frame per 1000 is slow: draining 100 ms takes ~100 s of reads
        for (var i = 0; i < 10_000; i++) { b.Write(chunk); b.Read(o); }
        Assert.True(b.DroppedFrames > 0);
        Assert.InRange(b.FillMs, 60 - 20, 60 + 20);
    }

    [Fact]
    public void Underfull_RepeatsFrames()
    {
        var b = new PlayoutBuffer(Rate, Ch);
        b.Write(Ramp(0, Frames(60)));
        b.Read(new float[Frames(30) * Ch]);
        b.Read(new float[2500 * Ch]);
        Assert.True(b.RepeatedFrames > 0);
    }

    [Fact]
    public void DelayMs_IsClamped()
    {
        var b = new PlayoutBuffer(Rate, Ch) { DelayMs = -5 };
        Assert.Equal(0, b.DelayMs);
        b.DelayMs = 900;
        Assert.Equal(500, b.DelayMs);
    }

    [Fact]
    public void Overflow_DropsOldest()
    {
        var b = new PlayoutBuffer(Rate, Ch);
        for (var s = 0; s < 3; s++) b.Write(Ramp(s * Rate, Rate));
        var o = new float[2];
        b.Read(o);
        Assert.Equal(Rate + 1, o[0]);
    }
}

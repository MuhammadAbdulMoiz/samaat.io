using Samaat.Protocols.Jieli;

namespace Samaat.Protocols.Tests;

public sealed class JlAuthCipherTests
{
    [Fact]
    public void Encrypt_MatchesKnownAnswer()
    {
        var cipher = JlAuthCipher.Encrypt(Convert.FromHexString("B6E080ECAFF322916D88FAD5AA34C2AC"));

        Assert.Equal("1D8897AC4604D332E8175E81BB292524", Convert.ToHexString(cipher));
    }
}

public sealed class RcspFrameTests
{
    [Fact]
    public void Build_LaysOutHeaderLengthSnAndTerminator()
    {
        var frame = RcspFrame.Build(0xC0, 0x02, 0x00, [0xFF, 0xFF, 0xFF, 0xFF]);

        Assert.Equal("FEDCBAC002000500FFFFFFFFEF", Convert.ToHexString(frame));
    }

    [Fact]
    public void Parser_ReadsTheCapturedPush()
    {
        var packet = Assert.Single(new RcspFrameParser().Feed(Captured.AdvInfoFrame));

        Assert.Equal(0x80, packet.Flags);
        Assert.Equal(0xC2, packet.Op);
        Assert.Equal(0x01, packet.Sn);
        Assert.Equal(Captured.AdvInfoParams, packet.Params);
    }

    [Fact]
    public void Parser_ReassemblesChunksAndSplitsGluedFrames()
    {
        var parser = new RcspFrameParser();
        var glued = Captured.AdvInfoFrame.Concat(Captured.OtherFrame).ToArray();

        var packets = new List<RcspPacket>();
        packets.AddRange(parser.Feed(glued[..6]));
        packets.AddRange(parser.Feed(glued[6..12]));
        packets.AddRange(parser.Feed(glued[12..]));

        Assert.Equal([(byte)0xC2, (byte)0x09], packets.Select(p => p.Op));
    }

    [Fact]
    public void Parser_SkipsGarbageBeforeAFrame()
    {
        var packets = new RcspFrameParser().Feed([0x00, 0xFE, 0xDC, 0x11, 0xFE, .. Captured.AdvInfoFrame]);

        Assert.Equal(0xC2, Assert.Single(packets).Op);
    }

    [Fact]
    public void Parser_WaitsForTruncatedFrame()
    {
        var parser = new RcspFrameParser();

        Assert.Empty(parser.Feed(Captured.AdvInfoFrame[..^1]));
        Assert.Single(parser.Feed(Captured.AdvInfoFrame[^1..]));
    }

    [Fact]
    public void Parser_SkipsOversizeLengthAndRecovers()
    {
        var oversize = new byte[] { 0xFE, 0xDC, 0xBA, 0x80, 0xC2, 0xFF, 0xFF, 0x01, 0xEF };

        var packets = new RcspFrameParser().Feed([.. oversize, .. Captured.OtherFrame]);

        Assert.Equal(0x09, Assert.Single(packets).Op);
    }

    [Fact]
    public void Parser_SkipsFrameWithoutTerminator()
    {
        var bad = (byte[])Captured.OtherFrame.Clone();
        bad[^1] = 0x00;

        var packets = new RcspFrameParser().Feed([.. bad, .. Captured.AdvInfoFrame]);

        Assert.Equal(0xC2, Assert.Single(packets).Op);
    }

    [Fact]
    public void Parser_NeverThrowsOnRandomBytes()
    {
        var random = new Random(7);
        var parser = new RcspFrameParser();
        for (var i = 0; i < 500; i++)
        {
            var chunk = new byte[random.Next(0, 40)];
            random.NextBytes(chunk);
            parser.Feed(chunk);
        }

        // Whatever noise came before, a clean frame is still understood eventually.
        var found = false;
        for (var i = 0; i < 3 && !found; i++)
        {
            found = parser.Feed(Captured.AdvInfoFrame).Any(p => p.Op == 0xC2);
        }

        Assert.True(found);
    }
}

public sealed class AdvInfoTests
{
    [Fact]
    public void CapturedPush_IsLeft100Right100CaseUnknown()
    {
        Assert.True(AdvInfo.TryParse(Captured.AdvInfoParams, out var info));

        Assert.Equal(new BudLevel(100, false), info!.Left);
        Assert.Equal(new BudLevel(100, false), info.Right);
        Assert.Null(info.Case);
    }

    [Fact]
    public void ChargingBit_IsSeparatedFromThePercent()
    {
        var parameters = (byte[])Captured.AdvInfoParams.Clone();
        parameters[14] = 0x80 | 55;
        parameters[16] = 0x80 | 20;

        Assert.True(AdvInfo.TryParse(parameters, out var info));

        Assert.Equal(new BudLevel(55, true), info!.Left);
        Assert.Equal(new BudLevel(20, true), info.Case);
    }

    [Fact]
    public void ZeroByte_MeansNoReading()
    {
        var parameters = (byte[])Captured.AdvInfoParams.Clone();
        parameters[14] = 0;
        parameters[15] = 0;

        Assert.True(AdvInfo.TryParse(parameters, out var info));
        Assert.Null(info!.Left);
        Assert.Null(info.Right);
    }

    [Fact]
    public void ImpossiblePercent_MeansNoReading()
    {
        var parameters = (byte[])Captured.AdvInfoParams.Clone();
        parameters[14] = 101;

        Assert.True(AdvInfo.TryParse(parameters, out var info));
        Assert.Null(info!.Left);
    }

    [Fact]
    public void TooShort_IsRejected() => Assert.False(AdvInfo.TryParse(Captured.AdvInfoParams[..16], out _));
}

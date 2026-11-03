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

public sealed class SysInfoCodecTests
{
    private static SysInfoAttrs Attrs(byte[] responseFrame)
    {
        var packet = Assert.Single(new RcspFrameParser().Feed(responseFrame));
        return SysInfo.ParseParams(packet.Params[1..])!;
    }

    [Fact]
    public void Response_SplitsStatusAndSnFromTheParams()
    {
        var packet = Assert.Single(new RcspFrameParser().Feed(Captured.Sys6000Response));

        Assert.True(packet.IsResponse);
        Assert.Equal(0x00, packet.Sn);
        Assert.Equal(0x02, packet.Params[0]);
        Assert.Equal(0xFF, packet.Params[1]);
    }

    [Fact]
    public void DeviceCommand_IsNotAResponse() =>
        Assert.False(Assert.Single(new RcspFrameParser().Feed(Captured.AdvInfoFrame)).IsResponse);

    [Fact]
    public void CapturedNoiseModes_AreOffNoiseCancellingTransparency()
    {
        var attrs = Attrs(Captured.Sys6000Response);

        Assert.Equal(0xFF, attrs.Function);
        var modes = SysInfo.ParseModes(attrs.Values[SysInfo.AttrNoiseModes])!;
        Assert.Equal([(byte)0, 1, 2], modes.Select(m => m.Mode));
        Assert.Equal(new VoiceMode(1, 0x4000, 0x4000, 0x4000, 0x4000), modes[1]);
        Assert.Equal(new VoiceMode(0, 0, 0, 0, 0), modes[0]);
        Assert.Equal(Captured.Modes[2], modes[2].ToBytes());
    }

    [Fact]
    public void CapturedCurrentNoiseMode_IsOff() =>
        Assert.Equal(0, VoiceMode.Parse(Attrs(Captured.Sys6000Response).Values[SysInfo.AttrCurrentNoise])!.Mode);

    [Fact]
    public void CapturedEq_IsClassical()
    {
        var eq = SysInfo.ParseEq(Attrs(Captured.Sys1010Response).Values[SysInfo.AttrEq])!;

        Assert.Equal(3, eq.Mode);
        Assert.Equal(Captured.PresetGains[3], eq.Gains);
    }

    [Fact]
    public void CapturedPresets_HaveTenFrequenciesAndSevenGainTables()
    {
        var table = SysInfo.ParsePresets(Attrs(Captured.Sys1010Response).Values[SysInfo.AttrEqPresets])!;

        Assert.Equal(Captured.Frequencies, table.Frequencies);
        Assert.Equal([(byte)0, 1, 2, 3, 4, 5, 6], table.Presets.Select(p => p.Mode));
        for (var i = 0; i < 7; i++)
        {
            Assert.Equal(Captured.PresetGains[i], table.Presets[i].Gains);
        }
    }

    [Fact]
    public void DynamicEqLayout_IsParsed()
    {
        var eq = SysInfo.ParseEq([0x83, 0x03, 0x01, 0xFF, 0x02])!;

        Assert.Equal(3, eq.Mode);
        Assert.Equal([(sbyte)1, -1, 2], eq.Gains);
    }

    [Fact]
    public void PresetSlotsOutsideModes0To6_AreDropped()
    {
        var value = (byte[])Captured.PresetsTlv[2..].Clone();
        value[1 + (2 * 10)] = 9;

        var table = SysInfo.ParsePresets(value)!;

        Assert.Equal([(byte)1, 2, 3, 4, 5, 6], table.Presets.Select(p => p.Mode));
    }

    [Fact]
    public void GetSysInfo_BuildsTheExactFrames()
    {
        Assert.Equal("FEDCBAC007000602FF00006000EF", Convert.ToHexString(RcspCommands.GetSysInfo(0x6000)!.ToFrame(2)));
        Assert.Equal("FEDCBAC007000603FF00002000EF", Convert.ToHexString(RcspCommands.GetSysInfo(0x2000)!.ToFrame(3)));
        Assert.Equal("FEDCBAC007000604FF00001010EF", Convert.ToHexString(RcspCommands.GetSysInfo(0x1010)!.ToFrame(4)));
        Assert.Equal("FEDCBAC007000605FF00000010EF", Convert.ToHexString(RcspCommands.GetSysInfo(0x0010)!.ToFrame(5)));
    }

    [Fact]
    public void SetNoiseMode_BuildsTheExactFrame()
    {
        var frame = RcspCommands.SetNoiseMode(VoiceMode.Parse(Captured.Modes[1])!).ToFrame(5);

        Assert.Equal("FEDCBAC008000D05FF0A0D014000400040004000EF", Convert.ToHexString(frame));
    }

    [Fact]
    public void SetEqPreset_BuildsTheExactFrames()
    {
        Assert.Equal(
            "FEDCBAC008000F0BFF0C0401FE000204FEFE00000404EF",
            Convert.ToHexString(RcspCommands.SetEqPreset(1, Captured.PresetGains[1])!.ToFrame(0x0B)));
        Assert.Equal(
            "FEDCBAC008000F0DFF0C040300080804000000000202EF",
            Convert.ToHexString(RcspCommands.SetEqPreset(3, Captured.PresetGains[3])!.ToFrame(0x0D)));
    }

    [Fact]
    public void Builders_RefuseAnythingOutsideTheAllowedShapes()
    {
        Assert.Null(RcspCommands.GetSysInfo(0x1234));
        Assert.Null(RcspCommands.SetEqPreset(7, Captured.PresetGains[0]));
        Assert.Null(RcspCommands.SetEqPreset(1, Captured.PresetGains[1][..9]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("FF00")]
    [InlineData("FF0A0D00")]
    [InlineData("FF05")]
    [InlineData("FF0A")]
    public void MalformedTlvs_GiveNull(string hex) => Assert.Null(SysInfo.ParseParams(Convert.FromHexString(hex)));

    [Fact]
    public void MalformedAttributeValues_GiveNull()
    {
        Assert.Null(VoiceMode.Parse(new byte[8]));
        Assert.Null(SysInfo.ParseModes([]));
        Assert.Null(SysInfo.ParseModes([0x02, .. Captured.Modes[0]]));
        Assert.Null(SysInfo.ParseEq([0x03, 0x01]));
        Assert.Null(SysInfo.ParseEq([0x83, 0x05, 0x01]));
        Assert.Null(SysInfo.ParseEq([]));
        Assert.Null(SysInfo.ParsePresets([]));
        Assert.Null(SysInfo.ParsePresets(Captured.PresetsTlv[2..^1]));
        Assert.Null(SysInfo.ParsePresets([0x00]));
        Assert.Null(SysInfo.ParsePresets([.. Captured.PresetsTlv[2..], 0x00]));
    }
}

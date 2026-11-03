namespace Samaat.Protocols.Jieli;

/// <summary>A noise mode as the buds list it: mode id plus left/right maximum and current gain, all big-endian.</summary>
public sealed record VoiceMode(byte Mode, ushort LeftMax, ushort RightMax, ushort LeftCurrent, ushort RightCurrent)
{
    public const int Length = 9;

    public static VoiceMode? Parse(ReadOnlySpan<byte> value) => value.Length != Length
        ? null
        : new VoiceMode(value[0], Be(value, 1), Be(value, 3), Be(value, 5), Be(value, 7));

    public byte[] ToBytes() =>
    [
        Mode,
        (byte)(LeftMax >> 8), (byte)LeftMax,
        (byte)(RightMax >> 8), (byte)RightMax,
        (byte)(LeftCurrent >> 8), (byte)LeftCurrent,
        (byte)(RightCurrent >> 8), (byte)RightCurrent,
    ];

    private static ushort Be(ReadOnlySpan<byte> value, int at) => (ushort)((value[at] << 8) | value[at + 1]);
}

/// <summary>One attribute list from a sys-info response or push: the function byte and each TLV's value by type.</summary>
public sealed record SysInfoAttrs(byte Function, IReadOnlyDictionary<byte, byte[]> Values);

/// <summary>The buds' current EQ: preset mode (0–6) and band gains in dB.</summary>
public sealed record EqState(byte Mode, IReadOnlyList<sbyte> Gains);

/// <summary>One preset from the presets attribute: its mode id and the gains for every band.</summary>
public sealed record EqPresetGains(byte Mode, IReadOnlyList<sbyte> Gains);

/// <summary>The presets attribute: band centre frequencies in Hz and the preset gains, in the buds' order.</summary>
public sealed record EqPresetTable(IReadOnlyList<ushort> Frequencies, IReadOnlyList<EqPresetGains> Presets);

/// <summary>Parsers for Jieli sys-info attributes. Malformed input gives null, never an exception.</summary>
public static class SysInfo
{
    public const byte FunctionPublic = 0xFF;
    public const byte AttrEq = 0x04;
    public const byte AttrEqPresets = 0x0C;
    public const byte AttrCurrentNoise = 0x0D;
    public const byte AttrNoiseModes = 0x0E;

    /// <summary>The fixed EQ layout: ten bands. Only this layout is ever written.</summary>
    public const int Bands = 10;

    private const int PresetSlots = 7;

    /// <summary>Parses <c>function | TLV…</c> where a TLV is <c>len(1) type(1) value(len-1)</c>.</summary>
    public static SysInfoAttrs? ParseParams(byte[] parameters)
    {
        if (parameters.Length < 1)
        {
            return null;
        }

        var values = new Dictionary<byte, byte[]>();
        var at = 1;
        while (at < parameters.Length)
        {
            var len = parameters[at];
            if (len < 1 || at + 1 + len > parameters.Length)
            {
                return null;
            }

            values[parameters[at + 1]] = parameters[(at + 2)..(at + 1 + len)];
            at += 1 + len;
        }

        return new SysInfoAttrs(parameters[0], values);
    }

    /// <summary>Attribute 0x0E: <c>count(1)</c> then count VoiceModes.</summary>
    public static IReadOnlyList<VoiceMode>? ParseModes(byte[] value)
    {
        if (value.Length < 1 || value.Length != 1 + (value[0] * VoiceMode.Length))
        {
            return null;
        }

        var modes = new List<VoiceMode>();
        for (var i = 0; i < value[0]; i++)
        {
            modes.Add(VoiceMode.Parse(value.AsSpan(1 + (i * VoiceMode.Length), VoiceMode.Length))!);
        }

        return modes;
    }

    /// <summary>Attribute 0x04: <c>mode gains(10)</c>, or with bit 7 of mode set <c>mode count gains(count)</c>.</summary>
    public static EqState? ParseEq(byte[] value)
    {
        if (value.Length < 1)
        {
            return null;
        }

        if ((value[0] & 0x80) == 0)
        {
            return value.Length == 1 + Bands ? new EqState(value[0], Signed(value.AsSpan(1))) : null;
        }

        return value.Length >= 2 && value.Length == 2 + value[1]
            ? new EqState((byte)(value[0] & 0x7F), Signed(value.AsSpan(2)))
            : null;
    }

    /// <summary>Attribute 0x0C: <c>n(1)</c>, n frequencies (2, BE), then 7 × (<c>mode gains(n)</c>). Only modes 0–6 are kept.</summary>
    public static EqPresetTable? ParsePresets(byte[] value)
    {
        if (value.Length < 1 || value[0] == 0)
        {
            return null;
        }

        int n = value[0];
        if (value.Length != 1 + (2 * n) + (PresetSlots * (1 + n)))
        {
            return null;
        }

        var freqs = Enumerable.Range(0, n).Select(i => (ushort)((value[1 + (2 * i)] << 8) | value[2 + (2 * i)])).ToList();
        var presets = new List<EqPresetGains>();
        for (var slot = 0; slot < PresetSlots; slot++)
        {
            var at = 1 + (2 * n) + (slot * (1 + n));
            if (value[at] <= 6 && presets.All(p => p.Mode != value[at]))
            {
                presets.Add(new EqPresetGains(value[at], Signed(value.AsSpan(at + 1, n))));
            }
        }

        return new EqPresetTable(freqs, presets);
    }

    private static sbyte[] Signed(ReadOnlySpan<byte> bytes)
    {
        var result = new sbyte[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            result[i] = (sbyte)bytes[i];
        }

        return result;
    }
}

/// <summary>An RCSP command before framing. Only <see cref="RcspCommands"/> can create one, so nothing else reaches the earbuds.</summary>
public sealed class RcspCommand
{
    private readonly byte[] _params;

    internal RcspCommand(byte op, byte[] parameters)
    {
        Op = op;
        _params = parameters;
    }

    public byte Op { get; }

    public byte[] ToFrame(byte sn) => RcspFrame.Build(RcspCommands.Flags, Op, sn, _params);
}

/// <summary>
/// The only RCSP commands Samaat ever sends after auth, built only inside this adapter from what the buds
/// listed. The earbuds are real hardware: nothing else
/// (and never an op in 0xD0–0xEF, which includes OTA update and reboot) is built anywhere.
/// </summary>
public static class RcspCommands
{
    /// <summary>Command flags: bit 7 = command, bit 6 = please respond.</summary>
    public const byte Flags = 0xC0;

    public const byte GetSysInfoOp = 0x07;
    public const byte SetSysInfoOp = 0x08;

    /// <summary>Noise modes (0x0E) and current noise mode (0x0D).</summary>
    public const uint MaskNoise = 0x6000;

    /// <summary>Current noise mode only.</summary>
    public const uint MaskNoiseCurrent = 0x2000;

    /// <summary>Current EQ (0x04) and the preset table (0x0C).</summary>
    public const uint MaskEq = 0x1010;

    /// <summary>Current EQ only.</summary>
    public const uint MaskEqCurrent = 0x0010;

    /// <summary>Null unless <paramref name="mask"/> is one of the four masks Samaat reads.</summary>
    internal static RcspCommand? GetSysInfo(uint mask) => mask is MaskNoise or MaskNoiseCurrent or MaskEq or MaskEqCurrent
        ? new RcspCommand(GetSysInfoOp, [SysInfo.FunctionPublic, (byte)(mask >> 24), (byte)(mask >> 16), (byte)(mask >> 8), (byte)mask])
        : null;

    /// <summary>Sets the noise mode; <paramref name="mode"/> must be exactly what the buds listed.</summary>
    internal static RcspCommand SetNoiseMode(VoiceMode mode) =>
        new(SetSysInfoOp, [SysInfo.FunctionPublic, VoiceMode.Length + 1, SysInfo.AttrCurrentNoise, .. mode.ToBytes()]);

    /// <summary>Selects an EQ preset (mode 0–6) with the ten gains the buds listed for it. Null on any other input.</summary>
    internal static RcspCommand? SetEqPreset(byte mode, IReadOnlyList<sbyte> gains) => mode > 6 || gains.Count != SysInfo.Bands
        ? null
        : new RcspCommand(
            SetSysInfoOp,
            [SysInfo.FunctionPublic, SysInfo.Bands + 2, SysInfo.AttrEq, mode, .. gains.Select(g => (byte)g)]);
}

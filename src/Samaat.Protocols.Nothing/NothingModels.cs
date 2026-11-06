// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/maniacx/BudsLink @ 0eb4d07dcd7eafabf425d98c607e02fc9519c94b, src/lib/devices/nothingBuds/{nothingBudsSocket.js,nothingBudsConfig.js,deviceConfigs/CMFBuds.js}, GPL-3.0 (BudsLink credits radiance-project/ear-web).
// Changes: rewritten in C# onto Samaat.Transport and Samaat.Core; ANC read offset and noise-cancelling byte corrected from Samaat's own CMF Buds captures (docs/hardware-tests/2026-11-06-cmf-buds.md).

using Samaat.Core.Equalizer;
using Samaat.Core.NoiseControl;

namespace Samaat.Protocols.Nothing;

/// <summary>What differs between Nothing/CMF models: the noise modes and the EQ presets, as the buds number them.</summary>
internal sealed record NothingModel(string Name, IReadOnlyList<NoiseMode> NoiseModes, IReadOnlyList<EqPreset> Presets);

/// <summary>The model table, keyed by the model id the buds report for C01C (u16 LE).</summary>
internal static class NothingModels
{
    /// <summary>CMF Buds, model id bytes <c>68 B1</c>.</summary>
    public const ushort CmfBuds = 0xB168;

    private static readonly Dictionary<ushort, NothingModel> Table = new()
    {
        [CmfBuds] = new NothingModel(
            "CMF Buds",
            [NoiseMode.Off, NoiseMode.NoiseCancelling, NoiseMode.Transparency],
            [
                new(0, "Balanced"), new(1, "Rock"), new(2, "Electronic"), new(3, "Pop"),
                new(4, "Enhance vocals"), new(5, "Classical"), new(6, "Custom"),
            ]),
    };

    public static bool TryGet(ushort modelId, out NothingModel model) => Table.TryGetValue(modelId, out model!);
}

/// <summary>The noise-control bytes: 05 off, 07 transparency, 01/02/03 noise cancelling high/mid/low. 04 is ignored by CMF Buds and never sent.</summary>
internal static class NothingAnc
{
    public const byte Off = 0x05;
    public const byte Transparency = 0x07;
    public const byte DefaultNoiseCancelling = 0x01;

    public static bool IsNoiseCancelling(byte raw) => raw is >= 1 and <= 3;

    public static NoiseMode? ModeOf(byte raw) => raw switch
    {
        Off => NoiseMode.Off,
        Transparency => NoiseMode.Transparency,
        _ when IsNoiseCancelling(raw) => NoiseMode.NoiseCancelling,
        _ => null,
    };

    /// <summary>The raw mode byte in a 401E reply (<c>02 01 00 01 MM 00</c>) or an E003 push (<c>01 MM 00</c>); null for other messages.</summary>
    public static byte? RawOf(NothingMessage message) => message switch
    {
        { Command: NothingCommands.AncReply, Payload.Length: >= 5 } => message.Payload[4],
        { Command: NothingCommands.AncPush, Payload.Length: >= 2 } => message.Payload[1],
        _ => null,
    };
}

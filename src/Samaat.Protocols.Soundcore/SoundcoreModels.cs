// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/Oppzippy/OpenSCQ30 @ e75a68b70de8eaf2cf0ca364e132085a097557c5, lib/src/devices/soundcore/a3028.rs, a3027.rs, a3931.rs, a3951.rs, a3040.rs and lib/src/devices/soundcore/common/device.rs, Copyright (c) 2023-2026 Kyle Scheuing and contributors, GPL-3.0.
// Changes: rewritten in C# onto Samaat.Transport (IRfcommChannel) and Samaat.Core features.

using System.Buffers.Binary;
using System.Globalization;
using Samaat.Core.Battery;

namespace Samaat.Protocols.Soundcore;

internal enum SoundcoreBattery
{
    /// <summary>One level and one charging flag (body[0], body[1]).</summary>
    Single,

    /// <summary>Left and right levels and charging flags (body[2..5]).</summary>
    Dual,

    /// <summary>One level, charging not reported (body[0]).</summary>
    LevelOnly,
}

/// <summary>What differs between Soundcore models. Offsets index the body of the 01 01 state message.</summary>
internal sealed record SoundcoreModel(SoundcoreBattery Battery, int? EqOffset, int? SoundModesOffset)
{
    /// <summary>The shortest state body that holds every field this model reads.</summary>
    public int MinStateLength { get; } = Math.Max(
        Battery switch { SoundcoreBattery.Single => 2, SoundcoreBattery.Dual => 6, _ => 1 },
        Math.Max(EqOffset + 2 ?? 0, SoundModesOffset + SoundcoreSoundModes.Length ?? 0));
}

/// <summary>The model table, keyed by device catalog entry id (devices/soundcore).</summary>
internal static class SoundcoreModels
{
    private static readonly SoundcoreModel Headphones = new(SoundcoreBattery.Single, 2, 35);

    // SoundModesOffset is deliberately null for the earbuds: the offset is unconfirmed.
    private static readonly SoundcoreModel Earbuds = new(SoundcoreBattery.Dual, 6, null);

    private static readonly Dictionary<string, SoundcoreModel> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["soundcore.life-q30"] = Headphones,
        ["soundcore.life-tune"] = Headphones,
        ["soundcore.life-q35"] = Headphones,
        ["soundcore.life-tune-pro"] = Headphones,
        ["soundcore.life-dot-2-nc"] = Earbuds,
        ["soundcore.life-a2-nc"] = Earbuds,
        ["soundcore.liberty-air-2-pro"] = Earbuds,
        ["soundcore.space-q45"] = new(SoundcoreBattery.LevelOnly, null, null),
    };

    public static IReadOnlyCollection<string> Ids => Table.Keys;

    public static bool TryGet(string entryId, out SoundcoreModel model) => Table.TryGetValue(entryId, out model!);
}

/// <summary>Picks the RFCOMM service: Soundcore's vendor UUID family if the device lists one, else plain SPP.</summary>
internal static class SoundcoreService
{
    private static readonly Guid Spp = Guid.Parse("00001101-0000-1000-8000-00805f9b34fb");
    private static readonly UInt128 LowBits = 0xFFFFF;
    private static readonly UInt128 Base = UInt128.Parse("0cf12d31fac34553bd80d6832e700000", NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    public static Guid Pick(IReadOnlySet<Guid> advertised) =>
        advertised.Where(IsSoundcore).OrderBy(g => g.ToString("D"), StringComparer.Ordinal).Cast<Guid?>().FirstOrDefault() ?? Spp;

    private static bool IsSoundcore(Guid uuid) =>
        (BinaryPrimitives.ReadUInt128BigEndian(uuid.ToByteArray(bigEndian: true)) & ~LowBits) == (Base & ~LowBits);
}

/// <summary>The four sound-mode bytes: ambient (0 noise cancelling, 1 transparency, 2 normal), then the NC, transparency and custom NC sub-modes.</summary>
internal readonly record struct SoundcoreSoundModes(byte Ambient, byte NoiseCancelling, byte Transparency, byte CustomNoiseCancelling)
{
    public const int Length = 4;

    public static SoundcoreSoundModes From(ReadOnlySpan<byte> body) => new(body[0], body[1], body[2], body[3]);
}

/// <summary>
/// The 01 01 state body, as far as Samaat reads it. <see cref="Level"/> is the raw 0-5 level (left for dual models),
/// and the EQ and sound modes are null when the model has none or the body is too short.
/// </summary>
internal sealed record SoundcoreState(
    int Level,
    int? RightLevel,
    ChargingState Charging,
    ChargingState RightCharging,
    int? EqRaw,
    SoundcoreSoundModes? Modes)
{
    public static SoundcoreState? TryParse(SoundcoreModel model, SoundcoreMessage message)
    {
        if (message is not { C0: 0x01, C1: 0x01 })
        {
            return null;
        }

        var body = message.Body;
        if (body.Length < model.MinStateLength)
        {
            return null;
        }

        int? eq = model.EqOffset is { } eqAt && body.Length >= eqAt + 2 ? BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(eqAt)) : null;
        SoundcoreSoundModes? modes = model.SoundModesOffset is { } modesAt && body.Length >= modesAt + SoundcoreSoundModes.Length
            ? SoundcoreSoundModes.From(body.AsSpan(modesAt))
            : null;

        return model.Battery switch
        {
            SoundcoreBattery.Single when body.Length >= 2 => new(body[0], null, ChargingOf(body[1]), ChargingState.Unknown, eq, modes),
            SoundcoreBattery.Dual when body.Length >= 6 => new(body[2], body[3], ChargingOf(body[4]), ChargingOf(body[5]), eq, modes),
            SoundcoreBattery.LevelOnly when body.Length >= 1 => new(body[0], null, ChargingState.Unknown, ChargingState.Unknown, eq, modes),
            _ => null,
        };
    }

    public static ChargingState ChargingOf(byte raw) => raw switch
    {
        1 => ChargingState.Charging,
        0 => ChargingState.NotCharging,
        _ => ChargingState.Unknown,
    };
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/timschneeb/GalaxyBudsClient @ 754b6fdfb1c151b022cece775c8aaad4eb185afa, GalaxyBudsClient/Message/SppMessageEnums.cs and GalaxyBudsClient/Message/Decoder/ExtendedStatusUpdateDecoder.cs and GalaxyBudsClient/Message/Decoder/StatusUpdateDecoder.cs, Copyright (c) 2019-2026 Tim Schneeberger and contributors, GPL-3.0.
// Changes: rewritten onto Samaat.Transport (IRfcommChannel) and Samaat.Core features.

namespace Samaat.Protocols.Samsung;

/// <summary>Message ids (the numbers only; the names are Samaat's).</summary>
internal static class SamsungIds
{
    public const byte Ack = 0x42;
    public const byte StatusUpdate = 0x60;
    public const byte ExtendedStatus = 0x61;
    public const byte NoiseControlsUpdate = 0x77;
    public const byte NoiseControlsSet = 0x78;
    public const byte Equalizer = 0x86;
    public const byte ManagerInfo = 0x88;
}

internal enum SamsungPlacement
{
    Unknown,
    Disconnected,
    Wearing,
    Idle,
    InCase,
}

/// <summary>
/// What a status message carries. Levels are the raw bytes (above 100 means unknown); a null raw value
/// means the message is too short to hold that field or the model has no such field.
/// </summary>
internal sealed record SamsungStatus(
    int Left,
    int Right,
    int? CaseRaw,
    SamsungPlacement PlacementLeft,
    SamsungPlacement PlacementRight,
    int? EqRaw,
    int? NoiseRaw)
{
    public int? LeftPercent => Percent(Left);

    public int? RightPercent => Percent(Right);

    public int? CasePercent => CaseRaw is { } raw ? Percent(raw) : null;

    public static int? Percent(int raw) => raw <= 100 ? raw : null;

    /// <summary>Null when the message isn't a status message or is too short to hold both bud levels.</summary>
    public static SamsungStatus? TryParse(SppMessage message, SamsungModel model) => message.Id switch
    {
        SamsungIds.ExtendedStatus => Extended(message.Payload, model),
        SamsungIds.StatusUpdate => Update(message.Payload, model.Legacy),
        _ => null,
    };

    private static SamsungStatus? Extended(byte[] p, SamsungModel model)
    {
        if (p.Length < 4)
        {
            return null;
        }

        var (left, right) = model.Legacy ? (SamsungPlacement.Unknown, SamsungPlacement.Unknown) : Placement(At(p, 6));
        return new SamsungStatus(
            p[2],
            p[3],
            model.Legacy ? null : At(p, 7),
            left,
            right,
            model.EqOffset is { } offset ? At(p, offset) : null,
            model.NoiseModes ? At(p, 12) : null);
    }

    private static SamsungStatus? Update(byte[] p, bool legacy)
    {
        if (p.Length < 3)
        {
            return null;
        }

        var (left, right) = legacy ? (SamsungPlacement.Unknown, SamsungPlacement.Unknown) : Placement(At(p, 5));
        return new SamsungStatus(p[1], p[2], legacy ? null : At(p, 6), left, right, null, null);
    }

    private static int? At(byte[] p, int index) => index < p.Length ? p[index] : null;

    // High nibble is the left bud, low nibble the right.
    private static (SamsungPlacement Left, SamsungPlacement Right) Placement(int? raw) =>
        raw is { } b ? (Of(b >> 4), Of(b & 0x0F)) : (SamsungPlacement.Unknown, SamsungPlacement.Unknown);

    private static SamsungPlacement Of(int nibble) => nibble switch
    {
        0 => SamsungPlacement.Disconnected,
        1 => SamsungPlacement.Wearing,
        2 => SamsungPlacement.Idle,
        3 => SamsungPlacement.InCase,
        _ => SamsungPlacement.Unknown,
    };
}

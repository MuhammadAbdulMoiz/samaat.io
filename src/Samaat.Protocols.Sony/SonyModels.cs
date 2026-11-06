// SPDX-License-Identifier: GPL-3.0-or-later
// Written for Samaat from docs/protocols/sony.md (clean-room: behaviour learned from Gadgetbridge, no code copied).

namespace Samaat.Protocols.Sony;

internal enum SonyBatteryKind
{
    /// <summary>One battery (<c>t</c> 0).</summary>
    Headset,

    /// <summary>Left and right buds (<c>t</c> 1) and the case (<c>t</c> 2).</summary>
    BudsAndCase,
}

/// <summary>What differs between Sony models: which battery is asked for, and whether the noise mode is.</summary>
internal sealed record SonyModel(SonyBatteryKind Battery, bool Noise);

/// <summary>The model table, keyed by device catalog entry id (devices/sony).</summary>
internal static class SonyModels
{
    private static readonly Dictionary<string, SonyModel> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sony.wh-1000xm3"] = new(SonyBatteryKind.Headset, true),
        ["sony.wh-1000xm4"] = new(SonyBatteryKind.Headset, true),
        ["sony.wf-1000xm3"] = new(SonyBatteryKind.BudsAndCase, true),
        ["sony.mdr-xb950bt"] = new(SonyBatteryKind.Headset, false),
    };

    public static IReadOnlyCollection<string> Ids => Table.Keys;

    public static bool TryGet(string entryId, out SonyModel model) => Table.TryGetValue(entryId, out model!);
}

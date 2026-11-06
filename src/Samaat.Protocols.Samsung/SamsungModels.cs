// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/timschneeb/GalaxyBudsClient @ 754b6fdfb1c151b022cece775c8aaad4eb185afa, GalaxyBudsClient/Model/Constants.cs and GalaxyBudsClient/Model/Specifications/*.cs, Copyright (c) 2019-2026 Tim Schneeberger and contributors, GPL-3.0.
// Changes: rewritten onto Samaat.Transport (IRfcommChannel) and Samaat.Core features.

namespace Samaat.Protocols.Samsung;

/// <summary>What differs between Galaxy Buds models: the RFCOMM service, the framing and where the status bytes sit.</summary>
internal sealed record SamsungModel(Guid Service, bool Legacy, int? EqOffset, bool NoiseModes);

/// <summary>The model table, keyed by device catalog entry id (devices/samsung).</summary>
internal static class SamsungModels
{
    private static readonly Guid SppLegacy = Guid.Parse("00001102-0000-1000-8000-00805f9b34fd");
    private static readonly Guid SppStandard = Guid.Parse("00001101-0000-1000-8000-00805f9b34fb");
    private static readonly Guid SppNew = Guid.Parse("2e73a4ad-332d-41fc-90e2-16bef06523f2");

    private static readonly Dictionary<string, SamsungModel> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["samsung.galaxy-buds"] = new(SppLegacy, true, null, false),
        ["samsung.galaxy-buds-plus"] = new(SppStandard, false, 11, false),
        ["samsung.galaxy-buds-live"] = new(SppStandard, false, 9, false),
        ["samsung.galaxy-buds-pro"] = new(SppStandard, false, 9, true),
        ["samsung.galaxy-buds2"] = new(SppNew, false, 9, true),
        ["samsung.galaxy-buds2-pro"] = new(SppNew, false, 9, true),
        ["samsung.galaxy-buds-fe"] = new(SppNew, false, 9, true),
        ["samsung.galaxy-buds3"] = new(SppNew, false, 9, true),
        ["samsung.galaxy-buds3-pro"] = new(SppNew, false, 9, true),
        ["samsung.galaxy-buds3-fe"] = new(SppNew, false, 9, true),
        ["samsung.galaxy-buds-core"] = new(SppNew, false, 9, true),
    };

    public static IReadOnlyCollection<string> Ids => Table.Keys;

    public static bool TryGet(string entryId, out SamsungModel model) => Table.TryGetValue(entryId, out model!);
}

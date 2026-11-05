namespace Samaat.Core.Profiles;

/// <summary>What the hero artwork shows: a model photo, the split part photos, or one of the two "?" looks.</summary>
public enum LookKind { Photo, PartsPhoto, UnknownSingle, UnknownPieces }

public sealed record ResolvedLook(LookKind Kind, DeviceProfile? Profile = null);

/// <summary>Picks the hero artwork for a device. Shared by the desktop and the phone, so both draw a device the same way.</summary>
public static class ArtworkLook
{
    public const string Automatic = "auto";
    public const string UnknownSingle = "unknown.single";
    public const string UnknownPieces = "unknown.pieces";

    /// <summary>
    /// <paramref name="choice"/> is "auto" (or null), one of the unknown keys, or a profile id. A photo needs the profile's full photo;
    /// its part photos are used only with it and when each bud is reported. A choice that names no usable model acts as Automatic.
    /// </summary>
    public static ResolvedLook Resolve(string? choice, DeviceProfile? matched, IReadOnlyList<DeviceProfile> profiles, bool reportsBuds)
    {
        switch (choice)
        {
            case UnknownSingle:
                return new ResolvedLook(LookKind.UnknownSingle);
            case UnknownPieces:
                return new ResolvedLook(LookKind.UnknownPieces);
        }

        var profile = (choice is null or Automatic ? null : profiles.FirstOrDefault(p => p.Id == choice && p.Image is not null))
            ?? (matched?.Image is not null ? matched : null);
        return profile is null
            ? new ResolvedLook(reportsBuds ? LookKind.UnknownPieces : LookKind.UnknownSingle)
            : new ResolvedLook(reportsBuds && profile.PartImages is not null ? LookKind.PartsPhoto : LookKind.Photo, profile);
    }

    /// <summary>The key a choice is shown under in a "Change look" menu: the choice when it's usable, else "auto".</summary>
    public static string MenuKey(string? choice, IReadOnlyList<DeviceProfile> profiles) =>
        choice is UnknownSingle or UnknownPieces || profiles.Any(p => p.Id == choice && p.Image is not null) ? choice! : Automatic;
}

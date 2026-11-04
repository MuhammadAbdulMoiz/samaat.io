using System.Windows.Input;
using Samaat.Core.Profiles;

namespace Samaat.App.Features.Overview;

/// <summary>What the hero artwork shows: a model photo, the split part photos, or one of the two "?" looks.</summary>
public enum LookKind { Photo, PartsPhoto, UnknownSingle, UnknownPieces }

public sealed record ResolvedLook(LookKind Kind, DeviceProfile? Profile = null);

/// <summary>One entry of the "Change look" menu; <see cref="Key"/> is "auto", "unknown.single", "unknown.pieces" or a profile id.</summary>
public sealed record LookOptionViewModel(string Key, string Label, bool IsSelected, ICommand Select)
{
    public string AccessibleName => IsSelected ? $"{Label}, current look" : Label;
}

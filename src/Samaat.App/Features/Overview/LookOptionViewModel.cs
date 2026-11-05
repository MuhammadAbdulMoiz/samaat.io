using System.Windows.Input;
using Samaat.Core.Profiles;

namespace Samaat.App.Features.Overview;

/// <summary>One entry of the "Change look" menu; <see cref="Key"/> is "auto", "unknown.single", "unknown.pieces" or a profile id.</summary>
public sealed record LookOptionViewModel(string Key, string Label, bool IsSelected, ICommand Select)
{
    public string AccessibleName => IsSelected ? $"{Label}, current look" : Label;
}

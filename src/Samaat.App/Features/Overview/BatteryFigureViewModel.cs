using CommunityToolkit.Mvvm.ComponentModel;

namespace Samaat.App.Features.Overview;

/// <summary>One number in the battery panel: label, value, unit, meta. Unreported values read "–".</summary>
public sealed partial class BatteryFigureViewModel(string label) : ObservableObject
{
    public string Label { get; } = label;

    /// <summary>Which picture the tile shows next to its label: a bud (mirrored for the right one), the case, or none for Lowest bud.</summary>
    public bool IsBud => Label is "Left bud" or "Right bud";

    public bool IsRightBud => Label == "Right bud";

    public bool IsCase => Label == "Case";

    /// <summary>The model's own photo of this part, shown instead of the drawn bud or case; null draws the generic glyph.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsBudGlyph), nameof(ShowsCaseGlyph))]
    public partial Avalonia.Media.Imaging.Bitmap? Photo { get; set; }

    public bool ShowsBudGlyph => IsBud && Photo is null;

    public bool ShowsCaseGlyph => IsCase && Photo is null;

    [ObservableProperty]
    public partial string Value { get; set; } = "–";

    [ObservableProperty]
    public partial string Unit { get; set; } = "";

    [ObservableProperty]
    public partial string Meta { get; set; } = "";

    [ObservableProperty]
    public partial double Percent { get; set; }

    [ObservableProperty]
    public partial bool HasLevel { get; set; }

    [ObservableProperty]
    public partial bool IsReported { get; set; }

    [ObservableProperty]
    public partial bool IsLow { get; set; }

    [ObservableProperty]
    public partial bool IsCritical { get; set; }

    [ObservableProperty]
    public partial bool IsCharging { get; set; }

    /// <summary>What a screen reader says, e.g. "Left bud, 80 percent, charging".</summary>
    [ObservableProperty]
    public partial string AccessibleText { get; set; } = "";
}

using CommunityToolkit.Mvvm.ComponentModel;

namespace Samaat.App.Features.Overview;

/// <summary>One number in the battery panel: label, value, unit, meta. Unreported values read "–".</summary>
public sealed partial class BatteryFigureViewModel(string label) : ObservableObject
{
    public string Label { get; } = label;

    /// <summary>Which picture the tile shows next to its label: a bud, the case, or none for Lowest bud.</summary>
    public bool IsBud => Label is "Left bud" or "Right bud";

    public bool IsCase => Label == "Case";

    /// <summary>The model's own photo of this part, shown instead of the mystery piece; null shows the mystery piece.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsUnknownBud), nameof(ShowsUnknownCase))]
    public partial Avalonia.Media.Imaging.Bitmap? Photo { get; set; }

    public bool ShowsUnknownBud => IsBud && Photo is null;

    public bool ShowsUnknownCase => IsCase && Photo is null;

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

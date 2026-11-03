using CommunityToolkit.Mvvm.ComponentModel;

namespace Samaat.App.Features.Overview;

/// <summary>One number in the battery panel: label, value, unit, meta. Unreported values read "–".</summary>
public sealed partial class BatteryFigureViewModel(string label) : ObservableObject
{
    public string Label { get; } = label;

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
}

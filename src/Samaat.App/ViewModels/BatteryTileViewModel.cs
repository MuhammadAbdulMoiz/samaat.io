using CommunityToolkit.Mvvm.ComponentModel;
using Samaat.Core.Battery;
using Samaat.Core.Capabilities;

namespace Samaat.App.ViewModels;

public sealed partial class BatteryTileViewModel(BatteryComponent component) : ObservableObject
{
    /// <summary>Readings older than this are flagged so a frozen value isn't mistaken for a live one.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    private DateTimeOffset? _updatedAt;

    public BatteryComponent Component { get; } = component;

    public string Label { get; } = component switch
    {
        BatteryComponent.Left => "Left bud",
        BatteryComponent.Right => "Right bud",
        BatteryComponent.Case => "Case",
        _ => "Battery",
    };

    public string Glyph { get; } = component switch
    {
        BatteryComponent.Left => "L",
        BatteryComponent.Right => "R",
        BatteryComponent.Case => "▭",
        _ => "●",
    };

    [ObservableProperty]
    public partial bool IsAvailable { get; set; }

    /// <summary>Hidden tiles aren't rendered at all (used for the "overall" tile on bud-reporting devices).</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    [ObservableProperty]
    public partial string PercentText { get; set; } = "—";

    [ObservableProperty]
    public partial double Percent { get; set; }

    [ObservableProperty]
    public partial string DetailText { get; set; } = "";

    [ObservableProperty]
    public partial string UpdatedText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsLow { get; set; }

    [ObservableProperty]
    public partial bool IsCritical { get; set; }

    [ObservableProperty]
    public partial bool IsCharging { get; set; }

    [ObservableProperty]
    public partial bool IsStale { get; set; }

    public void Update(Capability capability, BatteryReading? reading, BatteryAlertPolicy policy, DateTimeOffset now)
    {
        IsAvailable = capability.UiState == CapabilityUiState.Enabled;
        _updatedAt = reading?.UpdatedAt;

        if (capability.UiState is CapabilityUiState.Unsupported or CapabilityUiState.Undetected
            || (capability.UiState == CapabilityUiState.Disabled && reading?.Percent is null))
        {
            PercentText = "—";
            Percent = 0;
            DetailText = capability.DisabledReason ?? "";
            IsLow = IsCritical = IsCharging = false;
            UpdateAge(now);
            return;
        }

        var percent = reading?.Percent;
        PercentText = percent is { } p ? $"{p}%" : "—";
        Percent = percent ?? 0;
        IsCharging = reading?.Charging == ChargingState.Charging;
        IsCritical = percent <= policy.CriticalThreshold && !IsCharging;
        IsLow = percent <= policy.LowThreshold && !IsCharging && !IsCritical;
        DetailText = capability.UiState == CapabilityUiState.Disabled
            ? capability.DisabledReason ?? ""
            : Describe(reading);
        UpdateAge(now);
    }

    public void UpdateAge(DateTimeOffset now)
    {
        if (_updatedAt is not { } updatedAt)
        {
            UpdatedText = "";
            IsStale = false;
            return;
        }

        var age = now - updatedAt;
        IsStale = age > StaleAfter;
        UpdatedText = age switch
        {
            { TotalSeconds: < 5 } => "Updated just now",
            { TotalMinutes: < 1 } => $"Updated {(int)age.TotalSeconds}s ago",
            { TotalHours: < 1 } => $"Updated {(int)age.TotalMinutes} min ago",
            _ => $"Updated {(int)age.TotalHours} h ago",
        };
    }

    private static string Describe(BatteryReading? reading)
    {
        if (reading is null)
        {
            return "Waiting for first reading";
        }

        var parts = new List<string>();
        switch (reading.Status)
        {
            case BudStatus.InCase:
                parts.Add("In case");
                break;
            case BudStatus.Disconnected:
                parts.Add("Not connected");
                break;
        }

        switch (reading.Charging)
        {
            case ChargingState.Charging:
                parts.Add("Charging");
                break;
            case ChargingState.Full:
                parts.Add("Fully charged");
                break;
        }

        if (reading.Percent is null)
        {
            parts.Add("Level unavailable");
        }

        return parts.Count == 0 ? "In use" : string.Join(" · ", parts);
    }
}

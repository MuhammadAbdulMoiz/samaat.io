namespace Samaat.Core.Settings;

public enum ThemeChoice
{
    System,
    Light,
    Dark,
}

public sealed record AppSettings
{
    // Dark matches the app's design; System is a choice, not the default.
    public ThemeChoice Theme { get; init; } = ThemeChoice.Dark;

    public int LowThreshold { get; init; } = 20;

    public int CriticalThreshold { get; init; } = 10;

    public bool IncludeCase { get; init; }

    public bool SystemNotifications { get; init; } = true;

    public bool CloseToTray { get; init; } = true;

    public bool StartWithWindows { get; init; }

    /// <summary>Clamps thresholds to what the alert logic can use: low 5–50, critical 1 up to just below low.</summary>
    public AppSettings Normalize()
    {
        var low = Math.Clamp(LowThreshold, 5, 50);
        return this with { LowThreshold = low, CriticalThreshold = Math.Clamp(CriticalThreshold, 1, low - 1) };
    }
}

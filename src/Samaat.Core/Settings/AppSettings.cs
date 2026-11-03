namespace Samaat.Core.Settings;

public enum AccentChoice
{
    Lime,
    Emerald,
    Ice,
    Mono,
}

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

    public AccentChoice Accent { get; init; } = AccentChoice.Lime;

    /// <summary>Null follows the operating system; only the user toggling the switch stores a bool.</summary>
    public bool? ReduceMotion { get; init; }

    public int LowThreshold { get; init; } = 20;

    public int CriticalThreshold { get; init; } = 10;

    public bool IncludeCase { get; init; }

    public bool SystemNotifications { get; init; } = true;

    public bool CloseToTray { get; init; } = true;

    public bool StartWithWindows { get; init; }

    /// <summary>Output the audio is shared to; null when sharing is off.</summary>
    public string? ShareTargetId { get; init; }

    public int ShareDelayMs { get; init; }

    /// <summary>Clamps thresholds to what the alert logic can use: low 5–50, critical 1 up to just below low.</summary>
    public AppSettings Normalize()
    {
        var low = Math.Clamp(LowThreshold, 5, 50);
        return this with
        {
            LowThreshold = low,
            CriticalThreshold = Math.Clamp(CriticalThreshold, 1, low - 1),
            ShareDelayMs = Math.Clamp(ShareDelayMs, 0, 500),
        };
    }
}

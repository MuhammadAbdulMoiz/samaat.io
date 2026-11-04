using System.Collections.Frozen;

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

    /// <summary>Share delay in milliseconds per output id, so each device remembers its own.</summary>
    public IReadOnlyDictionary<string, int> ShareDelays { get; init; } = FrozenDictionary<string, int>.Empty;

    /// <summary>The artwork look picked per device id: "unknown.single", "unknown.pieces" or a profile id; absent means automatic.</summary>
    public IReadOnlyDictionary<string, string> ArtworkChoices { get; init; } = FrozenDictionary<string, string>.Empty;

    /// <summary>Clamps thresholds to what the alert logic can use (low 5–50, critical 1 up to just below low) and share delays to 0–500 ms.</summary>
    public AppSettings Normalize()
    {
        var low = Math.Clamp(LowThreshold, 5, 50);
        return this with
        {
            LowThreshold = low,
            CriticalThreshold = Math.Clamp(CriticalThreshold, 1, low - 1),
            ShareDelays = (ShareDelays ?? FrozenDictionary<string, int>.Empty).ToDictionary(p => p.Key, p => Math.Clamp(p.Value, 0, 500)),
            ArtworkChoices = (ArtworkChoices ?? FrozenDictionary<string, string>.Empty).ToDictionary(p => p.Key, p => p.Value),
        };
    }
}

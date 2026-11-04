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
    /// <summary>The schema this build writes. Files without a version are version 1.</summary>
    public const int CurrentVersion = 2;

    private static readonly AppSettings Defaults = new();

    /// <summary>The schema version of the file; a newer build's number is kept, never lowered.</summary>
    public int Version { get; init; } = CurrentVersion;

    public DashboardView Dashboard { get; init; } = DashboardView.Studio;

    public DeviceFilter DeviceFilter { get; init; } = DeviceFilter.Connected;

    /// <summary>When each device id was last seen connected, for the "Recent" filter.</summary>
    public IReadOnlyDictionary<string, DateTimeOffset> LastConnected { get; init; } = FrozenDictionary<string, DateTimeOffset>.Empty;

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
            LastConnected = (LastConnected ?? FrozenDictionary<string, DateTimeOffset>.Empty).ToDictionary(p => p.Key, p => p.Value),
            // Unknown or out-of-range values (a newer build's, or a typo) fall back to the defaults.
            Theme = Enum.IsDefined(Theme) ? Theme : Defaults.Theme,
            Accent = Enum.IsDefined(Accent) ? Accent : Defaults.Accent,
            Dashboard = Enum.IsDefined(Dashboard) ? Dashboard : Defaults.Dashboard,
            DeviceFilter = Enum.IsDefined(DeviceFilter) ? DeviceFilter : Defaults.DeviceFilter,
            Version = Math.Max(Version, 1),
        };
    }

    /// <summary>The same settings stamped for saving by this build: never lowers a newer build's version.</summary>
    public AppSettings Upgraded() => this with { Version = Math.Max(Version, CurrentVersion) };
}

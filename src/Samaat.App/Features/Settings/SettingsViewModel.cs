using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Samaat.Core.Battery;
using Samaat.Core.Platform;
using Samaat.Core.Settings;
using Samaat.Core.Diagnostics;

namespace Samaat.App.Features.Settings;

/// <summary>The Settings page: every change is normalized, saved and applied straight away.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly LowBatteryDetector _detector;
    private readonly IStartupRegistration _startup;
    private readonly IMotionPreference _motion;
    private bool? _reduceMotionOverride;
    private bool _syncing;
    private Dictionary<string, int> _shareDelays = new();
    private Dictionary<string, string> _artworkChoices = new();
    private Dictionary<string, DateTimeOffset> _lastConnected = new();
    private int _version = AppSettings.CurrentVersion;

    public SettingsViewModel(SettingsStore store, LowBatteryDetector detector, IStartupRegistration startup, IMotionPreference? motion = null)
    {
        _store = store;
        _detector = detector;
        _startup = startup;
        _motion = motion ?? new NoMotionPreference();

        StartupSupported = startup.IsSupported;
        StartupNote = startup.IsSupported ? "" : "Not available on this system.";
        // The system's Run entry is the truth: the user can remove it outside the app.
        Show(store.Load() with { StartWithWindows = startup.IsSupported && startup.IsEnabled });
        ErrorText = store.LastError;
        Apply();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSystemTheme), nameof(IsLightTheme), nameof(IsDarkTheme))]
    public partial ThemeChoice Theme { get; set; }

    public bool IsSystemTheme => Theme == ThemeChoice.System;

    public bool IsLightTheme => Theme == ThemeChoice.Light;

    public bool IsDarkTheme => Theme == ThemeChoice.Dark;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLimeAccent), nameof(IsEmeraldAccent), nameof(IsIceAccent), nameof(IsMonoAccent))]
    public partial AccentChoice Accent { get; set; }

    public bool IsLimeAccent => Accent == AccentChoice.Lime;

    public bool IsEmeraldAccent => Accent == AccentChoice.Emerald;

    public bool IsIceAccent => Accent == AccentChoice.Ice;

    public bool IsMonoAccent => Accent == AccentChoice.Mono;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStudioView), nameof(IsFocusView), nameof(IsGlanceView), nameof(DashboardName))]
    public partial DashboardView Dashboard { get; set; }

    public bool IsStudioView => Dashboard == DashboardView.Studio;

    public bool IsFocusView => Dashboard == DashboardView.Focus;

    public bool IsGlanceView => Dashboard == DashboardView.Glance;

    /// <summary>The layout's name as the switcher shows it.</summary>
    public string DashboardName => DashboardNames.Name(Dashboard);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnectedFilter), nameof(IsRecentFilter), nameof(IsPairedFilter))]
    public partial DeviceFilter DeviceFilter { get; set; }

    public bool IsConnectedFilter => DeviceFilter == DeviceFilter.Connected;

    public bool IsRecentFilter => DeviceFilter == DeviceFilter.Recent;

    public bool IsPairedFilter => DeviceFilter == DeviceFilter.Paired;

    /// <summary>What the switch shows: the user's choice, or the system setting until they make one.</summary>
    [ObservableProperty]
    public partial bool ReduceMotion { get; set; }

    /// <summary>The Phone section; set by the shell.</summary>
    public Phone.PhoneLinkViewModel? PhoneLink { get; set; }

    /// <summary>The phone handoff is switched off (<see cref="AppOptions.PhoneHandoff"/>): the Phone panel says it's coming.</summary>
    public bool ShowPhoneComingSoon { get; set; }

    // Doubles because Avalonia's Slider binds to double; values are always whole numbers.
    [ObservableProperty]
    public partial double LowThreshold { get; set; }

    [ObservableProperty]
    public partial double CriticalThreshold { get; set; }

    [ObservableProperty]
    public partial double CriticalMax { get; set; }

    [ObservableProperty]
    public partial bool IncludeCase { get; set; }

    [ObservableProperty]
    public partial bool SystemNotifications { get; set; }

    [ObservableProperty]
    public partial bool CloseToTray { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    public bool StartupSupported { get; }

    public string StartupNote { get; }

    /// <summary>"Version 1.0.0" for the About section, without the build hash.</summary>
    public string VersionText { get; } = "Version " + (typeof(SettingsViewModel).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0]
        ?? typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "unknown");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorText { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>The settings as currently applied.</summary>
    public AppSettings Current => new AppSettings
    {
        Version = _version,
        Dashboard = Dashboard,
        DeviceFilter = DeviceFilter,
        LastConnected = new Dictionary<string, DateTimeOffset>(_lastConnected),
        Theme = Theme,
        Accent = Accent,
        ReduceMotion = _reduceMotionOverride,
        LowThreshold = (int)LowThreshold,
        CriticalThreshold = (int)CriticalThreshold,
        IncludeCase = IncludeCase,
        SystemNotifications = SystemNotifications,
        CloseToTray = CloseToTray,
        StartWithWindows = StartWithWindows,
        ShareDelays = new Dictionary<string, int>(_shareDelays),
        ArtworkChoices = new Dictionary<string, string>(_artworkChoices),
    }.Normalize().Upgraded();

    [RelayCommand]
    private void SetTheme(ThemeChoice theme) => Theme = theme;

    [RelayCommand]
    private void SetAccent(AccentChoice accent) => Accent = accent;

    [RelayCommand]
    private void SetDashboard(DashboardView view) => Dashboard = view;

    [RelayCommand]
    private void SetDeviceFilter(DeviceFilter filter) => DeviceFilter = filter;

    partial void OnDashboardChanged(DashboardView value) => Changed();

    partial void OnDeviceFilterChanged(DeviceFilter value) => Changed();

    partial void OnThemeChanged(ThemeChoice value) => Changed();

    partial void OnAccentChanged(AccentChoice value) => Changed();

    partial void OnReduceMotionChanged(bool value)
    {
        if (_syncing)
        {
            return;
        }

        _reduceMotionOverride = value;
        Changed();
    }

    partial void OnLowThresholdChanged(double value) => Changed();

    partial void OnCriticalThresholdChanged(double value) => Changed();

    partial void OnIncludeCaseChanged(bool value) => Changed();

    partial void OnSystemNotificationsChanged(bool value) => Changed();

    partial void OnCloseToTrayChanged(bool value) => Changed();

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_syncing || !StartupSupported || value == _startup.IsEnabled)
        {
            return;
        }

        try
        {
            _startup.SetEnabled(value);
        }
        catch (Exception e)
        {
            Show(Current with { StartWithWindows = !value });
            ErrorText = $"Couldn't change start with Windows: {e.Message}";
            SamaatLog.Warn("Settings", "Changing start with Windows failed", e);
            return;
        }

        Changed();
    }

    /// <summary>The share delay saved for an output; 0 when none is saved.</summary>
    public int GetShareDelay(string id) => _shareDelays.GetValueOrDefault(id);

    /// <summary>Saves the share delay for one output alongside the other settings.</summary>
    public void SetShareDelay(string id, int ms)
    {
        _shareDelays[id] = ms;
        Changed();
    }

    /// <summary>The artwork look saved for a device; null means automatic.</summary>
    public string? GetArtworkChoice(string id) => _artworkChoices.GetValueOrDefault(id);

    /// <summary>Saves the artwork look for a device; null (automatic) removes the entry.</summary>
    public void SetArtworkChoice(string id, string? choice)
    {
        if (choice is null)
        {
            _artworkChoices.Remove(id);
        }
        else
        {
            _artworkChoices[id] = choice;
        }

        Changed();
    }

    /// <summary>When the device was last seen connected; null if never (as far as Samaat knows).</summary>
    public DateTimeOffset? GetLastConnected(string id) => _lastConnected.TryGetValue(id, out var at) ? at : null;

    /// <summary>Remembers that these devices were connected at this time, for the "Recent" filter.</summary>
    public void MarkConnected(IEnumerable<string> ids, DateTimeOffset at)
    {
        foreach (var id in ids)
        {
            _lastConnected[id] = at;
        }

        Changed();
    }

    private void Changed()
    {
        if (_syncing)
        {
            return;
        }

        var settings = Current;
        Show(settings);
        _store.Save(settings);
        ErrorText = _store.LastError;
        Apply();
    }

    private void Show(AppSettings settings)
    {
        _syncing = true;
        try
        {
            Theme = settings.Theme;
            Accent = settings.Accent;
            _reduceMotionOverride = settings.ReduceMotion;
            ReduceMotion = _reduceMotionOverride ?? _motion.ReduceMotion;
            LowThreshold = settings.LowThreshold;
            CriticalThreshold = settings.CriticalThreshold;
            CriticalMax = settings.LowThreshold - 1;
            IncludeCase = settings.IncludeCase;
            SystemNotifications = settings.SystemNotifications;
            CloseToTray = settings.CloseToTray;
            StartWithWindows = settings.StartWithWindows;
            _shareDelays = new Dictionary<string, int>(settings.ShareDelays);
            _artworkChoices = new Dictionary<string, string>(settings.ArtworkChoices);
            _lastConnected = new Dictionary<string, DateTimeOffset>(settings.LastConnected);
            _version = settings.Version;
            Dashboard = settings.Dashboard;
            DeviceFilter = settings.DeviceFilter;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void Apply()
    {
        var settings = Current;
        _detector.Policy = _detector.Policy with
        {
            LowThreshold = settings.LowThreshold,
            CriticalThreshold = settings.CriticalThreshold,
            IncludeCase = settings.IncludeCase,
        };

        if (Application.Current is { } app)
        {
            AccentPalette.Apply(app, settings.Accent);
            app.RequestedThemeVariant = settings.Theme switch
            {
                ThemeChoice.Light => ThemeVariant.Light,
                ThemeChoice.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }
}

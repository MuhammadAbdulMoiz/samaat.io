using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Samaat.Core.Battery;
using Samaat.Core.Platform;
using Samaat.Core.Settings;

namespace Samaat.App.Features.Settings;

/// <summary>The Settings page: every change is normalized, saved and applied straight away.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly LowBatteryDetector _detector;
    private readonly IStartupRegistration _startup;
    private bool _syncing;

    public SettingsViewModel(SettingsStore store, LowBatteryDetector detector, IStartupRegistration startup)
    {
        _store = store;
        _detector = detector;
        _startup = startup;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorText { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>The settings as currently applied.</summary>
    public AppSettings Current => new AppSettings
    {
        Theme = Theme,
        LowThreshold = (int)LowThreshold,
        CriticalThreshold = (int)CriticalThreshold,
        IncludeCase = IncludeCase,
        SystemNotifications = SystemNotifications,
        CloseToTray = CloseToTray,
        StartWithWindows = StartWithWindows,
    }.Normalize();

    [RelayCommand]
    private void SetTheme(ThemeChoice theme) => Theme = theme;

    partial void OnThemeChanged(ThemeChoice value) => Changed();

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
            return;
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
            LowThreshold = settings.LowThreshold;
            CriticalThreshold = settings.CriticalThreshold;
            CriticalMax = settings.LowThreshold - 1;
            IncludeCase = settings.IncludeCase;
            SystemNotifications = settings.SystemNotifications;
            CloseToTray = settings.CloseToTray;
            StartWithWindows = settings.StartWithWindows;
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
            app.RequestedThemeVariant = settings.Theme switch
            {
                ThemeChoice.Light => ThemeVariant.Light,
                ThemeChoice.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }
}

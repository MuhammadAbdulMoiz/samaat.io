using Samaat.Core.Diagnostics;
using Samaat.App.Features.Overview;
using Samaat.App.Features.Phone;
using Samaat.App.Features.Settings;
using Samaat.App.Features.Sound;
using Samaat.Core.Audio;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Samaat.Core.Battery;
using Samaat.Core.Devices;
using Samaat.Core.Platform;
using Samaat.Core.Profiles;
using Samaat.Core.Settings;

namespace Samaat.App.Shell;

public enum AppSection
{
    Overview,
    Sound,
    Connections,
    Info,
    Details,
    Settings,
}

/// <summary>What the content area shows. Switching pages cross-fades.</summary>
public abstract record PageModel;

/// <summary>One page of one device.</summary>
public abstract record DevicePageModel(DeviceViewModel Device) : PageModel;

public sealed record OverviewPageModel(DeviceViewModel Device) : DevicePageModel(Device);

public sealed record SoundPageModel(DeviceViewModel Device) : DevicePageModel(Device);

public sealed record ConnectionsPageModel(DeviceViewModel Device) : DevicePageModel(Device);

public sealed record InfoPageModel(DeviceViewModel Device) : DevicePageModel(Device);

/// <summary>Bluetooth IDs, address and services (master's Info page is the diagnostics page).</summary>
public sealed record DetailsPageModel(DeviceViewModel Device) : DevicePageModel(Device);

public sealed record SettingsPageModel(SettingsViewModel Settings) : PageModel;

public sealed partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private const int MaxAlerts = 3;

    /// <summary>Toasts disappear on their own; the alert counter in the sidebar keeps the state.</summary>
    public static readonly TimeSpan ToastLifetime = TimeSpan.FromSeconds(8);

    private readonly List<ITimer> _toastTimers = [];

    private readonly DeviceRegistry _registry;
    private readonly TimeProvider _time;
    private readonly LowBatteryDetector _alerts = new();
    private readonly DispatcherTimer _ageTimer;

    private readonly DeviceProfileDb _profiles;
    private readonly INotifier _notifier;

    /// <param name="settings">Defaults to %APPDATA%/Samaat/settings.json; tests pass a temp-directory store.</param>
    /// <param name="profiles">Defaults to the devices/ folder next to the executable.</param>
    public MainWindowViewModel(
        AppOptions options,
        TimeProvider? time = null,
        SettingsStore? settings = null,
        IStartupRegistration? startup = null,
        DeviceProfileDb? profiles = null,
        INotifier? notifier = null,
        IMotionPreference? motion = null,
        Func<IAudioShare>? audioShareFactory = null,
        Samaat.Core.Platform.IDefaultOutputSwitcher? outputSwitcher = null,
        IReadOnlyList<IDeviceSource>? sources = null,
        Func<Func<IReadOnlyList<IEarbudDevice>>, Samaat.Handoff.PhoneLinkHost>? phoneLinkFactory = null)
    {
        _time = time ?? TimeProvider.System;
        _notifier = notifier ?? options.CreateNotifier();
        settings ??= new SettingsStore(SettingsStore.DefaultPath);
        _profiles = profiles ?? DeviceProfileDb.Load(Path.Combine(AppContext.BaseDirectory, "devices"));
        Settings = new SettingsViewModel(
            settings, _alerts, startup ?? options.CreateStartupRegistration(),
            motion ?? options.CreateMotionPreference());
        Settings.PropertyChanged += OnSettingsChanged;
        AudioShare = new AudioShareViewModel(audioShareFactory ?? options.CreateAudioShare, outputSwitcher ?? options.CreateDefaultOutputSwitcher(), Settings);
        // Paired phones live next to the settings, so tests with a temp settings file never see real pairings.
        // With the phone handoff switched off the link is never created: nothing pairs, listens or broadcasts.
        PhoneHandoff = options.PhoneHandoff;
        if (PhoneHandoff)
        {
            var phoneStore = Path.Combine(Path.GetDirectoryName(settings.Path) ?? ".", "phones.json");
            PhoneLink = new PhoneLinkViewModel(phoneLinkFactory ?? (devices => options.CreatePhoneLinkHost(phoneStore, devices)), onMessage: ShowMessage);
        }

        Settings.PhoneLink = PhoneLink;
        Settings.ShowPhoneComingSoon = !PhoneHandoff;
        Alerts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CurrentAlert));
            OnPropertyChanged(nameof(MoreAlertsText));
        };
        _registry = new DeviceRegistry(sources ?? options.CreateSources(_time));
        _registry.DevicesChanged += (_, _) => Dispatcher.UIThread.Post(SyncDevices);
        _registry.SourceFailed += (_, failure) => Dispatcher.UIThread.Post(() =>
            StatusText = $"{failure.Source.Name} failed: {failure.Error.Message}");

        if (options.UsesSimulator)
        {
            ModeText = options.HasRealBluetooth
                ? "Simulator enabled alongside real Bluetooth devices."
                : "Simulation mode: real Bluetooth is only implemented for Windows so far.";
        }

        _ageTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            foreach (var device in Devices)
            {
                device.UpdateAges();
            }
        });
        _ageTimer.Start();
    }

    public SettingsViewModel Settings { get; }

    public AudioShareViewModel AudioShare { get; }

    /// <summary>Pairing with phones and moving earbuds to them; null when <see cref="PhoneHandoff"/> is off.</summary>
    public PhoneLinkViewModel? PhoneLink { get; }

    /// <summary>The phone-handoff feature flag, from <see cref="AppOptions.PhoneHandoff"/>.</summary>
    public bool PhoneHandoff { get; }

    public ObservableCollection<DeviceViewModel> Devices { get; } = [];

    /// <summary>Queued toasts, newest first. Only the newest shows, so stacked toasts never cover the page's own buttons.</summary>
    public ObservableCollection<string> Alerts { get; } = [];

    /// <summary>The toast on screen, or null when there is none.</summary>
    public string? CurrentAlert => Alerts.Count > 0 ? Alerts[0] : null;

    /// <summary>"+2 more" under the toast when others are queued behind it.</summary>
    public string? MoreAlertsText => Alerts.Count > 1 ? $"+{Alerts.Count - 1} more" : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial DeviceViewModel? SelectedDevice { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPage), nameof(DevicePage), nameof(ShowDevicePage), nameof(ShowStudio), nameof(ShowFocus), nameof(ShowFocusHero), nameof(ShowFocusPage))]
    public partial PageModel? CurrentPage { get; set; }

    /// <summary>How many devices currently have a low or critical battery.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlertingDevices), nameof(AlertCountText))]
    public partial int AlertingDeviceCount { get; set; }

    public bool HasAlertingDevices => AlertingDeviceCount > 0;

    public string AlertCountText => AlertingDeviceCount == 1 ? "1 low battery" : $"{AlertingDeviceCount} low batteries";

    partial void OnSelectedDeviceChanged(DeviceViewModel? value)
    {
        if (_pinned is not null && !ReferenceEquals(value, _pinned))
        {
            _pinned = null;
            UpdateVisibleDevices();
        }

        UpdatePage();
        UpdateCurrent();
    }

    partial void OnSectionChanged(AppSection value)
    {
        UpdatePage();
        UpdateCurrent();
    }

    /// <summary>Only the sidebar row of the device being shown is highlighted; in Settings none is.</summary>
    private void UpdateCurrent()
    {
        foreach (var device in Devices)
        {
            device.IsCurrent = ReferenceEquals(device, SelectedDevice) && !IsSettings;
        }
    }

    private void UpdatePage() => CurrentPage = Section == AppSection.Settings ? new SettingsPageModel(Settings)
        : SelectedDevice is not { } device ? null
        : Section switch
        {
            AppSection.Sound => new SoundPageModel(device),
            AppSection.Connections => new ConnectionsPageModel(device),
            AppSection.Info => new InfoPageModel(device),
            AppSection.Details => new DetailsPageModel(device),
            _ => new OverviewPageModel(device),
        };

    [RelayCommand]
    private void ShowAlertingDevice()
    {
        if (Devices.FirstOrDefault(d => d.HasAlert) is { } device)
        {
            SelectedDevice = device;
            if (!VisibleDevices.Contains(device))
            {
                _pinned = device;
                UpdateVisibleDevices();
            }

            Section = AppSection.Overview;
        }
    }

    private void UpdateAlertCount() => AlertingDeviceCount = Devices.Count(d => d.HasAlert);

    private void OnDevicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceViewModel.HasAlert))
        {
            UpdateAlertCount();
        }
        else if (e.PropertyName == nameof(DeviceViewModel.IsConnected) && sender is DeviceViewModel device)
        {
            OnDeviceConnectionChanged(device);
        }
    }

    public bool HasSelection => SelectedDevice is not null;

    public bool HasPage => CurrentPage is not null;

    /// <summary>The page of the selected device; null in Settings and when there is no device.</summary>
    public DevicePageModel? DevicePage => CurrentPage as DevicePageModel;

    public bool ShowDevicePage => DevicePage is not null;

    /// <summary>True when the sidebar is the 68 px icon rail: below 900 px wide, or when the user collapsed it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarToggleText))]
    public partial bool IsSidebarCompact { get; set; }

    private bool _windowIsNarrow;
    private bool? _sidebarChoice;

    public string SidebarToggleText => IsSidebarCompact ? "Expand sidebar" : "Collapse sidebar";

    /// <summary>Called by the window on resize. Crossing the 900 px line hands control back to the width.</summary>
    public void SetWindowNarrow(bool narrow)
    {
        if (narrow != _windowIsNarrow)
        {
            _sidebarChoice = null;
        }

        _windowIsNarrow = narrow;
        UpdateSidebar();
    }

    // Glance lists every device itself, so the sidebar is the icon rail there unless the user expands it.
    private void UpdateSidebar() => IsSidebarCompact = _sidebarChoice ?? (_windowIsNarrow || IsGlance);

    [RelayCommand]
    private void ToggleSidebar()
    {
        _sidebarChoice = !IsSidebarCompact;
        IsSidebarCompact = _sidebarChoice.Value;
    }

    [ObservableProperty]
    public partial string? ModeText { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Looking for devices…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState), nameof(ShowFilterEmpty))]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverview), nameof(IsSound), nameof(IsConnections), nameof(IsInfo), nameof(IsDetails), nameof(IsSettings), nameof(ShowEmptyState),
        nameof(ShowFocusHero), nameof(ShowFocusPage), nameof(ShowGlance), nameof(ShowFilterEmpty))]
    public partial AppSection Section { get; set; }

    public bool IsOverview => Section == AppSection.Overview;

    public bool IsSound => Section == AppSection.Sound;

    public bool IsConnections => Section == AppSection.Connections;

    public bool IsInfo => Section == AppSection.Info;

    public bool IsDetails => Section == AppSection.Details;

    public bool IsSettings => Section == AppSection.Settings;

    /// <summary>Settings has no device, so the "no earbuds" message must not cover it.</summary>
    public bool ShowEmptyState => IsEmpty && !IsSettings;

    [RelayCommand]
    private void Navigate(AppSection section) => Section = section;

    [RelayCommand]
    private void SelectDevice(DeviceViewModel device)
    {
        SelectedDevice = device;
        if (IsSettings)
        {
            Section = AppSection.Overview;
        }
    }

    [RelayCommand]
    public Task RefreshDevicesAsync() => _registry.RefreshAsync();

    [RelayCommand]
    private void DismissAlerts() => Alerts.Clear();

    [RelayCommand]
    private void DismissAlert(string alert) => Alerts.Remove(alert);

    private void SyncDevices()
    {
        var current = _registry.Devices;
        var selectedId = SelectedDevice?.Device.Identity.Id;
        foreach (var stale in Devices.Where(vm => !current.Contains(vm.Device)).ToList())
        {
            stale.PropertyChanged -= OnDevicePropertyChanged;
            stale.Dispose();
            Devices.Remove(stale);
        }

        var added = new List<DeviceViewModel>();
        foreach (var device in current.Where(d => Devices.All(vm => vm.Device != d)))
        {
            var vm = new DeviceViewModel(device, _alerts, _time, OnAlert, _profiles.Match(device.Identity), _profiles.Profiles, Settings, ShowMessage);
            vm.Share = AudioShare;
            vm.PhoneMove = PhoneLink?.CreateMove(device, () => Section = AppSection.Settings);
            vm.ShowPhoneComingSoon = !PhoneHandoff;
            vm.PropertyChanged += OnDevicePropertyChanged;
            Devices.Add(vm);
            added.Add(vm);
        }

        MarkConnectedNow(added);
        if (SelectedDevice is null || !Devices.Contains(SelectedDevice))
        {
            SelectedDevice = Devices.FirstOrDefault(d => d.Device.Identity.Id == selectedId && IsListed(d)) ?? Devices.FirstOrDefault(IsListed);
        }

        UpdateVisibleDevices();
        PhoneLink?.SetDevices(current);

        UpdateAlertCount();
        UpdateCurrent();
        IsEmpty = Devices.Count == 0;
        StatusText = IsEmpty
            ? "No paired Bluetooth audio devices found. Pair your earbuds in system settings, then refresh."
            : $"{Devices.Count} device{(Devices.Count == 1 ? "" : "s")}";
    }

    // Called during DeviceViewModel construction too, before it is in Devices, so the VM is passed in.
    private void OnAlert(DeviceViewModel device, BatteryAlert alert)
    {
        var part = alert.Component switch
        {
            BatteryComponent.Left => "left bud",
            BatteryComponent.Right => "right bud",
            BatteryComponent.Case => "case",
            _ => "battery",
        };
        var level = alert.Level == BatteryAlertLevel.Critical ? "critically low" : "low";

        var text = $"{device.Name}: {part} {level} ({alert.Percent}%)";
        if (Settings.SystemNotifications)
        {
            _notifier.Show("Samaat", text);
        }

        PushToast(text);
    }

    /// <summary>A short message from anywhere in the app (a failed connect, say), shown on the toast list.</summary>
    public void ShowMessage(string text) => PushToast(text);

    private void PushToast(string text)
    {
        Alerts.Insert(0, text);
        ITimer? timer = null;
        timer = _time.CreateTimer(_ => Dispatcher.UIThread.Post(() =>
        {
            Alerts.Remove(text);
            _toastTimers.Remove(timer!);
            timer!.Dispose();
        }), null, ToastLifetime, Timeout.InfiniteTimeSpan);
        _toastTimers.Add(timer);
        while (Alerts.Count > MaxAlerts)
        {
            Alerts.RemoveAt(Alerts.Count - 1);
        }
    }

    private int _uiDisposed, _servicesDisposed;

    public async ValueTask DisposeAsync()
    {
        DisposeUiParts();
        await DisposeServicesAsync();
    }

    /// <summary>
    /// Exit path: the dispatcher is blocked here, so the async disposals run on the pool and are waited for at most
    /// <paramref name="timeout"/>. Never throws; a second call does nothing.
    /// </summary>
    public void Shutdown(TimeSpan timeout)
    {
        DisposeUiParts();
        try
        {
            if (!Task.Run(() => DisposeServicesAsync().AsTask()).Wait(timeout))
            {
                SamaatLog.Warn("App", $"Shutdown didn't finish within {timeout.TotalSeconds:0.#}s; exiting anyway");
            }
        }
        catch (Exception e)
        {
            SamaatLog.Warn("App", "Shutdown failed", e);
        }
    }

    // The UI-affine part: timers and the per-device view models.
    private void DisposeUiParts()
    {
        if (Interlocked.Exchange(ref _uiDisposed, 1) == 1)
        {
            return;
        }

        _ageTimer.Stop();
        MarkConnectedNow(Devices);
        foreach (var timer in _toastTimers)
        {
            timer.Dispose();
        }

        _toastTimers.Clear();
        foreach (var device in Devices)
        {
            device.Dispose();
        }
    }

    // The async part; it must not need the UI thread, because Shutdown runs it while the dispatcher is blocked.
    private async ValueTask DisposeServicesAsync()
    {
        if (Interlocked.Exchange(ref _servicesDisposed, 1) == 1)
        {
            return;
        }

        await AudioShare.DisposeAsync();
        if (PhoneLink is not null)
        {
            await PhoneLink.DisposeAsync();
        }

        await _registry.DisposeAsync();
    }
}

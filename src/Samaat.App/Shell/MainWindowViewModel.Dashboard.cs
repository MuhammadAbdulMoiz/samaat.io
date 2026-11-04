using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Samaat.App.Features.Overview;
using Samaat.App.Features.Settings;
using Samaat.Core.Settings;

namespace Samaat.App.Shell;

// The dashboard layout (Studio, Focus, Glance) and the device filter (Connected, Recent, All paired).
public sealed partial class MainWindowViewModel
{
    // A selected device that disconnects stays listed until the selection or the filter changes,
    // so the page doesn't vanish under the user's cursor after they press Disconnect.
    private DeviceViewModel? _pinned;

    /// <summary>The devices the sidebar and Glance list: the filter's matches, in discovery order.</summary>
    public ObservableCollection<DeviceViewModel> VisibleDevices { get; } = [];

    public DashboardView Dashboard => Settings.Dashboard;

    public bool IsStudio => Dashboard == DashboardView.Studio;

    public bool IsFocus => Dashboard == DashboardView.Focus;

    public bool IsGlance => Dashboard == DashboardView.Glance;

    public string LayoutButtonName => $"Layout: {DashboardNames.Name(Dashboard)}";

    public string DeviceListTitle => DashboardNames.ListTitle(Settings.DeviceFilter);

    public string DeviceFilterName => DashboardNames.FilterName(Settings.DeviceFilter);

    public string DeviceFilterButtonName => $"Show devices: {DeviceFilterName}";

    public bool HasVisibleDevices => VisibleDevices.Count > 0;

    /// <summary>Devices exist, but the filter hides all of them.</summary>
    public bool ShowFilterEmpty => !IsEmpty && !HasVisibleDevices && !IsSettings;

    public string FilterEmptyTitle => DashboardNames.EmptyListText(Settings.DeviceFilter);

    public string FilterEmptyDetail => $"{Devices.Count} paired device{(Devices.Count == 1 ? " is" : "s are")} hidden. "
        + "Connect your earbuds in Windows, or show all paired devices.";

    /// <summary>Studio's rail and tabs: a device page in the Studio layout.</summary>
    public bool ShowStudio => IsStudio && DevicePage is not null;

    /// <summary>Focus: the selected device's hero, or one of its pages with a Back button.</summary>
    public bool ShowFocus => IsFocus && DevicePage is not null;

    public bool ShowFocusHero => ShowFocus && IsOverview;

    public bool ShowFocusPage => ShowFocus && !IsOverview;

    public bool ShowGlance => IsGlance && !IsSettings && HasVisibleDevices;

    [RelayCommand]
    private void SetDashboard(DashboardView view) => Settings.Dashboard = view;

    [RelayCommand]
    private void SetDeviceFilter(DeviceFilter filter) => Settings.DeviceFilter = filter;

    /// <summary>From Glance: show this device with every control (the Studio layout).</summary>
    [RelayCommand]
    private void OpenDevice(DeviceViewModel device)
    {
        SelectedDevice = device;
        Section = AppSection.Overview;
        Settings.Dashboard = DashboardView.Studio;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SettingsViewModel.Dashboard):
                _sidebarChoice = null;
                UpdateSidebar();
                OnPropertyChanged(nameof(Dashboard));
                OnPropertyChanged(nameof(IsStudio));
                OnPropertyChanged(nameof(IsFocus));
                OnPropertyChanged(nameof(IsGlance));
                OnPropertyChanged(nameof(LayoutButtonName));
                RaiseViewFlags();
                break;
            case nameof(SettingsViewModel.DeviceFilter):
                _pinned = null;
                OnPropertyChanged(nameof(DeviceListTitle));
                OnPropertyChanged(nameof(DeviceFilterName));
                OnPropertyChanged(nameof(DeviceFilterButtonName));
                OnPropertyChanged(nameof(FilterEmptyTitle));
                UpdateVisibleDevices();
                break;
        }
    }

    private void RaiseViewFlags()
    {
        OnPropertyChanged(nameof(ShowStudio));
        OnPropertyChanged(nameof(ShowFocus));
        OnPropertyChanged(nameof(ShowFocusHero));
        OnPropertyChanged(nameof(ShowFocusPage));
        OnPropertyChanged(nameof(ShowGlance));
        OnPropertyChanged(nameof(ShowFilterEmpty));
    }

    private bool IsListed(DeviceViewModel device) =>
        ReferenceEquals(device, _pinned)
        || DeviceFilters.Includes(Settings.DeviceFilter, device.IsConnected, Settings.GetLastConnected(device.Device.Identity.Id), _time.GetUtcNow());

    /// <summary>Brings <see cref="VisibleDevices"/> in line with the filter in place, so rows that stay keep their focus.</summary>
    private void UpdateVisibleDevices()
    {
        var wanted = Devices.Where(IsListed).ToList();
        for (var i = VisibleDevices.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(VisibleDevices[i]))
            {
                VisibleDevices.RemoveAt(i);
            }
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            if (i >= VisibleDevices.Count || !ReferenceEquals(VisibleDevices[i], wanted[i]))
            {
                VisibleDevices.Remove(wanted[i]);
                VisibleDevices.Insert(i, wanted[i]);
            }
        }

        if (SelectedDevice is null || !VisibleDevices.Contains(SelectedDevice))
        {
            SelectedDevice = VisibleDevices.FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasVisibleDevices));
        OnPropertyChanged(nameof(FilterEmptyDetail));
        RaiseViewFlags();
    }

    private void OnDeviceConnectionChanged(DeviceViewModel device)
    {
        // Disconnecting counts as "last connected now" too, so Recent measures from the end of the session.
        Settings.MarkConnected([device.Device.Identity.Id], _time.GetUtcNow());
        if (!device.IsConnected && ReferenceEquals(device, SelectedDevice))
        {
            _pinned = device;
        }

        UpdateVisibleDevices();
    }

    private void MarkConnectedNow(IEnumerable<DeviceViewModel> devices)
    {
        var ids = devices.Where(d => d.IsConnected).Select(d => d.Device.Identity.Id).ToList();
        if (ids.Count > 0)
        {
            Settings.MarkConnected(ids, _time.GetUtcNow());
        }
    }
}

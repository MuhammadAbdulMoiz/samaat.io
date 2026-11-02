using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Platform;
using Samaat.App.Features.Overview;
using Samaat.App.Features.Settings;
using Samaat.App.Shell;

namespace Samaat.App.Tray;

/// <summary>The notification-area icon: battery summary tooltip, per-device menu, open/refresh/quit. UI thread only.</summary>
public sealed class TrayController : IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly MainWindow _window;
    private readonly MainWindowViewModel _viewModel;
    private readonly TrayIcon _tray;

    public TrayController(
        Application application,
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow window,
        MainWindowViewModel viewModel,
        WindowIcon icon)
    {
        _desktop = desktop;
        _window = window;
        _viewModel = viewModel;
        _tray = new TrayIcon { Icon = icon, IsVisible = true };
        _tray.Clicked += (_, _) => Open();
        TrayIcon.SetIcons(application, [_tray]);

        viewModel.Devices.CollectionChanged += OnDevicesChanged;
        foreach (var device in viewModel.Devices)
        {
            device.PropertyChanged += OnDeviceChanged;
        }

        Rebuild();
    }

    /// <summary>Set before a deliberate quit so the window's Closing handler lets it through.</summary>
    public bool Quitting { get; private set; }

    public void Open()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    public void Quit()
    {
        Quitting = true;
        _desktop.Shutdown();
    }

    private void OnDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (DeviceViewModel device in e.OldItems ?? Array.Empty<DeviceViewModel>())
        {
            device.PropertyChanged -= OnDeviceChanged;
        }

        foreach (DeviceViewModel device in e.NewItems ?? Array.Empty<DeviceViewModel>())
        {
            device.PropertyChanged += OnDeviceChanged;
        }

        Rebuild();
    }

    private void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DeviceViewModel.SummaryText) or nameof(DeviceViewModel.IsConnected) or nameof(DeviceViewModel.Name))
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        var devices = _viewModel.Devices.Select(d => new TrayDevice(d.Name, d.SummaryText, d.IsConnected)).ToList();
        _tray.ToolTipText = TraySummary.Text(devices);

        var menu = new NativeMenu();
        foreach (var device in devices)
        {
            menu.Add(new NativeMenuItem(TraySummary.MenuLine(device)) { IsEnabled = false });
        }

        if (devices.Count > 0)
        {
            menu.Add(new NativeMenuItemSeparator());
        }

        menu.Add(Item("Open Samaat", Open));
        menu.Add(Item("Refresh devices", () => _ = _viewModel.RefreshDevicesAsync()));
        menu.Add(Item("Quit", Quit));
        _tray.Menu = menu;
    }

    private static NativeMenuItem Item(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }

    public void Dispose()
    {
        _viewModel.Devices.CollectionChanged -= OnDevicesChanged;
        foreach (var device in _viewModel.Devices)
        {
            device.PropertyChanged -= OnDeviceChanged;
        }

        _tray.IsVisible = false;
        _tray.Dispose();
    }
}

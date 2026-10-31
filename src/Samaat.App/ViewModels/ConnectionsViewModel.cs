using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Samaat.Core.Commands;
using Samaat.Core.Connections;
using Samaat.Core.Devices;

namespace Samaat.App.ViewModels;

/// <summary>Which hosts the earbuds are connected to, and switching between them where supported.</summary>
public sealed partial class ConnectionsViewModel : ObservableObject, IDisposable
{
    private readonly IEarbudDevice _device;
    private readonly IMultipointFeature? _feature;
    private readonly Action<Action> _post;
    private readonly TimeProvider _time;
    private bool _applying;

    public ConnectionsViewModel(IEarbudDevice device, Action<Action>? post = null, TimeProvider? time = null)
    {
        _device = device;
        _feature = device.GetFeature<IMultipointFeature>();
        _post = post ?? (a => Dispatcher.UIThread.Post(a));
        _time = time ?? TimeProvider.System;

        if (_feature is not null)
        {
            _feature.Changed += OnFeatureChanged;
        }

        _device.ConnectionChanged += OnConnectionChanged;
        ApplyState();
    }

    public bool HasMultipoint => _feature is not null;

    public bool CanToggleMultipoint => _feature?.Options.CanToggleMultipoint == true;

    public string LimitText => _feature is null ? "" : $"Up to {_feature.Options.MaxConnections} devices at once";

    public string UnavailableText =>
        "Your earbuds may also be connected to other devices, like your phone. Samaat can show and switch them only for models whose protocol it supports.";

    public ObservableCollection<HostViewModel> Hosts { get; } = [];

    public Task PendingCommand { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial bool MultipointEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial string ThisComputerText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorText { get; set; }

    public bool HasError => ErrorText is not null;

    partial void OnMultipointEnabledChanged(bool value)
    {
        if (!_applying && _feature is not null)
        {
            PendingCommand = RunAsync(ct => _feature.SetMultipointEnabledAsync(value, ct));
        }
    }

    private Task SwitchAsync(string hostId) =>
        _feature is null ? Task.CompletedTask : PendingCommand = RunAsync(ct => _feature.SwitchToAsync(hostId, ct));

    private Task DisconnectAsync(string hostId) =>
        _feature is null ? Task.CompletedTask : PendingCommand = RunAsync(ct => _feature.DisconnectAsync(hostId, ct));

    private async Task RunAsync(Func<CancellationToken, Task<CommandResult>> command)
    {
        IsBusy = true;
        ErrorText = null;
        var result = await CommandRunner.RunAsync(command, CommandRunner.DefaultTimeout, _time);
        IsBusy = false;
        if (!result.Succeeded)
        {
            ErrorText = result.UserMessage;
        }

        ApplyState();
    }

    private void OnFeatureChanged(object? sender, MultipointState state) => _post(ApplyState);

    private void OnConnectionChanged(object? sender, EventArgs e) => _post(ApplyState);

    private void ApplyState()
    {
        _applying = true;
        try
        {
            IsConnected = _device.Connection == ConnectionState.Connected;
            ThisComputerText = IsConnected ? "Connected to this computer" : "Not connected to this computer";
            if (_feature is null)
            {
                return;
            }

            var state = _feature.Current;
            MultipointEnabled = state.MultipointEnabled;
            Hosts.Clear();
            foreach (var host in state.Hosts.OrderBy(h => h.State).ThenByDescending(h => h.IsThisHost))
            {
                Hosts.Add(new HostViewModel(host, _feature.Options, IsConnected, SwitchAsync, DisconnectAsync));
            }
        }
        finally
        {
            _applying = false;
        }
    }

    public void Dispose()
    {
        _device.ConnectionChanged -= OnConnectionChanged;
        if (_feature is not null)
        {
            _feature.Changed -= OnFeatureChanged;
        }
    }
}

public sealed class HostViewModel
{
    public HostViewModel(HostLink host, MultipointOptions options, bool earbudsReachable, Func<string, Task> switchTo, Func<string, Task> disconnect)
    {
        Id = host.Id;
        Name = host.Name;
        IsThisHost = host.IsThisHost;
        IsActive = host.State == HostLinkState.Active;
        IsConnected = host.IsConnected;
        CanSwitch = earbudsReachable && options.CanSwitch && host.State is HostLinkState.Connected or HostLinkState.Paired;
        CanDisconnect = earbudsReachable && options.CanDisconnect && host.IsConnected && !host.IsThisHost;
        SwitchText = host.State == HostLinkState.Paired ? "Connect" : "Switch audio here";
        StateText = host.State switch
        {
            HostLinkState.Active => "Playing audio",
            HostLinkState.Connected => "Connected · standby",
            HostLinkState.Paired => "Not connected",
            _ => "Out of range",
        };
        IconKey = host.Kind switch
        {
            HostKind.Phone => "Icon.Phone",
            HostKind.Tablet => "Icon.Tablet",
            HostKind.Computer => "Icon.Laptop",
            _ => "Icon.Bluetooth",
        };
        SwitchCommand = new AsyncRelayCommand(() => switchTo(Id));
        DisconnectCommand = new AsyncRelayCommand(() => disconnect(Id));
    }

    public string Id { get; }

    public string Name { get; }

    public bool IsThisHost { get; }

    public bool IsActive { get; }

    public bool IsConnected { get; }

    public bool CanSwitch { get; }

    public bool CanDisconnect { get; }

    public string SwitchText { get; }

    public string StateText { get; }

    public string IconKey { get; }

    public IAsyncRelayCommand SwitchCommand { get; }

    public IAsyncRelayCommand DisconnectCommand { get; }
}

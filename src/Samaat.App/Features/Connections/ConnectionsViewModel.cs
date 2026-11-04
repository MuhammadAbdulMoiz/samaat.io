using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Samaat.Core.Commands;
using Samaat.Core.Connections;
using Samaat.Core.Devices;

namespace Samaat.App.Features.Connections;

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

    /// <summary>Why the multipoint switch is greyed out; null when it can be changed.</summary>
    public string? MultipointDisabledReason => CanToggleMultipoint ? null : "These earbuds don't let Samaat change this";

    public string UnavailableTitle => "Other devices aren't shown for these earbuds";

    public string UnavailableText =>
        "Your phone or other devices may still be connected. Manage them on each device.";

    /// <summary>The limits we always state (docs/PLAN.md §8), behind the "How links work" disclosure.</summary>
    public string HelpText =>
        "Samaat can list and switch other devices only when your earbuds' own protocol allows it. "
        + "To forget a device completely, remove the pairing in that device's Bluetooth settings. "
        + "While this computer is disconnected, Samaat can't read or change the earbuds.";

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

    /// <summary>"2 of 2": hosts connected now, out of how many the earbuds allow in the current mode.</summary>
    [ObservableProperty]
    public partial string ConnectedSummary { get; set; } = "";

    /// <summary>Why the host actions are hidden; null while this computer is connected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHostsNote))]
    public partial string? HostsNote { get; set; }

    public bool HasHostsNote => HostsNote is not null;

    /// <summary>Multipoint is supported but the earbuds report no hosts at all.</summary>
    [ObservableProperty]
    public partial bool ShowNoHosts { get; set; }

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
            HostsNote = IsConnected ? null : "Connect this computer to switch or disconnect other devices.";
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

            var allowed = state.MultipointEnabled ? _feature.Options.MaxConnections : 1;
            ShowNoHosts = Hosts.Count == 0;
            ConnectedSummary = $"{state.Hosts.Count(h => h.IsConnected)} of {allowed}";
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
        SwitchText = host.State == HostLinkState.Paired ? "Connect" : "Switch here";
        SwitchIconKey = host.State == HostLinkState.Paired ? "Icon.Link" : "Icon.Switch";
        SwitchLabel = host.State == HostLinkState.Paired ? $"Connect {Name}" : $"Switch audio to {Name}";
        DisconnectLabel = $"Disconnect {Name}";
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

    public string SwitchIconKey { get; }

    /// <summary>Accessible name for the switch or connect button, naming the host.</summary>
    public string SwitchLabel { get; }

    public string DisconnectLabel { get; }

    public string StateText { get; }

    public string IconKey { get; }

    public IAsyncRelayCommand SwitchCommand { get; }

    public IAsyncRelayCommand DisconnectCommand { get; }
}

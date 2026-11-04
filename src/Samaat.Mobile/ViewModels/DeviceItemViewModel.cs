using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Samaat.Core.Battery;
using Samaat.Core.Commands;
using Samaat.Core.Devices;
using Samaat.Core.Equalizer;
using Samaat.Core.NoiseControl;
using Samaat.Handoff;
using Samaat.Handoff.Protocol;
using Samaat.Mobile.Runtime;

namespace Samaat.Mobile.ViewModels;

/// <summary>One pair of earbuds on the phone: the list row and the device page.</summary>
public sealed partial class DeviceItemViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PhoneLinkTimeout = TimeSpan.FromSeconds(60);

    private readonly MobileRuntime _runtime;
    private readonly Action<Action> _post;
    private readonly Action<string> _onMessage;
    private readonly Action _openComputer;
    private readonly IBatteryFeature? _battery;
    private readonly INoiseControlFeature? _noise;
    private readonly IEqualizerFeature? _eq;

    public DeviceItemViewModel(IEarbudDevice device, MobileRuntime runtime, Action<Action> post, Action<string> onMessage, Action openComputer)
    {
        Device = device;
        _runtime = runtime;
        _post = post;
        _onMessage = onMessage;
        _openComputer = openComputer;
        Address = device.Identity.Details?.Address is { } a ? MessageCodec.NormalizeAddress(a) : null;
        _battery = device.GetFeature<IBatteryFeature>();
        _noise = device.GetFeature<INoiseControlFeature>();
        _eq = device.GetFeature<IEqualizerFeature>();

        device.ConnectionChanged += OnChanged;
        _runtime.Computer.Changed += OnChanged;
        _runtime.Computer.Progress += OnProgress;
        if (_battery is not null)
        {
            _battery.Changed += OnBatteryChanged;
        }

        if (_noise is not null)
        {
            _noise.Changed += OnNoiseChanged;
        }

        if (_eq is not null)
        {
            _eq.Changed += OnEqChanged;
        }

        Refresh();
        RefreshBattery();
        RefreshNoise();
        RefreshEq();
    }

    public IEarbudDevice Device { get; }

    public string? Address { get; }

    public string Name => Device.Identity.Name;

    public string AccessibleText => $"{Name}, {ConnectionText}, battery {SummaryText}";

    // Connection on this phone

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConnect), nameof(ShowDisconnect), nameof(AccessibleText))]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial string ConnectionText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConnect), nameof(ShowDisconnect))]
    public partial bool IsLinkBusy { get; set; }

    public bool ShowConnect => Address is not null && !IsConnected && !IsLinkBusy;

    public bool ShowDisconnect => Address is not null && IsConnected && !IsLinkBusy;

    [RelayCommand]
    private Task ConnectAsync() => RunLinkAsync(true);

    [RelayCommand]
    private Task DisconnectAsync() => RunLinkAsync(false);

    private async Task RunLinkAsync(bool connect)
    {
        if (Address is null || IsLinkBusy)
        {
            return;
        }

        IsLinkBusy = true;
        ErrorText = null;
        var result = await CommandRunner.RunAsync(
            ct => connect ? _runtime.Audio.ConnectAsync(Address, PhoneLinkTimeout, ct) : _runtime.Audio.DisconnectAsync(Address, PhoneLinkTimeout, ct),
            PhoneLinkTimeout + TimeSpan.FromSeconds(2), _runtime.Time);
        IsLinkBusy = false;
        if (!result.Succeeded)
        {
            ErrorText = result.UserMessage;
        }

        Refresh();
    }

    // Battery

    public ObservableCollection<BatteryCellViewModel> Battery { get; } = [];

    public bool HasBattery => Battery.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleText))]
    public partial string SummaryText { get; set; } = "–";

    // Noise control

    public bool HasNoise => _noise is not null && _noise.Options.Modes.Count > 0;

    public ObservableCollection<OptionViewModel> NoiseModes { get; } = [];

    // EQ

    public bool HasEq => _eq is not null && _eq.Presets.Count > 0;

    public ObservableCollection<OptionViewModel> EqPresets { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorText { get; set; }

    public bool HasError => ErrorText is not null;

    // Moving to and from the computer

    [ObservableProperty]
    public partial string ComputerTitle { get; set; } = "Your computer";

    [ObservableProperty]
    public partial string ComputerStateText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsComputerOnline { get; set; }

    [ObservableProperty]
    public partial bool ShowPairComputer { get; set; }

    [ObservableProperty]
    public partial bool CanSendToComputer { get; set; }

    [ObservableProperty]
    public partial bool CanBringHere { get; set; }

    [ObservableProperty]
    public partial string SendText { get; set; } = "Move to computer";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    public partial string? ProgressText { get; set; }

    public bool ShowProgress => ProgressText is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSuccess), nameof(ShowFailure))]
    public partial string? ResultText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSuccess), nameof(ShowFailure))]
    public partial bool ResultIsError { get; set; }

    public bool ShowSuccess => ResultText is not null && !ResultIsError;

    public bool ShowFailure => ResultText is not null && ResultIsError;

    [ObservableProperty]
    public partial bool IsMoving { get; set; }

    public Task PendingMove { get; private set; } = Task.CompletedTask;

    [RelayCommand]
    private void PairComputer() => _openComputer();

    [RelayCommand]
    private Task SendToComputerAsync() => PendingMove = MoveAsync(HandoffDirection.ToComputer);

    [RelayCommand]
    private Task BringHereAsync() => PendingMove = MoveAsync(HandoffDirection.ToPhone);

    private async Task MoveAsync(HandoffDirection direction)
    {
        if (Address is null || IsMoving)
        {
            return;
        }

        IsMoving = true;
        ResultText = null;
        Refresh();
        var result = await _runtime.Computer.RequestMoveAsync(Address, direction);
        IsMoving = false;
        ProgressText = null;
        ResultText = result.Message;
        ResultIsError = !result.Succeeded;
        if (!result.Succeeded)
        {
            _onMessage(result.Message);
        }

        Refresh();
    }

    private void OnProgress(object? sender, HandoffProgressMessage progress)
    {
        if (MessageCodec.NormalizeAddress(progress.Address) != Address)
        {
            return;
        }

        _post(() =>
        {
            if (progress.Stage == HandoffStage.Done)
            {
                ProgressText = null;
                if (!IsMoving)
                {
                    // A move started on the computer.
                    ResultText = progress.Text;
                    ResultIsError = false;
                }
            }
            else
            {
                ProgressText = progress.Text;
            }
        });
    }

    private void OnChanged(object? sender, EventArgs e) => _post(Refresh);

    private void OnBatteryChanged(object? sender, BatteryState e) => _post(RefreshBattery);

    private void OnNoiseChanged(object? sender, NoiseControlState e) => _post(RefreshNoise);

    private void OnEqChanged(object? sender, int? e) => _post(RefreshEq);

    private void Refresh()
    {
        IsConnected = Device.Connection == ConnectionState.Connected;
        ConnectionText = Device.Connection switch
        {
            ConnectionState.Connected => "Connected to this phone",
            ConnectionState.Connecting => "Connecting…",
            _ => "Not connected to this phone",
        };

        var link = _runtime.Computer;
        var computer = link.Computer;
        var name = computer?.Name ?? "your computer";
        ComputerTitle = computer is null ? "Your computer" : computer.Name;
        SendText = $"Move to {name}";
        IsComputerOnline = link.State == ComputerLinkState.Online;
        ShowPairComputer = computer is null;
        var there = Address is null ? null : link.ComputerDevices.FirstOrDefault(d => d.Address == Address);
        ComputerStateText =
            computer is null ? "Pair Samaat with your computer to move these earbuds between it and this phone, even if they connect to one device at a time."
            : !IsComputerOnline ? $"Can't reach {name}. Samaat must be running there, on the same Wi-Fi."
            : Address is null ? "Samaat doesn't know these earbuds' Bluetooth address."
            : there is null ? $"{name} doesn't have these earbuds paired."
            : !there.CanConnect ? $"{name} can't connect these earbuds from Samaat."
            : there.Connected ? $"Playing on {name}"
            : IsConnected ? $"{name} can take these earbuds"
            : $"Not connected to {name} or this phone";
        var ready = IsComputerOnline && there is { CanConnect: true } && !IsMoving;
        CanSendToComputer = ready && !there!.Connected;
        CanBringHere = ready && !IsConnected;
    }

    private void RefreshBattery()
    {
        Battery.Clear();
        var state = _battery?.Current ?? BatteryState.Empty;
        foreach (var component in new[] { BatteryComponent.Left, BatteryComponent.Right, BatteryComponent.Case, BatteryComponent.Combined })
        {
            if (state.Get(component) is { } reading)
            {
                Battery.Add(new BatteryCellViewModel(reading));
            }
        }

        SummaryText = state.SummaryPercent is { } p ? $"{p}%" : "–";
        OnPropertyChanged(nameof(HasBattery));
    }

    private void RefreshNoise()
    {
        NoiseModes.Clear();
        if (_noise is null)
        {
            return;
        }

        foreach (var mode in NoiseControlPresentation.OrderedModes(_noise.Options))
        {
            NoiseModes.Add(new OptionViewModel(
                NoiseControlPresentation.Label(mode),
                NoiseControlPresentation.Description(mode),
                _noise.Current.Mode == mode,
                () => SetAsync(ct => _noise.SetModeAsync(mode, ct))));
        }
    }

    private void RefreshEq()
    {
        EqPresets.Clear();
        if (_eq is null)
        {
            return;
        }

        foreach (var preset in _eq.Presets)
        {
            EqPresets.Add(new OptionViewModel(preset.Name, null, _eq.CurrentPresetId == preset.Id, () => SetAsync(ct => _eq.SetPresetAsync(preset.Id, ct))));
        }
    }

    private async Task SetAsync(Func<CancellationToken, Task<CommandResult>> command)
    {
        ErrorText = null;
        var result = await CommandRunner.RunAsync(command, CommandRunner.DefaultTimeout, _runtime.Time);
        if (!result.Succeeded)
        {
            ErrorText = result.UserMessage;
        }

        RefreshNoise();
        RefreshEq();
    }

    public void Dispose()
    {
        Device.ConnectionChanged -= OnChanged;
        _runtime.Computer.Changed -= OnChanged;
        _runtime.Computer.Progress -= OnProgress;
        if (_battery is not null)
        {
            _battery.Changed -= OnBatteryChanged;
        }

        if (_noise is not null)
        {
            _noise.Changed -= OnNoiseChanged;
        }

        if (_eq is not null)
        {
            _eq.Changed -= OnEqChanged;
        }
    }
}

public sealed class BatteryCellViewModel(BatteryReading reading)
{
    public string Label { get; } = reading.Component switch
    {
        BatteryComponent.Left => "Left",
        BatteryComponent.Right => "Right",
        BatteryComponent.Case => "Case",
        _ => "Battery",
    };

    public double Percent { get; } = reading.Percent ?? 0;

    public string Text { get; } = reading.Percent is { } p ? $"{p}%" : "–";

    public bool IsCharging { get; } = reading.IsCharging;

    public string AccessibleText => $"{Label} {Text}{(IsCharging ? ", charging" : "")}";
}

/// <summary>A selectable choice: a noise mode or an EQ preset.</summary>
public sealed class OptionViewModel(string label, string? description, bool isSelected, Func<Task> select)
{
    public string Label { get; } = label;

    public string? Description { get; } = description;

    public bool HasDescription => Description is not null;

    public bool IsSelected { get; } = isSelected;

    public IAsyncRelayCommand SelectCommand { get; } = new AsyncRelayCommand(select);
}

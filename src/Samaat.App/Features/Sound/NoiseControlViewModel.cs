using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Core.NoiseControl;

namespace Samaat.App.Features.Sound;

/// <summary>
/// Noise control for one device. Changes are shown optimistically, sent to the device, and rolled
/// back to the device's reported state if it doesn't confirm them.
/// </summary>
public sealed partial class NoiseControlViewModel : ObservableObject, IDisposable
{
    private readonly IEarbudDevice _device;
    private readonly INoiseControlFeature? _feature;
    private readonly Action<Action> _post;
    private readonly TimeProvider _time;
    private readonly LatestValueSender<int>? _ancSender;
    private readonly LatestValueSender<int>? _ambientSender;
    private int _busy;
    private bool _applying;

    public NoiseControlViewModel(IEarbudDevice device, Action<Action>? post = null, TimeProvider? time = null)
    {
        _device = device;
        _feature = device.GetFeature<INoiseControlFeature>();
        _post = post ?? (a => Dispatcher.UIThread.Post(a));
        _time = time ?? TimeProvider.System;

        Layout = NoiseControlPresentation.LayoutFor(_feature?.Options);
        if (_feature is not null)
        {
            var options = _feature.Options;
            foreach (var mode in NoiseControlPresentation.OrderedModes(options))
            {
                Modes.Add(new NoiseModeOptionViewModel(mode, SelectAsync));
            }

            AncMin = options.AncLevel?.Min ?? 0;
            AncMax = options.AncLevel?.Max ?? 0;
            AmbientMin = options.AmbientLevel?.Min ?? 0;
            AmbientMax = options.AmbientLevel?.Max ?? 0;
            ShowWind = options.WindReduction;
            ShowConversation = options.ConversationAwareness;
            _ancSender = new LatestValueSender<int>((v, ct) => _feature.SetAncLevelAsync(v, ct));
            _ambientSender = new LatestValueSender<int>((v, ct) => _feature.SetAmbientLevelAsync(v, ct));
            _feature.Changed += OnFeatureChanged;
        }

        _device.ConnectionChanged += OnConnectionChanged;
        ApplyState();
    }

    public NoiseControlLayout Layout { get; }

    public bool IsSelector => Layout == NoiseControlLayout.Selector;

    public bool IsToggle => Layout == NoiseControlLayout.Toggle;

    public bool IsUnavailable => Layout == NoiseControlLayout.None;

    public bool HasExtras => ShowWind || ShowConversation;

    public string UnavailableTitle => DriverRetry.WaitsForConnection(_device)
        ? "Connect to use noise control"
        : "Not available for this model yet";

    public string UnavailableText => DriverRetry.WaitsForConnection(_device)
        ? "Connect your earbuds and Samaat will read noise control from them."
        : _feature is null
        ? "Samaat can't control noise cancelling on this device yet. That needs support for this model's own protocol."
        : "This model doesn't have noise control.";

    public ObservableCollection<NoiseModeOptionViewModel> Modes { get; } = [];

    public int AncMin { get; }

    public int AncMax { get; }

    public int AmbientMin { get; }

    public int AmbientMax { get; }

    public bool ShowWind { get; }

    public bool ShowConversation { get; }

    /// <summary>The last command, so callers (and tests) can await it.</summary>
    public Task PendingCommand { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedModeLabel))]
    public partial NoiseMode SelectedMode { get; set; }

    public string SelectedModeLabel => NoiseControlPresentation.Label(SelectedMode);

    [ObservableProperty]
    public partial bool IsAncOn { get; set; }

    [ObservableProperty]
    public partial double AncLevel { get; set; }

    [ObservableProperty]
    public partial double AmbientLevel { get; set; }

    [ObservableProperty]
    public partial bool WindReduction { get; set; }

    [ObservableProperty]
    public partial bool ConversationAwareness { get; set; }

    [ObservableProperty]
    public partial bool ShowAncLevel { get; set; }

    [ObservableProperty]
    public partial bool ShowAmbientLevel { get; set; }

    [ObservableProperty]
    public partial bool CanControl { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorText { get; set; }

    public bool HasError => ErrorText is not null;

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    partial void OnIsAncOnChanged(bool value)
    {
        if (!_applying)
        {
            _ = SelectAsync(value ? NoiseMode.NoiseCancelling : NoiseMode.Off);
        }
    }

    partial void OnAncLevelChanged(double value)
    {
        if (!_applying && _ancSender is not null)
        {
            PendingCommand = RunAsync(_ => _ancSender.SubmitAsync((int)Math.Round(value)));
        }
    }

    partial void OnAmbientLevelChanged(double value)
    {
        if (!_applying && _ambientSender is not null)
        {
            PendingCommand = RunAsync(_ => _ambientSender.SubmitAsync((int)Math.Round(value)));
        }
    }

    partial void OnWindReductionChanged(bool value)
    {
        if (!_applying && _feature is not null)
        {
            PendingCommand = RunAsync(ct => _feature.SetWindReductionAsync(value, ct));
        }
    }

    partial void OnConversationAwarenessChanged(bool value)
    {
        if (!_applying && _feature is not null)
        {
            PendingCommand = RunAsync(ct => _feature.SetConversationAwarenessAsync(value, ct));
        }
    }

    private Task SelectAsync(NoiseMode mode)
    {
        if (_feature is null)
        {
            return Task.CompletedTask;
        }

        // Optimistic: show the new mode now, roll back if the device doesn't confirm.
        Applying(() => ShowMode(mode));
        PendingCommand = RunAsync(ct => _feature.SetModeAsync(mode, ct));
        return PendingCommand;
    }

    private async Task RunAsync(Func<CancellationToken, Task<CommandResult>> command)
    {
        _busy++;
        IsBusy = true;
        ErrorText = null;
        UpdateStatus();

        var result = await CommandRunner.RunAsync(command, CommandRunner.DefaultTimeout, _time);

        _busy--;
        IsBusy = _busy > 0;
        if (!result.Succeeded)
        {
            ErrorText = result.UserMessage;
            ApplyState();
        }

        UpdateStatus();
    }

    private void OnFeatureChanged(object? sender, NoiseControlState state) => _post(ApplyState);

    private void OnConnectionChanged(object? sender, EventArgs e) => _post(ApplyState);

    /// <summary>Shows what the device last reported.</summary>
    private void ApplyState()
    {
        Applying(() =>
        {
            if (_feature is not null)
            {
                var state = _feature.Current;
                var options = _feature.Options;
                ShowMode(state.Mode);
                AncLevel = state.AncLevel ?? options.AncLevel?.Max ?? 0;
                AmbientLevel = state.AmbientLevel ?? options.AmbientLevel?.Min ?? 0;
                WindReduction = state.WindReduction ?? false;
                ConversationAwareness = state.ConversationAwareness ?? false;
            }

            CanControl = _device.Connection == ConnectionState.Connected && Layout != NoiseControlLayout.None
                && _feature?.Access != CapabilityAccess.ReadOnly;
        });
        OnPropertyChanged(nameof(UnavailableTitle));
        OnPropertyChanged(nameof(UnavailableText));
        UpdateStatus();
    }

    private void ShowMode(NoiseMode mode)
    {
        SelectedMode = mode;
        IsAncOn = mode == NoiseMode.NoiseCancelling;
        foreach (var option in Modes)
        {
            option.IsSelected = option.Mode == mode;
        }

        if (_feature is not null)
        {
            ShowAncLevel = NoiseControlPresentation.ShowsAncLevel(_feature.Options, mode);
            ShowAmbientLevel = NoiseControlPresentation.ShowsAmbientLevel(_feature.Options, mode);
        }
    }

    private void UpdateStatus()
    {
        StatusText = Layout == NoiseControlLayout.None ? ""
            : _device.Connection != ConnectionState.Connected ? "Connect your earbuds to change noise control."
            : IsBusy ? "Applying…"
            : SelectedMode == NoiseMode.Off ? "Noise control is off"
            : _feature?.Access == CapabilityAccess.ReadOnly ? $"{SelectedModeLabel} is on. Change it on your headphones."
            : $"{SelectedModeLabel} is on";
    }

    private void Applying(Action action)
    {
        _applying = true;
        try
        {
            action();
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

public sealed partial class NoiseModeOptionViewModel : ObservableObject
{
    public NoiseModeOptionViewModel(NoiseMode mode, Func<NoiseMode, Task> select)
    {
        Mode = mode;
        SelectCommand = new AsyncRelayCommand(() => select(mode));
    }

    public NoiseMode Mode { get; }

    public string Label => NoiseControlPresentation.Label(Mode);

    public string Description => NoiseControlPresentation.Description(Mode);

    public string IconKey => Mode switch
    {
        NoiseMode.NoiseCancelling => "Icon.NoiseCancelling",
        NoiseMode.Adaptive => "Icon.Adaptive",
        NoiseMode.Transparency => "Icon.Transparency",
        _ => "Icon.CircleOff",
    };

    public IAsyncRelayCommand SelectCommand { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

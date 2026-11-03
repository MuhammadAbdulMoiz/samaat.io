using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Samaat.Core.Commands;
using Samaat.Core.Devices;
using Samaat.Core.Equalizer;

namespace Samaat.App.Features.Sound;

/// <summary>
/// Device EQ presets for one device. Same flow as noise control: show the choice now, send it,
/// and roll back to the device's reported preset if it doesn't confirm.
/// </summary>
public sealed partial class EqualizerViewModel : ObservableObject, IDisposable
{
    private readonly IEarbudDevice _device;
    private readonly IEqualizerFeature? _feature;
    private readonly Action<Action> _post;
    private readonly TimeProvider _time;
    private int _busy;

    public EqualizerViewModel(IEarbudDevice device, Action<Action>? post = null, TimeProvider? time = null)
    {
        _device = device;
        _feature = device.GetFeature<IEqualizerFeature>();
        _post = post ?? (a => Dispatcher.UIThread.Post(a));
        _time = time ?? TimeProvider.System;

        if (_feature is not null)
        {
            foreach (var preset in _feature.Presets)
            {
                Presets.Add(new EqPresetOptionViewModel(preset, SelectAsync));
            }

            _feature.Changed += OnFeatureChanged;
        }

        _device.ConnectionChanged += OnConnectionChanged;
        ApplyState();
    }

    public bool IsUnavailable => _feature is null;

    public string UnavailableText => "Samaat can't change this device's EQ yet. That needs support for this model's own protocol.";

    public ObservableCollection<EqPresetOptionViewModel> Presets { get; } = [];

    /// <summary>The last command, so callers (and tests) can await it.</summary>
    public Task PendingCommand { get; private set; } = Task.CompletedTask;

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

    private Task SelectAsync(EqPreset preset)
    {
        if (_feature is null)
        {
            return Task.CompletedTask;
        }

        // Optimistic: show the new preset now, roll back if the device doesn't confirm.
        ShowPreset(preset.Id);
        PendingCommand = RunAsync(preset.Id);
        return PendingCommand;
    }

    private async Task RunAsync(int presetId)
    {
        _busy++;
        IsBusy = true;
        ErrorText = null;
        UpdateStatus();

        var result = await CommandRunner.RunAsync(ct => _feature!.SetPresetAsync(presetId, ct), CommandRunner.DefaultTimeout, _time);

        _busy--;
        IsBusy = _busy > 0;
        if (!result.Succeeded)
        {
            ErrorText = result.UserMessage;
            ApplyState();
        }

        UpdateStatus();
    }

    private void OnFeatureChanged(object? sender, int? presetId) => _post(ApplyState);

    private void OnConnectionChanged(object? sender, EventArgs e) => _post(ApplyState);

    /// <summary>Shows what the device last reported.</summary>
    private void ApplyState()
    {
        ShowPreset(_feature?.CurrentPresetId);
        CanControl = _device.Connection == ConnectionState.Connected && Presets.Count > 0;
        UpdateStatus();
    }

    private void ShowPreset(int? id)
    {
        foreach (var option in Presets)
        {
            option.IsSelected = option.Preset.Id == id;
        }
    }

    private void UpdateStatus()
    {
        StatusText = IsUnavailable ? ""
            : _device.Connection != ConnectionState.Connected ? "Connect your earbuds to change the EQ."
            : IsBusy ? "Applying…"
            : Presets.FirstOrDefault(p => p.IsSelected) is { } selected ? $"{selected.Label} preset"
            : "";
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

public sealed partial class EqPresetOptionViewModel : ObservableObject
{
    public EqPresetOptionViewModel(EqPreset preset, Func<EqPreset, Task> select)
    {
        Preset = preset;
        SelectCommand = new AsyncRelayCommand(() => select(preset));
    }

    public EqPreset Preset { get; }

    public string Label => Preset.Name;

    public IAsyncRelayCommand SelectCommand { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

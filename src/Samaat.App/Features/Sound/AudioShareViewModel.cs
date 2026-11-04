using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Samaat.App.Features.Settings;
using Samaat.Core.Audio;

namespace Samaat.App.Features.Sound;

/// <summary>The Share audio card: plays the default output on a second one. Choices are saved through the settings.</summary>
public sealed partial class AudioShareViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IAudioShare _share;
    private readonly SettingsViewModel _settings;
    private string? _savedTargetId;
    private bool _syncing;

    public AudioShareViewModel(IAudioShare share, SettingsViewModel settings)
    {
        _share = share;
        _settings = settings;
        _savedTargetId = settings.ShareTargetId;
        _syncing = true;
        DelayMs = settings.ShareDelayMs;
        _syncing = false;
        _share.DelayMs = DelayMs;
        Refresh();
        _share.Changed += OnChanged;
    }

    public string UnavailableText => "Sharing audio needs Windows.";

    public bool IsSupported => _share.IsSupported;

    [ObservableProperty]
    public partial IReadOnlyList<AudioOutput> Targets { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTargets), nameof(ShowNoTargets))]
    public partial AudioOutput? SelectedTarget { get; set; }

    public bool HasTargets => Targets.Count > 0;

    public bool ShowNoTargets => IsSupported && !HasTargets;

    [ObservableProperty]
    public partial bool IsSharing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DelayText))]
    public partial int DelayMs { get; set; }

    [ObservableProperty]
    public partial string? Status { get; set; }

    [ObservableProperty]
    public partial string HelpText { get; set; } = "";

    public string DelayText => $"Delay the copy · {DelayMs} ms";

    partial void OnDelayMsChanged(int value)
    {
        if (_syncing)
        {
            return;
        }

        _share.DelayMs = value;
        _settings.SetShare(_savedTargetId, value);
    }

    partial void OnSelectedTargetChanged(AudioOutput? value)
    {
        if (_syncing || value is null)
        {
            return;
        }

        _savedTargetId = value.Id;
        _settings.SetShare(value.Id, DelayMs);
        if (IsSharing)
        {
            _ = StartAsync(value.Id);
        }
    }

    partial void OnIsSharingChanged(bool value)
    {
        if (_syncing)
        {
            return;
        }

        if (!value)
        {
            _ = StopAsync();
        }
        else if (SelectedTarget is { } target)
        {
            _ = StartAsync(target.Id);
        }
        else
        {
            _syncing = true;
            IsSharing = false;
            _syncing = false;
        }
    }

    private async Task StopAsync()
    {
        try
        {
            await _share.StopAsync();
        }
        catch (Exception e)
        {
            Status = e.Message;
        }
    }

    private async Task StartAsync(string targetId)
    {
        try
        {
            await _share.StartAsync(targetId);
        }
        catch (Exception e)
        {
            Refresh();
            Status = e.Message;
            return;
        }

        Refresh();
    }

    private void OnChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Refresh);

    private void Refresh()
    {
        _syncing = true;
        try
        {
            var outputs = _share.Outputs;
            Targets = outputs.Where(o => !o.IsDefault).ToList();
            SelectedTarget = Targets.FirstOrDefault(t => t.Id == _savedTargetId) ?? Targets.FirstOrDefault();
            OnPropertyChanged(nameof(HasTargets));
            OnPropertyChanged(nameof(ShowNoTargets));
            HelpText = $"Plays everything from {outputs.FirstOrDefault(o => o.IsDefault)?.Name ?? "the default output"} on a second output too. " +
                "If the copy is ahead, add delay. If it's behind, make the other device the default and share to this one.";
            IsSharing = _share.State is AudioShareState.Sharing or AudioShareState.Starting;
            Status = _share.StatusText;
        }
        finally
        {
            _syncing = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _share.Changed -= OnChanged;
        await _share.DisposeAsync();
    }
}

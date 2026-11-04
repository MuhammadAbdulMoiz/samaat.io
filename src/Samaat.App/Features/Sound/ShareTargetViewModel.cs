using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Samaat.Core.Audio;

namespace Samaat.App.Features.Sound;

/// <summary>One output on the Share audio card. It owns its own share while switched on, and has its own delay.</summary>
public sealed partial class ShareTargetViewModel : ObservableObject, IAsyncDisposable
{
    private readonly AudioShareViewModel _parent;
    private readonly Func<IAudioShare> _createShare;
    private IAudioShare? _share;
    private Action? _detach;
    private bool _syncing;
    private bool _disposed;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _version;

    internal ShareTargetViewModel(AudioShareViewModel parent, string id, string name, Func<IAudioShare> createShare, int delayMs)
    {
        _parent = parent;
        Id = id;
        Name = name;
        _createShare = createShare;
        _syncing = true;
        DelayMs = delayMs;
        _syncing = false;
        SwitchHereCommand = new RelayCommand(() => _parent.SwitchTo(Id));
    }

    public string Id { get; }

    public string Name { get; }

    public IRelayCommand SwitchHereCommand { get; }

    [ObservableProperty]
    public partial bool IsSharing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DelayText))]
    public partial int DelayMs { get; set; }

    [ObservableProperty]
    public partial string? Status { get; set; }

    public string DelayText => $"Delay · {DelayMs} ms";

    public bool CanToggle => _parent.IsReserved(this) || _parent.SharingCount < AudioShareViewModel.MaxShareTargets;

    internal void RefreshCanToggle() => OnPropertyChanged(nameof(CanToggle));

    partial void OnDelayMsChanged(int value)
    {
        if (_syncing)
        {
            return;
        }

        _parent.SaveDelay(Id, value);
        if (_share is { } share)
        {
            share.DelayMs = value;
        }
    }

    partial void OnIsSharingChanged(bool value)
    {
        if (_syncing || _disposed)
        {
            return;
        }

        if (!value)
        {
            _ = StopAsync(++_version);
            return;
        }

        if (!_parent.TryReserveSlot(this))
        {
            SetSharing(false);
            return;
        }

        Status = null;
        if (_share is null)
        {
            var share = _createShare();
            share.DelayMs = DelayMs;
            void Handler(object? s, EventArgs e) => Dispatcher.UIThread.Post(() => _ = OnShareChangedAsync(share));
            share.Changed += Handler;
            _detach = () => share.Changed -= Handler;
            _share = share;
        }

        _ = StartAsync(++_version);
    }

    private void SetSharing(bool value)
    {
        _syncing = true;
        try
        {
            IsSharing = value;
        }
        finally
        {
            _syncing = false;
        }
    }

    // Operations run one at a time and only the latest request survives, so the last choice always wins.
    private async Task StartAsync(int version)
    {
        await _gate.WaitAsync();
        try
        {
            if (version != _version || _share is not { } share)
            {
                return;
            }

            try
            {
                await share.StartAsync(Id);
            }
            catch (Exception e)
            {
                Status = e.Message;
                await TearDownAsync(share);
                return;
            }

            if (share.State == AudioShareState.Stopped)
            {
                Status = share.StatusText;
                await TearDownAsync(share);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopAsync(int version)
    {
        await _gate.WaitAsync();
        try
        {
            if (version != _version)
            {
                return;
            }

            if (_share is { } share)
            {
                try
                {
                    await share.StopAsync();
                }
                catch (Exception e)
                {
                    Status = e.Message;
                }

                await TearDownAsync(share);
            }
            else
            {
                _parent.ReleaseSlot(this);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // A share that ended by itself (device unplugged and so on). Only acts if it is still this row's current share.
    private async Task OnShareChangedAsync(IAudioShare share)
    {
        if (share != _share || share.State is AudioShareState.Sharing or AudioShareState.Starting)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            if (share != _share)
            {
                return;
            }

            Status = share.StatusText;
            await TearDownAsync(share);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Callers hold the gate. Frees the share and the slot; the next toggle-on makes a fresh share.
    private async Task TearDownAsync(IAudioShare share)
    {
        if (share != _share)
        {
            return;
        }

        _detach?.Invoke();
        _detach = null;
        _share = null;
        _parent.ReleaseSlot(this);
        SetSharing(false);
        try
        {
            await share.DisposeAsync();
        }
        catch (Exception e)
        {
            Status = e.Message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _version++;
        _detach?.Invoke();
        _detach = null;
        var share = _share;
        _share = null;
        _parent.ReleaseSlot(this);
        if (share is not null)
        {
            await share.DisposeAsync();
        }
    }
}

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
    private Task? _disposing;

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

    /// <summary>The share ended by itself while the row was meant to be sharing; lets the card report a disconnect if the row then goes away.</summary>
    internal bool StoppedByDevice { get; private set; }

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
        StoppedByDevice = false;
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
            if (version != _version)
            {
                return;
            }

            // A stop or device-driven teardown may have released the slot or the toggle since this start was queued.
            if (!_parent.TryReserveSlot(this))
            {
                SetSharing(false);
                return;
            }

            SetSharing(true);
            if (_share is null)
            {
                var created = _createShare();
                created.DelayMs = DelayMs;
                void Handler(object? s, EventArgs e) => Dispatcher.UIThread.Post(() => _ = OnShareChangedAsync(created));
                created.Changed += Handler;
                _detach = () => created.Changed -= Handler;
                _share = created;
            }

            var share = _share;
            try
            {
                await share.StartAsync(Id);
            }
            catch (Exception e)
            {
                Status = e.Message;
                await TearDownAsync(share, version);
                return;
            }

            if (share.State == AudioShareState.Stopped)
            {
                Status = share.StatusText;
                StoppedByDevice = share.StatusText is not null;
                await TearDownAsync(share, version);
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

                await TearDownAsync(share, version);
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
            if (share != _share || share.State is AudioShareState.Sharing or AudioShareState.Starting)
            {
                return;
            }

            Status = share.StatusText;
            StoppedByDevice = true;
            await TearDownAsync(share, _version);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Callers hold the gate. Frees the share; the slot and toggle go too unless a newer start (version moved on) wants them.
    private async Task TearDownAsync(IAudioShare share, int version)
    {
        if (share != _share)
        {
            return;
        }

        _detach?.Invoke();
        _detach = null;
        _share = null;
        if (version == _version)
        {
            _parent.ReleaseSlot(this);
            SetSharing(false);
        }

        // Kept so a later DisposeAsync of the row can wait for this disposal.
        var dispose = DisposeShareAsync(share);
        _disposing = dispose;
        try
        {
            await dispose;
        }
        catch (Exception e)
        {
            Status = e.Message;
        }
    }

    // async so a synchronous throw from DisposeAsync becomes a faulted task the callers observe.
    private static async Task DisposeShareAsync(IAudioShare share) => await share.DisposeAsync();

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

        try
        {
            await (_disposing ?? Task.CompletedTask);
        }
        catch
        {
            // Already reported on the row.
        }
    }
}

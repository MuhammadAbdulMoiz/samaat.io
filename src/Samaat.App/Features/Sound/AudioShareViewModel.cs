using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Samaat.App.Features.Settings;
using Samaat.Core.Audio;
using Samaat.Core.Platform;

namespace Samaat.App.Features.Sound;

/// <summary>The Share audio card: one row per other output, up to three shared to at once, and a way to make one the default.</summary>
public sealed partial class AudioShareViewModel : ObservableObject, IAsyncDisposable
{
    public const int MaxShareTargets = 3;

    private readonly IAudioShare _lister;
    private readonly Func<IAudioShare> _createShare;
    private readonly IDefaultOutputSwitcher _switcher;
    private readonly SettingsViewModel _settings;
    private readonly HashSet<ShareTargetViewModel> _reserved = [];
    private bool _disposed;

    public AudioShareViewModel(Func<IAudioShare> createShare, IDefaultOutputSwitcher switcher, SettingsViewModel settings)
    {
        _createShare = createShare;
        _switcher = switcher;
        _settings = settings;
        _lister = createShare();
        Rows.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(ShowNoRows));
        };
        Reconcile();
        _lister.Changed += OnChanged;
    }

    public string UnavailableText => "Sharing audio needs Windows.";

    public bool IsSupported => _lister.IsSupported;

    public bool CanSwitch => _switcher.IsSupported;

    public ObservableCollection<ShareTargetViewModel> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    public bool ShowNoRows => IsSupported && !HasRows;

    public int SharingCount => _reserved.Count;

    [ObservableProperty]
    public partial string DefaultName { get; set; } = "the default output";

    [ObservableProperty]
    public partial string? Status { get; set; }

    public string HelpText =>
        $"Plays everything from {DefaultName} on other outputs too. " +
        "If a copy is ahead, add delay. If it's behind, use Switch here to make that device the default and share from it. " +
        "Most Bluetooth adapters can't play to more than two headsets at once.";

    // Slots are reserved on the UI thread before a start is queued, so a 4th toggle can never slip in.
    internal bool TryReserveSlot(ShareTargetViewModel row)
    {
        if (!_reserved.Contains(row) && _reserved.Count >= MaxShareTargets)
        {
            return false;
        }

        if (_reserved.Add(row))
        {
            SlotsChanged();
        }

        return true;
    }

    internal void ReleaseSlot(ShareTargetViewModel row)
    {
        if (_reserved.Remove(row))
        {
            SlotsChanged();
        }
    }

    internal bool IsReserved(ShareTargetViewModel row) => _reserved.Contains(row);

    private void SlotsChanged()
    {
        OnPropertyChanged(nameof(SharingCount));
        foreach (var row in Rows)
        {
            row.RefreshCanToggle();
        }
    }

    internal void SaveDelay(string id, int ms) => _settings.SetShareDelay(id, ms);

    internal void SwitchTo(string id)
    {
        try
        {
            _switcher.SetDefault(id);
            Status = null;
        }
        catch (Exception e)
        {
            Status = $"Couldn't switch: {e.Message}";
        }
    }

    private void OnChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (!_disposed)
        {
            Reconcile();
        }
    });

    // Keeps row instances by id so toggles and delays survive refreshes.
    private void Reconcile()
    {
        var outputs = _lister.Outputs;
        var targets = outputs.Where(o => !o.IsDefault).ToList();
        DefaultName = outputs.FirstOrDefault(o => o.IsDefault)?.Name ?? "the default output";
        OnPropertyChanged(nameof(HelpText));

        foreach (var row in Rows.Where(r => targets.All(t => t.Id != r.Id)).ToList())
        {
            Rows.Remove(row);
            var becameDefault = outputs.Any(o => o.IsDefault && o.Id == row.Id);
            if (row.IsSharing && !becameDefault)
            {
                Status = $"Stopped sharing to {row.Name}: disconnected";
            }

            _ = row.DisposeAsync().AsTask().ContinueWith(_ => { }, TaskScheduler.Default);
        }

        for (var i = 0; i < targets.Count; i++)
        {
            if (Rows.All(r => r.Id != targets[i].Id))
            {
                Rows.Insert(
                    Math.Min(i, Rows.Count),
                    new ShareTargetViewModel(this, targets[i].Id, targets[i].Name, _createShare, _settings.GetShareDelay(targets[i].Id)));
            }
        }

        foreach (var row in Rows)
        {
            row.RefreshCanToggle();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _lister.Changed -= OnChanged;
        foreach (var row in Rows.ToList())
        {
            await row.DisposeAsync();
        }

        await _lister.DisposeAsync();
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Samaat.Handoff;
using Samaat.Handoff.Pairing;
using Samaat.Mobile.Runtime;

namespace Samaat.Mobile.ViewModels;

/// <summary>The Computer tab: pairing with a computer running Samaat, and its status once paired.</summary>
public sealed partial class ComputerViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan NearbyRefresh = TimeSpan.FromSeconds(2);

    private readonly MobileRuntime _runtime;
    private readonly Action<Action> _post;
    private readonly Action<string> _onMessage;
    private readonly ITimer _timer;
    private PairingSession? _session;
    private NearbyComputer? _pairingWith;

    public ComputerViewModel(MobileRuntime runtime, Action<Action> post, Action<string> onMessage)
    {
        _runtime = runtime;
        _post = post;
        _onMessage = onMessage;
        _runtime.Computer.Changed += OnChanged;
        // Beacons stop when pairing closes, so the nearby list is re-read on a timer to drop stale computers.
        _timer = runtime.Time.CreateTimer(_ => _post(Refresh), null, NearbyRefresh, NearbyRefresh);
        Refresh();
    }

    private PhoneLinkClient Link => _runtime.Computer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPairing), nameof(ShowNearby))]
    public partial bool IsPaired { get; set; }

    [ObservableProperty]
    public partial string ComputerName { get; set; } = "";

    [ObservableProperty]
    public partial bool IsOnline { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLinkError))]
    public partial string? LinkError { get; set; }

    public bool HasLinkError => LinkError is not null;

    public ObservableCollection<NearbyComputerViewModel> Nearby { get; } = [];

    /// <summary>The computer's earbuds and where they are, while online.</summary>
    public ObservableCollection<ComputerEarbudsViewModel> ComputerEarbuds { get; } = [];

    public bool HasComputerEarbuds => ComputerEarbuds.Count > 0;

    public string ComputerEarbudsTitle => $"Earbuds on {ComputerName}";

    public bool HasNearby => Nearby.Count > 0;

    public bool ShowSearching => !HasNearby;

    public string HowToText => "On the computer, open Samaat, go to Settings, and choose Pair a phone. Keep both on the same Wi-Fi.";

    /// <summary>Shown while a code is on screen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPairing), nameof(ShowNearby), nameof(ShowCode))]
    public partial string? Code { get; set; }

    public bool ShowCode => Code is not null;

    public bool ShowPairing => !IsPaired || ShowCode;

    public bool ShowNearby => !IsPaired && !ShowCode;

    [ObservableProperty]
    public partial string PairingTitle { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PairingStatus))]
    public partial bool IsAnswering { get; set; }

    [ObservableProperty]
    public partial bool IsDialing { get; set; }

    public string PairingStatus => IsAnswering ? "Waiting for the computer…" : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPairingError))]
    public partial string? PairingError { get; set; }

    public bool HasPairingError => PairingError is not null;

    public Task PendingPairing { get; private set; } = Task.CompletedTask;

    private Task BeginAsync(NearbyComputer computer) => PendingPairing = BeginPairingAsync(computer);

    private async Task BeginPairingAsync(NearbyComputer computer)
    {
        if (IsDialing || _session is not null)
        {
            return;
        }

        PairingError = null;
        IsDialing = true;
        PairingSession session;
        try
        {
            session = await Link.BeginPairingAsync(computer.Endpoint);
        }
        catch (HandshakeException ex)
        {
            PairingError = ex.Message;
            return;
        }
        finally
        {
            IsDialing = false;
        }

        _session = session;
        _pairingWith = computer;
        PairingTitle = $"Pairing with {computer.Name}";
        Code = session.Code;
        _ = session.PeerDecision.ContinueWith(t =>
        {
            if (!t.Result)
            {
                _post(() =>
                {
                    if (ReferenceEquals(_session, session) && !IsAnswering)
                    {
                        PairingError = $"Pairing was cancelled on {computer.Name}.";
                        _ = EndAsync(session, false);
                    }
                });
            }
        }, TaskScheduler.Default);
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (_session is not { } session)
        {
            return;
        }

        IsAnswering = true;
        var paired = await Link.FinishPairingAsync(session, true, endpoint: _pairingWith?.Endpoint);
        var name = _pairingWith?.Name ?? "the computer";
        Reset();
        if (paired)
        {
            _onMessage($"Paired with {name}.");
        }
        else
        {
            PairingError = $"Pairing with {name} didn't finish. It was cancelled there, or the codes didn't match.";
        }

        Refresh();
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (_session is { } session)
        {
            await EndAsync(session, false);
        }
    }

    private async Task EndAsync(PairingSession session, bool accept)
    {
        Reset();
        await Link.FinishPairingAsync(session, accept);
        Refresh();
    }

    private void Reset()
    {
        _session = null;
        _pairingWith = null;
        Code = null;
        IsAnswering = false;
    }

    [RelayCommand]
    private async Task ForgetAsync()
    {
        await Link.ForgetAsync();
        Refresh();
    }

    private void OnChanged(object? sender, EventArgs e) => _post(Refresh);

    private void Refresh()
    {
        var computer = Link.Computer;
        IsPaired = computer is not null;
        ComputerName = computer?.Name ?? "";
        IsOnline = Link.State == ComputerLinkState.Online;
        StatusText = Link.State switch
        {
            ComputerLinkState.Online => "Connected",
            ComputerLinkState.Connecting => "Connecting…",
            ComputerLinkState.Searching => "Looking for it on this Wi-Fi…",
            _ => "Not paired",
        };
        LinkError = IsOnline ? null : Link.LastError;
        ComputerEarbuds.Clear();
        foreach (var device in IsOnline ? Link.ComputerDevices : [])
        {
            ComputerEarbuds.Add(new ComputerEarbudsViewModel(device.Name, device.Connected, device.Connected ? $"Playing on {ComputerName}" : "Not connected"));
        }

        OnPropertyChanged(nameof(HasComputerEarbuds));
        OnPropertyChanged(nameof(ComputerEarbudsTitle));

        var nearby = Link.PairableComputers;
        if (!nearby.SequenceEqual(Nearby.Select(n => n.Computer)))
        {
            Nearby.Clear();
            foreach (var computerNearby in nearby)
            {
                Nearby.Add(new NearbyComputerViewModel(computerNearby, BeginAsync));
            }

            OnPropertyChanged(nameof(HasNearby));
            OnPropertyChanged(nameof(ShowSearching));
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _runtime.Computer.Changed -= OnChanged;
    }
}

public sealed record ComputerEarbudsViewModel(string Name, bool IsConnected, string StateText);

public sealed class NearbyComputerViewModel(NearbyComputer computer, Func<NearbyComputer, Task> pair)
{
    public NearbyComputer Computer { get; } = computer;

    public string Name => Computer.Name;

    public string Detail => "Ready to pair";

    public string PairAccessibleName => $"Pair with {Name}";

    public IAsyncRelayCommand PairCommand { get; } = new AsyncRelayCommand(() => pair(computer));
}

using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Samaat.Core.Devices;
using Samaat.Handoff;

namespace Samaat.App.Features.Phone;

/// <summary>
/// Settings → Phone: pairing with phones running Samaat, and the paired list. Owns the <see cref="PhoneLinkHost"/>
/// and hands each device a <see cref="PhoneMoveViewModel"/> for the Links page.
/// </summary>
public sealed partial class PhoneLinkViewModel : ObservableObject, IAsyncDisposable
{
    private readonly PhoneLinkHost _host;
    private readonly Action<Action> _post;
    private readonly Action<string>? _onMessage;
    private readonly List<IEarbudDevice> _tracked = [];
    private IReadOnlyList<IEarbudDevice> _devices = [];

    /// <param name="createHost">Builds the host around a function that returns this computer's current devices.</param>
    public PhoneLinkViewModel(Func<Func<IReadOnlyList<IEarbudDevice>>, PhoneLinkHost> createHost, Action<Action>? post = null, Action<string>? onMessage = null)
    {
        _post = post ?? (a => Dispatcher.UIThread.Post(a));
        _onMessage = onMessage;
        _host = createHost(() => _devices);
        _host.Changed += (_, _) => _post(Refresh);
        _host.PairingRequested += (_, request) => _post(() => ShowRequest(request));
        _host.Activity += (_, activity) => _post(() => Activity?.Invoke(this, activity));
        _host.Start();
        Refresh();
    }

    public PhoneLinkHost Host => _host;

    public string ComputerName => _host.ComputerName;

    public ObservableCollection<PairedPhoneViewModel> Phones { get; } = [];

    public bool HasPhones => Phones.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPairButton), nameof(ShowWaiting))]
    public partial bool IsPairingOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWaiting), nameof(HasRequest))]
    public partial PairingPromptViewModel? Request { get; set; }

    public bool HasRequest => Request is not null;

    public bool ShowPairButton => !IsPairingOpen;

    public bool ShowWaiting => IsPairingOpen && Request is null;

    public string WaitingText => $"On your phone, open Samaat, tap Pair with a computer, and choose {ComputerName}. Keep both on the same Wi-Fi.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorText { get; set; }

    public bool HasError => ErrorText is not null;

    /// <summary>Moves in progress anywhere, including ones started on the phone. UI thread.</summary>
    public event EventHandler<HandoffActivity>? Activity;

    /// <summary>The phones changed (paired, online, their earbuds). UI thread.</summary>
    public event EventHandler? PhonesChanged;

    [RelayCommand]
    private void Pair()
    {
        ErrorText = null;
        _host.OpenPairing();
        Refresh();
        if (_host.ListenError is { } error)
        {
            ErrorText = error;
        }
    }

    [RelayCommand]
    private async Task CancelPairingAsync()
    {
        if (Request is { } request)
        {
            await request.DeclineCommand.ExecuteAsync(null);
        }

        _host.ClosePairing();
        Refresh();
    }

    /// <summary>Tracks this computer's devices: phones are told when one connects, disconnects, appears or goes.</summary>
    public void SetDevices(IReadOnlyList<IEarbudDevice> devices)
    {
        foreach (var device in _tracked)
        {
            device.ConnectionChanged -= OnDeviceChanged;
        }

        _tracked.Clear();
        _tracked.AddRange(devices);
        foreach (var device in _tracked)
        {
            device.ConnectionChanged += OnDeviceChanged;
        }

        _devices = [.. devices];
        _ = _host.PublishDevicesAsync();
    }

    public PhoneMoveViewModel CreateMove(IEarbudDevice device, Action? openSettings = null) => new(device, this, _post, _onMessage, openSettings);

    private void OnDeviceChanged(object? sender, EventArgs e) => _ = _host.PublishDevicesAsync();

    private void ShowRequest(PairingRequest request)
    {
        var prompt = new PairingPromptViewModel(request, async paired =>
        {
            if (ReferenceEquals(Request?.Source, request))
            {
                Request = null;
            }

            if (paired)
            {
                _onMessage?.Invoke($"{request.PhoneName} is paired. You can now move earbuds between it and this computer.");
            }

            Refresh();
            await Task.CompletedTask;
        });
        Request = prompt;
        _ = request.PhoneDecision.ContinueWith(t =>
        {
            if (!t.Result)
            {
                _post(() =>
                {
                    if (ReferenceEquals(Request, prompt) && !prompt.IsAnswering)
                    {
                        Request = null;
                        ErrorText = $"{request.PhoneName} cancelled pairing.";
                    }
                });
            }
        }, TaskScheduler.Default);
    }

    private void Refresh()
    {
        IsPairingOpen = _host.IsPairingOpen;
        var phones = _host.Phones;
        Phones.Clear();
        foreach (var phone in phones)
        {
            Phones.Add(new PairedPhoneViewModel(phone, id =>
            {
                _host.Forget(id);
                Refresh();
            }));
        }

        OnPropertyChanged(nameof(HasPhones));
        PhonesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var device in _tracked)
        {
            device.ConnectionChanged -= OnDeviceChanged;
        }

        await _host.DisposeAsync();
    }
}

public sealed class PairedPhoneViewModel
{
    public PairedPhoneViewModel(PhoneStatus phone, Action<string> forget)
    {
        Id = phone.Id;
        Name = phone.Name;
        IsOnline = phone.Online;
        StatusText = phone.Online ? "Connected" : "Not connected · open Samaat on the phone";
        ForgetCommand = new RelayCommand(() => forget(Id));
    }

    public string Id { get; }

    public string Name { get; }

    public bool IsOnline { get; }

    public string StatusText { get; }

    public string ForgetAccessibleName => $"Forget {Name}";

    public IRelayCommand ForgetCommand { get; }
}

/// <summary>A phone asking to pair: both screens show <see cref="Code"/>.</summary>
public sealed partial class PairingPromptViewModel : ObservableObject
{
    private readonly Func<bool, Task> _done;

    public PairingPromptViewModel(PairingRequest request, Func<bool, Task> done)
    {
        Source = request;
        _done = done;
    }

    public PairingRequest Source { get; }

    public string Title => $"{Source.PhoneName} wants to pair";

    public string Code => Source.Code;

    public string Hint => "Pair only if your phone shows the same code.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool IsAnswering { get; set; }

    public string StatusText => IsAnswering ? "Waiting for the phone…" : "";

    [RelayCommand]
    private async Task AcceptAsync()
    {
        IsAnswering = true;
        var paired = await Source.AcceptAsync();
        await _done(paired);
    }

    [RelayCommand]
    private async Task DeclineAsync()
    {
        IsAnswering = true;
        await Source.DeclineAsync();
        await _done(false);
    }
}

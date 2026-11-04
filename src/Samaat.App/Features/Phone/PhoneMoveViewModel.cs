using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Samaat.Core.Connections;
using Samaat.Core.Devices;
using Samaat.Handoff;
using Samaat.Handoff.Moving;
using Samaat.Handoff.Protocol;

namespace Samaat.App.Features.Phone;

/// <summary>The Links page's "Your phone" card for one pair of earbuds: move them to a paired phone, or bring them back.</summary>
public sealed partial class PhoneMoveViewModel : ObservableObject, IDisposable
{
    private readonly IEarbudDevice _device;
    private readonly PhoneLinkViewModel _link;
    private readonly Action<Action> _post;
    private readonly Action<string>? _onMessage;
    private readonly string? _address;

    internal PhoneMoveViewModel(IEarbudDevice device, PhoneLinkViewModel link, Action<Action> post, Action<string>? onMessage, Action? openSettings)
    {
        _device = device;
        _link = link;
        _post = post;
        _onMessage = onMessage;
        _address = device.Identity.Details?.Address is { } a ? MessageCodec.NormalizeAddress(a) : null;
        OpenSettingsCommand = new RelayCommand(() => openSettings?.Invoke(), () => openSettings is not null);
        _link.PhonesChanged += OnPhonesChanged;
        _link.Activity += OnActivity;
        _device.ConnectionChanged += OnDeviceChanged;
        Refresh();
    }

    public ObservableCollection<PhoneTargetViewModel> Targets { get; } = [];

    public bool HasPhones => Targets.Count > 0;

    public bool ShowSetup => !HasPhones;

    public string SetupText =>
        "Install Samaat on your Android phone and pair it in Settings to move these earbuds between the phone and this computer, even if they connect to one device at a time.";

    public IRelayCommand OpenSettingsCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    public partial string? ProgressText { get; set; }

    public bool ShowProgress => ProgressText is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult), nameof(ShowSuccess), nameof(ShowFailure))]
    public partial string? ResultText { get; set; }

    public bool HasResult => ResultText is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSuccess), nameof(ShowFailure))]
    public partial bool ResultIsError { get; set; }

    public bool ShowSuccess => HasResult && !ResultIsError;

    public bool ShowFailure => HasResult && ResultIsError;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public Task PendingMove { get; private set; } = Task.CompletedTask;

    private Task MoveAsync(string phoneId, HandoffDirection direction)
    {
        if (IsBusy)
        {
            return Task.CompletedTask;
        }

        return PendingMove = RunAsync(phoneId, direction);
    }

    private async Task RunAsync(string phoneId, HandoffDirection direction)
    {
        IsBusy = true;
        ResultText = null;
        Refresh();
        var progress = new Progress<HandoffProgress>(p => _post(() => ProgressText = p.Stage == HandoffStage.Done ? null : p.Text));
        HandoffResult result;
        try
        {
            result = await _link.Host.MoveAsync(_device, phoneId, direction, progress);
        }
        finally
        {
            IsBusy = false;
        }

        ProgressText = null;
        ResultText = result.Message;
        ResultIsError = !result.Succeeded;
        if (!result.Succeeded)
        {
            _onMessage?.Invoke(result.Message);
        }

        Refresh();
    }

    private void OnActivity(object? sender, HandoffActivity activity)
    {
        // Moves started on the phone show here too.
        if (activity.Address != _address || IsBusy)
        {
            return;
        }

        if (activity.Progress.Stage == HandoffStage.Done)
        {
            ProgressText = null;
            ResultText = activity.Progress.Text;
            ResultIsError = false;
        }
        else
        {
            ProgressText = activity.Progress.Text;
        }
    }

    private void OnPhonesChanged(object? sender, EventArgs e) => Refresh();

    private void OnDeviceChanged(object? sender, EventArgs e) => _post(Refresh);

    private void Refresh()
    {
        var here = _device.Connection == ConnectionState.Connected;
        var canLink = _device.GetFeature<IPcLinkFeature>() is not null && _address is not null;
        Targets.Clear();
        foreach (var phone in _link.Host.Phones)
        {
            Targets.Add(new PhoneTargetViewModel(phone, _address is null ? null : phone.FindDevice(_address), here, canLink, IsBusy, MoveAsync));
        }

        OnPropertyChanged(nameof(HasPhones));
        OnPropertyChanged(nameof(ShowSetup));
    }

    public void Dispose()
    {
        _link.PhonesChanged -= OnPhonesChanged;
        _link.Activity -= OnActivity;
        _device.ConnectionChanged -= OnDeviceChanged;
    }
}

/// <summary>One paired phone as a destination for these earbuds.</summary>
public sealed class PhoneTargetViewModel
{
    public PhoneTargetViewModel(PhoneStatus phone, PeerDevice? onPhone, bool connectedHere, bool canLink, bool busy, Func<string, HandoffDirection, Task> move)
    {
        Name = phone.Name;
        IsOnline = phone.Online;
        var connectedThere = onPhone?.Connected == true;
        StateText = !phone.Online ? $"Not connected. Open Samaat on {phone.Name}, on the same Wi-Fi."
            : !canLink ? "Samaat can't connect or disconnect these earbuds on this computer."
            : onPhone is null ? $"These earbuds aren't paired with {phone.Name}. Pair them in the phone's Bluetooth settings."
            : connectedThere ? $"Playing on {phone.Name}"
            : connectedHere ? $"{phone.Name} can take these earbuds"
            : $"Not connected to {phone.Name} or this computer";
        var ready = phone.Online && canLink && onPhone is not null && !busy;
        CanMoveThere = ready && !connectedThere;
        CanBringHere = ready && !connectedHere;
        MoveText = $"Move to {phone.Name}";
        MoveCommand = new AsyncRelayCommand(() => move(phone.Id, HandoffDirection.ToPhone), () => CanMoveThere);
        BringHereCommand = new AsyncRelayCommand(() => move(phone.Id, HandoffDirection.ToComputer), () => CanBringHere);
    }

    public string Name { get; }

    public bool IsOnline { get; }

    public string StateText { get; }

    public bool CanMoveThere { get; }

    public bool CanBringHere { get; }

    public string MoveText { get; }

    public IAsyncRelayCommand MoveCommand { get; }

    public IAsyncRelayCommand BringHereCommand { get; }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Samaat.Core.Battery;
using Samaat.Core.Commands;
using Samaat.Core.Devices;
using Samaat.Core.Equalizer;
using Samaat.Core.NoiseControl;
using Samaat.Core.Profiles;
using Avalonia.Media.Imaging;
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
    private readonly DeviceProfile? _profile;
    private string? _lookChoice;
    private ResolvedLook? _look;
    private IReadOnlyList<Bitmap> _partBitmaps = [];

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
        _profile = runtime.Profiles.Match(device.Identity);
        _lookChoice = runtime.Looks.Get(device.Identity.Id);

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

    /// <summary>Hidden while the computer has the earbuds: then "Bring to this phone" is the one primary action.</summary>
    public bool ShowConnect => Address is not null && !IsConnected && !IsLinkBusy && !ComputerHasThem;

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

    /// <summary>The hero figure: number and unit apart, like the desktop ("81" "%").</summary>
    [ObservableProperty]
    public partial string SummaryValue { get; set; } = "–";

    [ObservableProperty]
    public partial string SummaryUnit { get; set; } = "";

    [ObservableProperty]
    public partial double SummaryPercent { get; set; }

    [ObservableProperty]
    public partial bool HasSummary { get; set; }

    [ObservableProperty]
    public partial bool SummaryIsLow { get; set; }

    [ObservableProperty]
    public partial bool SummaryIsCritical { get; set; }

    /// <summary>"Right bud low · 17%": low battery is never shown by colour alone (docs/design/ui-audit.md, principle 2).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlert))]
    public partial string? AlertText { get; set; }

    public bool HasAlert => AlertText is not null;

    [ObservableProperty]
    public partial bool AlertIsCritical { get; set; }

    [ObservableProperty]
    public partial string BatteryReasonText { get; set; } = "";

    // Artwork: the desktop's looks (docs/design/ui-audit.md), from the same devices/ photos and ArtworkLook.Resolve.

    /// <summary>Whether this device draws two buds (true) or a headset (false) in the "?" look.</summary>
    public bool HasBuds => _battery?.Components.Contains(BatteryComponent.Left) == true
        || new[] { "bud", "pods", "earphone", "tws" }.Any(k => Name.Contains(k, StringComparison.OrdinalIgnoreCase));

    public string ArtworkLabel => $"{Name} {(HasBuds ? "earbuds" : "headset")}";

    /// <summary>The model photo inside the ring; null for the "?" looks.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArtworkImage))]
    public partial Bitmap? ArtworkImage { get; set; }

    public bool HasArtworkImage => ArtworkImage is not null;

    /// <summary>Left bud, right bud and case photos, each with its own figure (the split look).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ArtworkPartViewModel> ArtworkParts { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRing))]
    public partial bool ShowArtworkParts { get; set; }

    [ObservableProperty]
    public partial bool ShowUnknownSingle { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRing))]
    public partial bool ShowUnknownPieces { get; set; }

    /// <summary>The ring (with a photo or the single "?" drawing) shows unless the look is in separate pieces.</summary>
    public bool ShowRing => !ShowArtworkParts && !ShowUnknownPieces;

    /// <summary>Left bud, right bud and case figures for the pieces looks; "–" when not reported.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<PieceFigureViewModel> Pieces { get; set; } = [];

    /// <summary>"Change look": Automatic, the two "?" looks, then every model with a photo.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<LookOptionViewModel> LookOptions { get; set; } = [];

    private void ChangeLook(string key)
    {
        _lookChoice = key == ArtworkLook.Automatic ? null : key;
        _runtime.Looks.Set(Device.Identity.Id, _lookChoice);
        ApplyLook();
    }

    private bool ReportsBuds => _battery?.Current is { } state
        && (state.Get(BatteryComponent.Left) is not null || state.Get(BatteryComponent.Right) is not null);

    private void ApplyLook()
    {
        var profiles = _runtime.Profiles.Profiles;
        var look = ArtworkLook.Resolve(_lookChoice, _profile, profiles, ReportsBuds);
        if (!Equals(look, _look))
        {
            _look = look;
            var oldImage = ArtworkImage;
            var oldParts = _partBitmaps;
            ArtworkImage = Load(look.Profile?.Image);
            if (look.Profile is not null && ArtworkImage is null)
            {
                look = _look = new ResolvedLook(ReportsBuds ? LookKind.UnknownPieces : LookKind.UnknownSingle); // no photo to show
            }

            _partBitmaps = [];
            if (look.Kind == LookKind.PartsPhoto && look.Profile!.PartImages is { } parts)
            {
                var bitmaps = new[] { parts.Left, parts.Right, parts.Case }.Select(Load).ToList();
                if (bitmaps.All(b => b is not null))
                {
                    _partBitmaps = bitmaps!;
                }
                else
                {
                    bitmaps.ForEach(b => b?.Dispose());
                    look = _look = new ResolvedLook(LookKind.Photo, look.Profile); // a broken part photo keeps the whole photo
                }
            }

            ShowArtworkParts = look.Kind == LookKind.PartsPhoto;
            ShowUnknownSingle = look.Kind == LookKind.UnknownSingle;
            ShowUnknownPieces = look.Kind == LookKind.UnknownPieces;
            oldImage?.Dispose();
            foreach (var bitmap in oldParts)
            {
                bitmap.Dispose();
            }
        }

        ArtworkParts = _partBitmaps.Count == 3 && Pieces.Count == 3
            ? [.. _partBitmaps.Zip(Pieces, (bitmap, figure) => new ArtworkPartViewModel(figure, bitmap))]
            : [];

        var key = ArtworkLook.MenuKey(_lookChoice, profiles);
        if (LookOptions.Count == 0 || LookOptions.Single(o => o.IsSelected).Key != key)
        {
            LookOptionViewModel Option(string k, string label) => new(k, label, k == key, new RelayCommand(() => ChangeLook(k)));
            LookOptions =
            [
                Option(ArtworkLook.Automatic, "Automatic"),
                Option(ArtworkLook.UnknownSingle, "Unknown · single"),
                Option(ArtworkLook.UnknownPieces, "Unknown · separate pieces"),
                .. profiles.Where(p => p.Image is not null).OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .Select(p => Option(p.Id, p.DisplayName)),
            ];
        }
    }

    private static Bitmap? Load(string? path)
    {
        try
        {
            return path is null ? null : new Bitmap(path);
        }
        catch (Exception)
        {
            return null; // a broken image file falls back to the drawn artwork
        }
    }

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
    [NotifyPropertyChangedFor(nameof(ShowConnect))]
    public partial bool ComputerHasThem { get; set; }

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
        ComputerHasThem = IsComputerOnline && there?.Connected == true;
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

        Pieces = [.. new[] { BatteryComponent.Left, BatteryComponent.Right, BatteryComponent.Case }
            .Select(c => new PieceFigureViewModel(c, state.Get(c)))];
        ApplyLook();

        var summary = state.SummaryPercent;
        SummaryText = summary is { } p ? $"{p}%" : "–";
        SummaryValue = summary?.ToString() ?? "–";
        SummaryUnit = summary is null ? "" : "%";
        SummaryPercent = summary ?? 0;
        HasSummary = summary is not null;

        // The most urgent bud (or the single level) decides the alert; the case only informs, as on the desktop by default.
        var urgent = Battery.Where(c => c.Component != BatteryComponent.Case && (c.IsLow || c.IsCritical))
            .OrderByDescending(c => c.IsCritical).ThenBy(c => c.Percent).FirstOrDefault();
        SummaryIsCritical = urgent?.IsCritical == true;
        SummaryIsLow = urgent is { IsCritical: false };
        AlertIsCritical = SummaryIsCritical;
        AlertText = urgent is null ? null : $"{urgent.Label} {(urgent.IsCritical ? "critically low" : "low")} · {urgent.Value}%";
        BatteryReasonText = _battery is null ? "This device reports no battery level."
            : Battery.Count == 0 ? IsConnected ? "Waiting for a reading." : "Connect the earbuds to read their battery."
            : Battery.Any(c => c.Component is BatteryComponent.Left or BatteryComponent.Right) ? "Each level is reported by the earbuds."
            : "Left, right and case aren't reported for this device.";
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
                () => SetAsync(ct => _noise.SetModeAsync(mode, ct)),
                mode switch
                {
                    NoiseMode.NoiseCancelling => "Icon.NoiseCancelling",
                    NoiseMode.Adaptive => "Icon.Sparkle",
                    NoiseMode.Transparency => "Icon.Transparency",
                    _ => "Icon.CircleOff",
                }));
        }

        NoiseStatusText = _noise.Current.Mode == NoiseMode.Off
            ? "Noise control is off"
            : $"{NoiseControlPresentation.Label(_noise.Current.Mode)} is on";
    }

    [ObservableProperty]
    public partial string NoiseStatusText { get; set; } = "";

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
        ArtworkImage?.Dispose();
        foreach (var bitmap in _partBitmaps)
        {
            bitmap.Dispose();
        }

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

/// <summary>
/// One battery figure with the desktop's anatomy: label, value and unit apart, a level bar, and a meta line.
/// Low and critical use the desktop's default thresholds (20 % and 10 %) and always say so in words.
/// </summary>
public sealed class BatteryCellViewModel
{
    public const int LowThreshold = 20;
    public const int CriticalThreshold = 10;

    public BatteryCellViewModel(BatteryReading reading)
    {
        Component = reading.Component;
        Label = reading.Component switch
        {
            BatteryComponent.Left => "Left bud",
            BatteryComponent.Right => "Right bud",
            BatteryComponent.Case => "Case",
            _ => "Battery",
        };
        HasLevel = reading.Percent is not null;
        Percent = reading.Percent ?? 0;
        Value = reading.Percent?.ToString() ?? "–";
        Unit = HasLevel ? "%" : "";
        IsCharging = reading.IsCharging;
        IsCritical = HasLevel && Percent <= CriticalThreshold && !IsCharging;
        IsLow = HasLevel && Percent <= LowThreshold && !IsCharging && !IsCritical;
        var state = IsCritical ? "Critical" : IsLow ? "Low" : IsCharging ? "Charging" : reading.Charging == ChargingState.Full ? "Full" : "";
        var where = reading.Status switch
        {
            BudStatus.InCase => "In case",
            BudStatus.Connected => "In use",
            BudStatus.Disconnected => "Not connected",
            _ => "",
        };
        Meta = string.Join(" · ", new[] { state, where }.Where(t => t.Length > 0));
        AccessibleText = HasLevel
            ? $"{Label}, {Value} percent{(state.Length > 0 ? $", {state.ToLowerInvariant()}" : "")}"
            : $"{Label}, no reading";
    }

    public BatteryComponent Component { get; }

    public string Label { get; }

    public string Value { get; }

    public string Unit { get; }

    public double Percent { get; }

    public bool HasLevel { get; }

    public bool IsLow { get; }

    public bool IsCritical { get; }

    public bool IsCharging { get; }

    public string Meta { get; }

    public string AccessibleText { get; }
}

/// <summary>One piece of the device in the artwork (left bud, right bud, case) with its level, as on the desktop rail.</summary>
public sealed class PieceFigureViewModel
{
    public PieceFigureViewModel(BatteryComponent component, BatteryReading? reading)
    {
        var label = component switch
        {
            BatteryComponent.Left => "Left bud",
            BatteryComponent.Right => "Right bud",
            _ => "Case",
        };
        HasLevel = reading?.Percent is not null;
        Value = reading?.Percent?.ToString() ?? "–";
        Unit = HasLevel ? "%" : "";
        IsCharging = reading?.IsCharging == true;
        AccessibleText = HasLevel ? $"{label}, {Value} percent{(IsCharging ? ", charging" : "")}" : $"{label}, not reported";
    }

    public string Value { get; }

    public string Unit { get; }

    public bool HasLevel { get; }

    public bool IsCharging { get; }

    public string AccessibleText { get; }
}

public sealed record ArtworkPartViewModel(PieceFigureViewModel Figure, Bitmap Image);

/// <summary>One entry of the "Change look" menu; <see cref="Key"/> is "auto", "unknown.single", "unknown.pieces" or a profile id.</summary>
public sealed record LookOptionViewModel(string Key, string Label, bool IsSelected, System.Windows.Input.ICommand Select)
{
    public string AccessibleName => IsSelected ? $"{Label}, current look" : Label;
}

/// <summary>A selectable choice: a noise mode (with its icon) or an EQ preset.</summary>
public sealed class OptionViewModel(string label, string? description, bool isSelected, Func<Task> select, string? iconKey = null)
{
    public string? IconKey { get; } = iconKey;

    public bool HasIcon => IconKey is not null;

    public string Label { get; } = label;

    public string? Description { get; } = description;

    public bool HasDescription => Description is not null;

    public bool IsSelected { get; } = isSelected;

    public IAsyncRelayCommand SelectCommand { get; } = new AsyncRelayCommand(select);
}

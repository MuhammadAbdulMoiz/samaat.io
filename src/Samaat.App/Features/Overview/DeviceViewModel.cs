using Samaat.App.Features.Details;
using Samaat.App.Features.Info;
using Samaat.App.Shared;
using Samaat.App.Features.Connections;
using Samaat.App.Features.Sound;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Connections;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Core.NoiseControl;
using Samaat.Core.Profiles;
using Samaat.Core.Diagnostics;

namespace Samaat.App.Features.Overview;

public sealed partial class DeviceViewModel : ObservableObject, IDisposable
{
    private readonly IEarbudDevice _device;
    private readonly IBatteryFeature? _battery;
    private readonly LowBatteryDetector _alerts;
    private readonly TimeProvider _time;
    private readonly Action<DeviceViewModel, BatteryAlert> _onAlert;
    private readonly Action<string>? _onMessage;
    private readonly IPcLinkFeature? _pcLink;

    private readonly DeviceProfile? _profile;
    private readonly IReadOnlyList<DeviceProfile> _profiles;
    private readonly Settings.SettingsViewModel? _settings;
    private string? _choice;
    private ResolvedLook? _look;

    public DeviceViewModel(IEarbudDevice device, LowBatteryDetector alerts, TimeProvider time, Action<DeviceViewModel, BatteryAlert> onAlert,
        DeviceProfile? profile = null, IReadOnlyList<DeviceProfile>? profiles = null, Settings.SettingsViewModel? settings = null,
        Action<string>? onMessage = null)
    {
        _device = device;
        _profile = profile;
        _profiles = profiles ?? [];
        _settings = settings;
        _choice = settings?.GetArtworkChoice(device.Identity.Id);
        _battery = device.GetFeature<IBatteryFeature>();
        _alerts = alerts;
        _time = time;
        _onAlert = onAlert;
        _onMessage = onMessage;
        _pcLink = device.GetFeature<IPcLinkFeature>();
        Initials = string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
        if (Initials.Length == 0)
        {
            Initials = "?";
        }

        Tiles = new ObservableCollection<BatteryTileViewModel>(
            new[] { BatteryComponent.Combined, BatteryComponent.Left, BatteryComponent.Right, BatteryComponent.Case }
                .Select(c => new BatteryTileViewModel(c)));

        NoiseControl = new NoiseControlViewModel(device, time: time);
        Equalizer = new EqualizerViewModel(device, time: time);
        Connections = new ConnectionsViewModel(device, time: time);
        Info = new DeviceInfoViewModel(device, time: time, profile: profile);
        Details = new BluetoothDetailsViewModel(this, profile);

        _device.ConnectionChanged += OnDeviceChanged;
        if (_battery is not null)
        {
            _battery.Changed += OnBatteryChanged;
        }

        Update();
    }

    public IEarbudDevice Device => _device;

    public string Name => _device.Identity.Name;

    public string Subtitle => string.Join(" · ", new[]
    {
        // "Audionic · Audionic ParadoX-E" reads twice, so the maker is dropped when the model already names it.
        ContainsWords(_device.Identity.Model, _device.Identity.Manufacturer) ? null : _device.Identity.Manufacturer,
        // The profile names the model when Windows didn't report maker or model.
        _device.Identity.Manufacturer is null && _device.Identity.Model is null ? _profile?.DisplayName : _device.Identity.Model,
        _device.Identity.FirmwareVersion is { } fw ? $"Firmware {fw}" : null,
    }.Where(s => !string.IsNullOrWhiteSpace(s) && !ContainsWords(Name, s))); // don't repeat the title

    // Whole words only: "HP" must not match inside "SHP-9500".
    private static bool ContainsWords(string? text, string? phrase) =>
        text is not null && !string.IsNullOrWhiteSpace(phrase)
        && System.Text.RegularExpressions.Regex.IsMatch(text, $@"(?<![\w]){System.Text.RegularExpressions.Regex.Escape(phrase)}(?![\w])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    // A catalogued model's own driver needs the buds connected; until then "Basic" would undersell it.
    private bool WaitsForDriver => DriverRetry.WaitsForConnection(_device);

    public string TierText => WaitsForDriver ? "Connect for full support" : CapabilityText.Tier(_device.Identity.Tier);

    public string TierDescription => WaitsForDriver ? "Samaat speaks this model's own protocol once the earbuds are connected" : _device.Identity.Tier switch
    {
        SupportTier.Full => "Samaat speaks this model's own protocol",
        SupportTier.Standard => "Standard Bluetooth services only",
        _ => "What your operating system reports",
    };

    /// <summary>All four battery components, reported or not.</summary>
    public ObservableCollection<BatteryTileViewModel> Tiles { get; }

    /// <summary>The components shown as gauges in the hero, in display order.</summary>
    public ObservableCollection<BatteryTileViewModel> HeroTiles { get; } = [];

    /// <summary>Components this device doesn't report, shown as small chips rather than empty gauges.</summary>
    public ObservableCollection<BatteryTileViewModel> NotReported { get; } = [];

    public NoiseControlViewModel NoiseControl { get; }

    public EqualizerViewModel Equalizer { get; }

    /// <summary>Shared by all devices; owned and disposed by the main view model.</summary>
    public Sound.AudioShareViewModel? Share { get; set; }

    /// <summary>The Links page's "Your phone" card; set by the shell.</summary>
    public Phone.PhoneMoveViewModel? PhoneMove { get; set; }

    /// <summary>The phone handoff is switched off (<see cref="AppOptions.PhoneHandoff"/>): the Links page says it's coming.</summary>
    public bool ShowPhoneComingSoon { get; set; }

    public ConnectionsViewModel Connections { get; }

    public DeviceInfoViewModel Info { get; }

    /// <summary>Bluetooth IDs, address and services; separate from <see cref="Info"/>, the diagnostics page.</summary>
    public BluetoothDetailsViewModel Details { get; }

    /// <summary>Two letters for the collapsed sidebar rail.</summary>
    public string Initials { get; }

    /// <summary>The photo of the model in the current look; null when the look is one of the "?" drawings.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArtworkImage))]
    public partial Avalonia.Media.Imaging.Bitmap? ArtworkImage { get; set; }

    public bool HasArtworkImage => ArtworkImage is not null;

    private static Avalonia.Media.Imaging.Bitmap? LoadArtwork(string? path)
    {
        try
        {
            return path is null ? null : new Avalonia.Media.Imaging.Bitmap(path);
        }
        catch (Exception e)
        {
            SamaatLog.Warn("Artwork", $"Couldn't load {path}; drawing the artwork instead", e);
            return null; // a broken image file falls back to the drawn artwork
        }
    }

    /// <summary>Left bud, right bud and case photos with their figures; empty unless the look is the split photo.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ArtworkPartViewModel> ArtworkParts { get; set; } = [];

    /// <summary>The rail shows the split photo: all three parts exist and the device reports each bud.</summary>
    [ObservableProperty]
    public partial bool ShowArtworkParts { get; set; }

    /// <summary>One faint silhouette with a "?" badge: no photo, and the device reports a combined level.</summary>
    [ObservableProperty]
    public partial bool ShowUnknownSingle { get; set; }

    /// <summary>Faint left bud, right bud and case with "?" badges, each with its own figure.</summary>
    [ObservableProperty]
    public partial bool ShowUnknownPieces { get; set; }

    /// <summary>The "Change look" menu: Automatic, the two unknown looks, then every model that has a photo.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<LookOptionViewModel> LookOptions { get; set; } = [];

    /// <summary>Picks an artwork look for this device ("auto", "unknown.single", "unknown.pieces" or a profile id) and remembers it.</summary>
    [RelayCommand]
    private void ChangeLook(string? key)
    {
        _choice = key is null or "auto" ? null : key;
        _settings?.SetArtworkChoice(_device.Identity.Id, _choice);
        ApplyLook(Tile(BatteryComponent.Left).IsReported || Tile(BatteryComponent.Right).IsReported);
    }

    private const string UnknownSingleKey = ArtworkLook.UnknownSingle, UnknownPiecesKey = ArtworkLook.UnknownPieces;

    /// <summary>
    /// Which artwork to draw. A photo needs the profile's full photo; its part photos are used only with it and when each bud is reported.
    /// A choice that names no usable model (a removed profile, or one without a photo) acts as Automatic.
    /// </summary>
    public static ResolvedLook ResolveLook(string? choice, DeviceProfile? matched, IReadOnlyList<DeviceProfile> profiles, bool reportsBuds, bool looksLikeEarbuds)
    {
        // looksLikeEarbuds only picks the drawn silhouette inside UnknownSingle, which the view does from HasBuds.
        _ = looksLikeEarbuds;
        return ArtworkLook.Resolve(choice, matched, profiles, reportsBuds);
    }

    private void ApplyLook(bool reportsBuds)
    {
        var look = ResolveLook(_choice, _profile, _profiles, reportsBuds, LooksLikeEarbuds);
        var key = ArtworkLook.MenuKey(_choice, _profiles);
        if (!Equals(look, _look))
        {
            _look = look;
            var oldImage = ArtworkImage;
            var oldParts = ArtworkParts;
            ArtworkImage = LoadArtwork(look.Profile?.Image);
            if (look.Profile is not null && ArtworkImage is null)
            {
                look = _look = new ResolvedLook(reportsBuds ? LookKind.UnknownPieces : LookKind.UnknownSingle); // no photo to show
                ArtworkImage = null;
            }

            ArtworkParts = look.Kind == LookKind.PartsPhoto ? LoadArtworkParts(look.Profile!.PartImages) : [];
            if (look.Kind == LookKind.PartsPhoto && ArtworkParts.Count != 3)
            {
                look = _look = new ResolvedLook(LookKind.Photo, look.Profile); // a broken part photo keeps the whole photo
            }

            ShowArtworkParts = look.Kind == LookKind.PartsPhoto;
            ShowUnknownSingle = look.Kind == LookKind.UnknownSingle;
            ShowUnknownPieces = look.Kind == LookKind.UnknownPieces;
            DisposeArtwork(oldImage, oldParts); // after the bindings moved on to the new values
        }

        if (LookOptions.Count > 0 && LookOptions.Single(o => o.IsSelected).Key == key)
        {
            return;
        }

        LookOptions =
        [
            Option("auto", "Automatic", key),
            Option(UnknownSingleKey, "Unknown · single", key),
            Option(UnknownPiecesKey, "Unknown · separate pieces", key),
            .. _profiles.Where(p => p.Image is not null).OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(p => Option(p.Id, p.DisplayName, key)),
        ];
    }

    private static void DisposeArtwork(Avalonia.Media.Imaging.Bitmap? image, IReadOnlyList<ArtworkPartViewModel> parts)
    {
        image?.Dispose();
        foreach (var part in parts)
        {
            part.Image.Dispose();
        }
    }

    private LookOptionViewModel Option(string key, string label, string current) =>
        new(key, label, key == current, new RelayCommand(() => ChangeLook(key)));

    private IReadOnlyList<ArtworkPartViewModel> LoadArtworkParts(ProfilePartImages? images)
    {
        var bitmaps = images is null ? [] : new[] { images.Left, images.Right, images.Case }.Select(LoadArtwork).ToList();
        return bitmaps.Count == 3 && bitmaps.All(b => b is not null)
            ? bitmaps.Zip(Figures.Skip(1), (bitmap, figure) => new ArtworkPartViewModel(figure, bitmap!)).ToList()
            : [];
    }

    public string ArtworkLabel =>$"{Name} {(HasBuds ? "earbuds" : "headset")}";

    // shortcut: name keywords decide the form factor; add a formFactor field to device profiles when a model is misdrawn.
    private bool LooksLikeEarbuds =>
        new[] { "bud", "pods", "earphone", "tws" }.Any(k => $"{Name} {Subtitle}".Contains(k, StringComparison.OrdinalIgnoreCase));

    /// <summary>The four numbers of the battery panel: overall, left, right, case.</summary>
    public ObservableCollection<BatteryFigureViewModel> Figures { get; } =
        [new("Lowest bud"), new("Left bud"), new("Right bud"), new("Case")];

    /// <summary>The figures the battery panel shows: all four for earbuds, only the overall level otherwise.</summary>
    public ObservableCollection<BatteryFigureViewModel> ShownFigures { get; } = [];

    /// <summary>True when left, right or case is reported, so the battery panel shows four tiles instead of one.</summary>
    [ObservableProperty]
    public partial bool ShowsComponentFigures { get; set; }

    /// <summary>True when this device is the one shown in the workspace (false in Settings).</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>The device reports a left or right bud, so the artwork shows a pair of buds.</summary>
    [ObservableProperty]
    public partial bool HasBuds { get; set; }

    [ObservableProperty]
    public partial bool HasSummary { get; set; }

    [ObservableProperty]
    public partial string SummaryFigure { get; set; } = "–";

    [ObservableProperty]
    public partial string SummaryUnit { get; set; } = "";

    /// <summary>"78%", or "–" when the device reports no level.</summary>
    [ObservableProperty]
    public partial string SummaryShort { get; set; } = "–";

    /// <summary>Where the overall level comes from, e.g. "from Windows".</summary>
    [ObservableProperty]
    public partial string BatterySourceMeta { get; set; } = "";

    [ObservableProperty]
    public partial string BatteryAccessibleText { get; set; } = "Battery not reported";

    [ObservableProperty]
    public partial string RowAccessibleText { get; set; } = "";

    /// <summary>Connection and support tier as one line.</summary>
    [ObservableProperty]
    public partial string StatusLineText { get; set; } = "";

    /// <summary>One short sentence under the battery panel title: why some figures are "–".</summary>
    [ObservableProperty]
    public partial string BatteryReasonText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConnect), nameof(ShowDisconnect), nameof(LinkAccessibleName), nameof(LinkStateText), nameof(LinkDetailText))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(DisconnectCommand))]
    public partial bool IsConnected { get; set; }

    // Connect and disconnect from this computer. This type owns the single in-flight link operation;
    // the hero, the Links page and the tray all go through the two commands.
    private static readonly TimeSpan LinkTimeout = TimeSpan.FromSeconds(20);

    public bool HasPcLink => _pcLink is not null;

    public bool ShowConnect => HasPcLink && !IsConnected;

    public bool ShowDisconnect => HasPcLink && IsConnected;

    public bool ShowSettingsFallback => !HasPcLink;

    public string SettingsFallbackText => "Use Windows Bluetooth settings to connect this device.";

    public string LinkAccessibleName => IsConnected ? $"Disconnect {Name} from this computer" : $"Connect {Name} to this computer";

    /// <summary>The Links page headline for this computer: the state, or the request in flight.</summary>
    public string LinkStateText => IsLinkBusy
        ? IsConnected ? "Disconnecting…" : "Connecting…"
        : IsConnected ? "Connected" : "Not connected";

    /// <summary>One line under the headline: what the state means, or where to change it.</summary>
    public string LinkDetailText =>
        IsLinkBusy ? "Waiting for the earbuds to respond"
        : !HasPcLink ? "Connect or disconnect in Windows Bluetooth settings"
        : IsConnected ? "Battery, sound and controls work from here"
        : "Connect to see battery and change settings";

    /// <summary>Why Connect or Disconnect is unavailable right now; null when it isn't.</summary>
    public string? LinkDisabledReason => IsLinkBusy ? "Wait for the current request to finish" : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LinkStateText), nameof(LinkDetailText), nameof(LinkDisabledReason))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(DisconnectCommand))]
    public partial bool IsLinkBusy { get; set; }

    private bool CanConnect() => _pcLink is not null && !IsLinkBusy && !IsConnected;

    private bool CanDisconnect() => _pcLink is not null && !IsLinkBusy && IsConnected;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private Task ConnectAsync() => CanConnect() ? RunLinkAsync(_pcLink!.ConnectAsync) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private Task DisconnectAsync() => CanDisconnect() ? RunLinkAsync(_pcLink!.DisconnectAsync) : Task.CompletedTask;

    private async Task RunLinkAsync(Func<CancellationToken, Task<CommandResult>> command)
    {
        IsLinkBusy = true;
        Connections.ErrorText = null;
        try
        {
            var result = await CommandRunner.RunAsync(command, LinkTimeout, _time);
            if (!result.Succeeded)
            {
                var text = result.UserMessage!;
                Connections.ErrorText = text;
                _onMessage?.Invoke(text);
            }
        }
        finally
        {
            IsLinkBusy = false;
            Update();
        }
    }

    [RelayCommand]
    private void OpenBluetoothSettings()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _onMessage?.Invoke("Couldn't open Windows Bluetooth settings.");
        }
    }

    [ObservableProperty]
    public partial string ConnectionText { get; set; } = "";

    /// <summary>Short battery summary for the device switcher.</summary>
    [ObservableProperty]
    public partial string SummaryText { get; set; } = "";

    [ObservableProperty]
    public partial double SummaryPercent { get; set; }

    [ObservableProperty]
    public partial bool SummaryIsLow { get; set; }

    [ObservableProperty]
    public partial string SourceText { get; set; } = "";

    [ObservableProperty]
    public partial string LastUpdatedText { get; set; } = "–";

    /// <summary>False until some piece has reported a level, so "Updated …" never shows next to "Waiting for a reading".</summary>
    [ObservableProperty]
    public partial bool HasLastUpdate { get; set; }

    /// <summary>The most urgent battery problem on this device right now, if any.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlert))]
    public partial string? AlertText { get; set; }

    [ObservableProperty]
    public partial bool AlertIsCritical { get; set; }

    public bool HasAlert => AlertText is not null;

    [ObservableProperty]
    public partial bool HasNotReported { get; set; }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_battery is not null)
        {
            await _battery.RefreshAsync();
        }
    }

    public void UpdateAges()
    {
        var now = _time.GetUtcNow();
        foreach (var tile in Tiles)
        {
            tile.UpdateAge(now);
        }

        var updated = HeroTiles.FirstOrDefault(t => t.UpdatedText != "")?.UpdatedText.Replace("Updated ", "");
        LastUpdatedText = updated ?? "–";
        HasLastUpdate = updated is not null;
    }

    private BatteryTileViewModel Tile(BatteryComponent component) => Tiles.Single(t => t.Component == component);

    private void OnDeviceChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Update);

    private void OnBatteryChanged(object? sender, BatteryState state) => Dispatcher.UIThread.Post(Update);

    private void UpdateFigures(int? summary, bool buds)
    {
        var overall = Figures[0];
        overall.IsReported = HasSummary;
        overall.HasLevel = HasSummary;
        overall.Value = SummaryFigure;
        overall.Unit = SummaryUnit;
        overall.Percent = summary ?? 0;
        overall.IsLow = summary <= _alerts.Policy.LowThreshold && summary > _alerts.Policy.CriticalThreshold;
        overall.IsCritical = summary <= _alerts.Policy.CriticalThreshold;
        overall.Meta = HasSummary ? BatterySourceMeta : IsConnected ? "Not reported" : "Waiting for a reading";

        foreach (var (figure, component) in Figures.Skip(1).Zip([BatteryComponent.Left, BatteryComponent.Right, BatteryComponent.Case]))
        {
            var tile = Tile(component);
            figure.IsReported = tile.IsReported;
            figure.HasLevel = tile.HasLevel;
            figure.Value = tile.HasLevel ? ((int)tile.Percent).ToString() : "–";
            figure.Unit = tile.HasLevel ? "%" : "";
            figure.Percent = tile.Percent;
            figure.IsLow = tile.IsLow;
            figure.IsCritical = tile.IsCritical;
            figure.IsCharging = tile.IsCharging;
            figure.AccessibleText = !tile.IsReported ? $"{figure.Label}, not reported"
                : !tile.HasLevel ? $"{figure.Label}, no reading"
                : $"{figure.Label}, {(int)tile.Percent} percent{(tile.IsCharging ? ", charging" : tile.StateText == "Full" ? ", fully charged" : "")}";
            figure.Meta = !tile.IsReported
                ? "Not reported"
                : string.Join(" · ", new[] { tile.IsLow || tile.IsCritical ? tile.StateText : "", tile.DetailText }.Where(t => t.Length > 0));
        }

        // Collapse to Lowest bud only when no component (left, right or case) is reported.
        var components = Figures.Skip(1).Any(f => f.IsReported);
        var shown = components ? Figures.ToList() : [overall];
        ShowsComponentFigures = components;
        if (!ShownFigures.SequenceEqual(shown))
        {
            ShownFigures.Clear();
            foreach (var figure in shown)
            {
                ShownFigures.Add(figure);
            }
        }

        var missing = Figures.Skip(1).Where(f => !f.IsReported).Select(f => f.Label.ToLowerInvariant()).ToList();
        var noLevel = Figures.Skip(1).Where(f => f.IsReported && !f.HasLevel).Select(f => f.Label.ToLowerInvariant()).ToList();
        var notReported = missing.Count > 0 ? $"the {string.Join(" and ", missing)} {(missing.Count == 1 ? "isn't" : "aren't")} reported by this device" : null;
        var waiting = noLevel.Count > 0 ? $"No level from the {string.Join(" and ", noLevel)} yet" : null;
        BatteryReasonText = !HasSummary && !components
            ? IsConnected ? "This device reports no battery level" : "Connect the device to read its battery"
            : !components ? WaitsForDriver ? "Connect the earbuds to read each bud and the case" : "Left, right and case aren't reported for this device"
            : (waiting, notReported) switch
            {
                ({ } w, { } n) => $"{w}, and {n}",
                ({ } w, null) => w,
                (null, { } n) => char.ToUpperInvariant(n[0]) + n[1..],
                _ => "Each level is reported by the earbuds",
            };
    }

    private void Update()
    {
        var now = _time.GetUtcNow();
        var state = _battery?.Current ?? BatteryState.Empty;
        var capabilities = new CapabilitySet(BatteryCapabilities.For(_device));

        IsConnected = _device.Connection == ConnectionState.Connected;
        ConnectionText = _device.Connection switch
        {
            ConnectionState.Connected => "Connected",
            ConnectionState.Connecting => "Connecting…",
            _ => "Disconnected",
        };

        foreach (var tile in Tiles)
        {
            var capability = capabilities.Get(BatteryCapabilities.FeatureFor(tile.Component));
            tile.Update(capability, state.Get(tile.Component), _alerts.Policy, now);
        }

        // Buds layout when either bud is reported; otherwise a single gauge for the combined level.
        var buds = Tile(BatteryComponent.Left).IsReported || Tile(BatteryComponent.Right).IsReported;
        var hero = buds
            ? Tiles.Where(t => t.Component != BatteryComponent.Combined && t.IsReported).OrderBy(t => t.Component).ToList()
            : Tiles.Where(t => t.Component == BatteryComponent.Combined).ToList();
        Tile(BatteryComponent.Combined).IsVisible = !buds;
        Sync(HeroTiles, hero);
        Sync(NotReported, Tiles.Where(t => !t.IsReported && !hero.Contains(t) && !(buds && t.IsCombined)).ToList());
        HasNotReported = NotReported.Count > 0;

        var summary = state.SummaryPercent;
        SummaryText = summary is { } s ? $"{s}%" : IsConnected ? "?" : "–";
        SummaryPercent = summary ?? 0;
        SummaryIsLow = summary <= _alerts.Policy.LowThreshold;

        var urgent = HeroTiles.Where(t => t.IsCritical || t.IsLow).OrderBy(t => t.Percent).FirstOrDefault();
        AlertText = urgent is null ? null : $"{urgent.Label} {(urgent.IsCritical ? "critically low" : "low")} · {urgent.PercentText}";
        AlertIsCritical = urgent?.IsCritical == true;

        SourceText = _battery?.Source switch
        {
            CapabilitySource.OperatingSystem => "Windows (single level)",
            CapabilitySource.StandardService => "Bluetooth Battery Service",
            CapabilitySource.Simulator => "Simulator",
            CapabilitySource.Profile or CapabilitySource.RuntimeQuery => "Device protocol",
            _ => "Not reported",
        };

        HasBuds = buds || LooksLikeEarbuds;
        ApplyLook(buds);
        HasSummary = summary is not null;
        SummaryFigure = summary is { } figure ? figure.ToString() : "–";
        SummaryUnit = HasSummary ? "%" : "";
        SummaryShort = HasSummary ? $"{summary}%" : "–";
        BatterySourceMeta = !HasSummary ? (IsConnected ? "not reported" : "waiting for a reading") : _battery?.Source switch
        {
            CapabilitySource.OperatingSystem => "from Windows",
            CapabilitySource.StandardService => "from Bluetooth Battery Service",
            CapabilitySource.Simulator => "from simulator",
            CapabilitySource.Profile or CapabilitySource.RuntimeQuery => "from device protocol",
            _ => "not reported",
        };
        BatteryAccessibleText = HasSummary ? $"Battery {summary} percent" : "Battery not reported";
        RowAccessibleText = $"{Name}, {(HasSummary ? $"{summary}%" : "battery not reported")}, {ConnectionText}";
        StatusLineText = $"{ConnectionText} · {TierText}";
        OnPropertyChanged(nameof(TierText));
        OnPropertyChanged(nameof(TierDescription));
        UpdateFigures(summary, buds);

        UpdateAges();

        foreach (var alert in _alerts.Evaluate(_device.Identity.Id, state))
        {
            _onAlert(this, alert);
        }
    }

    /// <summary>Updates a collection in place only when its membership changed, so bound views don't flicker.</summary>
    private static void Sync(ObservableCollection<BatteryTileViewModel> target, IReadOnlyList<BatteryTileViewModel> items)
    {
        if (target.SequenceEqual(items))
        {
            return;
        }

        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    public void Dispose()
    {
        _device.ConnectionChanged -= OnDeviceChanged;
        DisposeArtwork(ArtworkImage, ArtworkParts);
        if (_battery is not null)
        {
            _battery.Changed -= OnBatteryChanged;
        }

        _alerts.Forget(_device.Identity.Id);
        NoiseControl.Dispose();
        Equalizer.Dispose();
        Connections.Dispose();
        Info.Dispose();
        Details.Dispose();
        PhoneMove?.Dispose();
    }
}

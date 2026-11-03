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
using Samaat.Core.Connections;
using Samaat.Core.Devices;
using Samaat.Core.NoiseControl;
using Samaat.Core.Profiles;

namespace Samaat.App.Features.Overview;

public sealed partial class DeviceViewModel : ObservableObject, IDisposable
{
    private readonly IEarbudDevice _device;
    private readonly IBatteryFeature? _battery;
    private readonly LowBatteryDetector _alerts;
    private readonly TimeProvider _time;
    private readonly Action<DeviceViewModel, BatteryAlert> _onAlert;

    private readonly DeviceProfile? _profile;

    public DeviceViewModel(IEarbudDevice device, LowBatteryDetector alerts, TimeProvider time, Action<DeviceViewModel, BatteryAlert> onAlert,
        DeviceProfile? profile = null)
    {
        _device = device;
        _profile = profile;
        ArtworkImage = LoadArtwork(profile?.Image);
        _battery = device.GetFeature<IBatteryFeature>();
        _alerts = alerts;
        _time = time;
        _onAlert = onAlert;
        Initials = string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
        if (Initials.Length == 0)
        {
            Initials = "?";
        }

        Tiles = new ObservableCollection<BatteryTileViewModel>(
            new[] { BatteryComponent.Combined, BatteryComponent.Left, BatteryComponent.Right, BatteryComponent.Case }
                .Select(c => new BatteryTileViewModel(c)));

        NoiseControl = new NoiseControlViewModel(device, time: time);
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
        _device.Identity.Manufacturer,
        // The profile names the model when Windows didn't report maker or model.
        _device.Identity.Manufacturer is null && _device.Identity.Model is null ? _profile?.DisplayName : _device.Identity.Model,
        _device.Identity.FirmwareVersion is { } fw ? $"Firmware {fw}" : null,
    }.Where(s => !string.IsNullOrWhiteSpace(s) && !Name.Contains(s!, StringComparison.OrdinalIgnoreCase))); // don't repeat the title

    public string TierText => CapabilityText.Tier(_device.Identity.Tier);

    public string TierDescription => _device.Identity.Tier switch
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

    public ConnectionsViewModel Connections { get; }

    public DeviceInfoViewModel Info { get; }

    /// <summary>Bluetooth IDs, address and services; separate from <see cref="Info"/>, the diagnostics page.</summary>
    public BluetoothDetailsViewModel Details { get; }

    /// <summary>Two letters for the collapsed sidebar rail.</summary>
    public string Initials { get; }

    /// <summary>The model's photo from its device profile; null draws the built-in earbud or headset artwork.</summary>
    public Avalonia.Media.Imaging.Bitmap? ArtworkImage { get; }

    public bool HasArtworkImage => ArtworkImage is not null;

    private static Avalonia.Media.Imaging.Bitmap? LoadArtwork(string? path)
    {
        try
        {
            return path is null ? null : new Avalonia.Media.Imaging.Bitmap(path);
        }
        catch (Exception)
        {
            return null; // a broken image file falls back to the drawn artwork
        }
    }

    public string ArtworkLabel =>$"{Name} {(HasBuds ? "earbuds" : "headset")}";

    // shortcut: name keywords decide the form factor; add a formFactor field to device profiles when a model is misdrawn.
    private bool LooksLikeEarbuds =>
        new[] { "bud", "pods", "earphone", "tws" }.Any(k => $"{Name} {Subtitle}".Contains(k, StringComparison.OrdinalIgnoreCase));

    /// <summary>The four numbers of the battery panel: overall, left, right, case.</summary>
    public ObservableCollection<BatteryFigureViewModel> Figures { get; } =
        [new("Overall"), new("Left bud"), new("Right bud"), new("Case")];

    /// <summary>The figures the battery panel shows: all four for earbuds, only the overall level otherwise.</summary>
    public ObservableCollection<BatteryFigureViewModel> ShownFigures { get; } = [];

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
    public partial bool IsConnected { get; set; }

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

        LastUpdatedText = HeroTiles.FirstOrDefault(t => t.UpdatedText != "")?.UpdatedText.Replace("Updated ", "") ?? "–";
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
            figure.Meta = !tile.IsReported
                ? "Not reported"
                : string.Join(" · ", new[] { tile.IsLow || tile.IsCritical ? tile.StateText : "", tile.DetailText }.Where(t => t.Length > 0));
        }

        // Collapse to Overall only when no component (left, right or case) is reported.
        var components = Figures.Skip(1).Any(f => f.IsReported);
        var shown = components ? Figures.ToList() : [overall];
        if (!ShownFigures.SequenceEqual(shown))
        {
            ShownFigures.Clear();
            foreach (var figure in shown)
            {
                ShownFigures.Add(figure);
            }
        }

        var missing = Figures.Skip(1).Where(f => !f.IsReported).Select(f => f.Label.ToLowerInvariant()).ToList();
        BatteryReasonText = !HasSummary && !components
            ? IsConnected ? "This device reports no battery level." : "Connect the device to read its battery."
            : components
                ? missing.Count > 0 ? $"The {string.Join(" and ", missing)} {(missing.Count == 1 ? "isn't" : "aren't")} reported by this device." : "Each level is reported by the earbuds."
                : "Left, right and case aren't reported for this device.";
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
        HasSummary = summary is not null;
        SummaryFigure = summary is { } figure ? figure.ToString() : "–";
        SummaryUnit = HasSummary ? "%" : "";
        SummaryShort = HasSummary ? $"{summary}%" : "–";
        BatterySourceMeta = _battery?.Source switch
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
        if (_battery is not null)
        {
            _battery.Changed -= OnBatteryChanged;
        }

        _alerts.Forget(_device.Identity.Id);
        NoiseControl.Dispose();
        Connections.Dispose();
        Info.Dispose();
    }
}

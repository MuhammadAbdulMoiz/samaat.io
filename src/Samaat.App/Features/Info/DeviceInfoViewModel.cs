using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Samaat.App.Shared;
using Samaat.Core.Battery;
using Samaat.Core.Bluetooth;
using Samaat.Core.Capabilities;
using Samaat.Core.Connections;
using Samaat.Core.Devices;
using Samaat.Core.Diagnostics;
using Samaat.Core.Drivers;
using Samaat.Core.NoiseControl;
using Samaat.Core.Profiles;

namespace Samaat.App.Features.Info;

public sealed record InfoRow(string Label, string Value);

public sealed record LedgerRow(string Feature, string Name, string State, string Source, bool Verified, string Access, bool IsEnabled);

public sealed record EventRow(string Time, string Text);

public sealed record CapabilityRow(string Name, string Meta)
{
    public string AccessibleName => $"{Name}, {Meta}";
}

/// <summary>Features of one module that can't be used, with the reason they share.</summary>
public sealed record UnavailableGroup(string Module, int Count, string Reason, IReadOnlyList<CapabilityRow> Items, bool Partial = false)
{
    // "· 5 more features" when some of the module works, so the row doesn't read as "noise control isn't supported".
    public string Title => $"{Module} · {Count} {(Partial ? "more " : "")}{(Count == 1 ? "feature" : "features")}";
}

/// <summary>
/// The per-device diagnostics page: what Samaat observed, how it matched the device, every
/// capability with its source, and a live event log. Its report is what hardware bug reports attach.
/// </summary>
public sealed partial class DeviceInfoViewModel : ObservableObject, IDisposable
{
    public const int MaxEvents = 50;

    private readonly IEarbudDevice _device;
    private readonly IDeviceDiagnostics? _diagnostics;
    private readonly IBatteryFeature? _battery;
    private readonly INoiseControlFeature? _noise;
    private readonly IMultipointFeature? _multipoint;
    private readonly DeviceProfile? _profile;
    private readonly Action<Action> _post;
    private readonly TimeProvider _time;
    private string? _lastBattery;

    /// <summary>How many log lines "Copy report" includes.</summary>
    public const int ReportLogLines = 40;

    private readonly Func<int, IReadOnlyList<string>>? _recentLog;

    /// <param name="recentLog">The newest log lines for the report; defaults to the installed log sink's, if it keeps any.</param>
    public DeviceInfoViewModel(IEarbudDevice device, Action<Action>? post = null, TimeProvider? time = null, DeviceProfile? profile = null,
        Func<int, IReadOnlyList<string>>? recentLog = null)
    {
        _recentLog = recentLog ?? (SamaatLog.Sink is IRecentLog log ? log.Tail : null);
        _device = device;
        _diagnostics = device.GetFeature<IDeviceDiagnostics>();
        _battery = device.GetFeature<IBatteryFeature>();
        _noise = device.GetFeature<INoiseControlFeature>();
        _multipoint = device.GetFeature<IMultipointFeature>();
        _post = post ?? (a => Dispatcher.UIThread.Post(a));
        _time = time ?? TimeProvider.System;

        var id = device.Identity;
        Identity =
        [
            new("Name", id.Name),
            new("Manufacturer", id.Manufacturer ?? "Unknown"),
            new("Model", id.Model ?? "Unknown"),
            new("Firmware", id.FirmwareVersion ?? "Not reported"),
            new("Support", id.Tier.ToString()),
            new("Device ID", id.Id),
        ];

        var signals = _diagnostics?.Signals;
        Signals = signals is null
            ? [new("Signals", "Not reported by this device source")]
            :
            [
                new("Name", signals.Name),
                new("Manufacturer", signals.Manufacturer ?? "–"),
                new("Class of device", signals.ClassOfDevice is { } cod ? $"0x{cod:X6}" : "–"),
                new("Vendor / product ID", signals.VendorId is { } vid ? $"0x{vid:X4} / 0x{signals.ProductId ?? 0:X4}" : "–"),
                new("Services", signals.Services.Count == 0 ? "–" : string.Join(", ", signals.Services)),
            ];

        ResolutionKind = _diagnostics?.Resolution.Kind ?? DriverResolutionKind.Generic;
        ResolutionText = _diagnostics?.Resolution.Description ?? "Unknown";
        VerifiedText = _diagnostics?.Resolution.Entry switch
        {
            null => "Not in the catalog",
            { Verified: true } => "Hardware-verified model",
            _ => "Recognised, not yet verified on hardware",
        };

        _profile = profile;
        var catalogued = _diagnostics?.Resolution is { Kind: DriverResolutionKind.Driver or DriverResolutionKind.KnownModelNoDriver, Entry: not null };
        IsRecognised = catalogued || profile is not null;
        RecognitionTitle = !IsRecognised
            ? "Not recognised"
            : $"Recognised as {(catalogued ? _diagnostics!.Resolution.Entry!.DisplayName : profile!.DisplayName)}";
        RecognitionMeta = catalogued
            ? ResolutionText
            : profile is null ? "Samaat adapter: none yet" : $"{Explain(profile.Match)} · Samaat adapter: {profile.Adapter ?? "none yet"}";

        var details = id.Details;
        var vendorId = signals?.VendorId ?? details?.VendorId;
        TechnicalRows =
        [
            new("Device ID", id.Id),
            new("Class of device", signals?.ClassOfDevice is { } classOfDevice ? $"0x{classOfDevice:X6}" : "–"),
            new("Vendor / product ID", vendorId is { } v ? $"0x{v:X4} / 0x{signals?.ProductId ?? details?.ProductId ?? 0:X4}" : "–"),
            new("Services", signals is { Services.Count: > 0 }
                ? string.Join(", ", BluetoothServices.Labels(signals.Services))
                : details is { Services.Count: > 0 } ? string.Join(", ", details.Services) : "–"),
        ];

        _device.ConnectionChanged += OnConnectionChanged;
        if (_battery is not null)
        {
            _battery.Changed += OnBatteryChanged;
        }

        if (_noise is not null)
        {
            _noise.Changed += OnNoiseChanged;
        }

        if (_multipoint is not null)
        {
            _multipoint.Changed += OnMultipointChanged;
        }

        RefreshLedger();
        if (_battery is not null)
        {
            _lastBattery = Describe(_battery.Current);
        }
    }

    /// <summary>"Recognised as X" when the driver catalog or the device profile database matched.</summary>
    public string RecognitionTitle { get; }

    /// <summary>How it matched, and whether Samaat has an adapter for it.</summary>
    public string RecognitionMeta { get; }

    public bool IsRecognised { get; }

    /// <summary>Device ID, class and vendor/product ID, and services as friendly labels; raw UUIDs stay in <see cref="Signals"/> and the report.</summary>
    public IReadOnlyList<InfoRow> TechnicalRows { get; }

    [ObservableProperty]
    public partial string SummaryText { get; set; } = "";

    /// <summary>Share of features that work, for the meter in the Diagnostics header.</summary>
    [ObservableProperty]
    public partial double AvailablePercent { get; set; }

    /// <summary>The support tier under the Diagnostics meter, which already shows the feature count.</summary>
    [ObservableProperty]
    public partial string SupportText { get; set; } = "";

    /// <summary>"14 of 15": the figure next to the meter.</summary>
    [ObservableProperty]
    public partial string FeatureCountText { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<CapabilityRow> AvailableRows { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<UnavailableGroup> UnavailableGroups { get; set; } = [];

    public IReadOnlyList<InfoRow> Identity { get; }

    public IReadOnlyList<InfoRow> Signals { get; }

    public DriverResolutionKind ResolutionKind { get; }

    public bool IsDriverMatch => ResolutionKind == DriverResolutionKind.Driver;

    public string ResolutionText { get; }

    public string VerifiedText { get; }

    public ObservableCollection<LedgerRow> Ledger { get; } = [];

    /// <summary>Newest first.</summary>
    public ObservableCollection<EventRow> Events { get; } = [];

    public string BuildReport()
    {
        var version = typeof(DeviceInfoViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Samaat {version} · {Environment.OSVersion} · {_time.GetUtcNow():u}");
        sb.AppendLine();
        foreach (var row in Identity)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"{row.Label}: {row.Value}");
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"Connection: {_device.Connection}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Resolution: {ResolutionText} ({VerifiedText})");
        if (_profile is not null)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Profile: {_profile.DisplayName} ({Explain(_profile.Match)})");
        }

        sb.AppendLine();
        foreach (var row in Signals)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Signal {row.Label}: {row.Value}");
        }

        sb.AppendLine();
        foreach (var row in Ledger)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"{row.Feature,-24} {row.State} [{row.Source}, {row.Access}{(row.Verified ? ", verified" : "")}]");
        }

        sb.AppendLine();
        foreach (var e in Events.Reverse())
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"{e.Time} {e.Text}");
        }

        AppendRecentLog(sb);
        return sb.ToString();
    }

    private void AppendRecentLog(StringBuilder sb)
    {
        if (_recentLog is null)
        {
            return;
        }

        var lines = _recentLog(ReportLogLines);
        sb.AppendLine();
        if (lines.Count == 0)
        {
            sb.AppendLine("Recent log: nothing logged");
            return;
        }

        sb.AppendLine("Recent log:");
        foreach (var line in lines)
        {
            sb.AppendLine(line);
        }
    }

    private static readonly (string Module, string Prefix)[] Modules =
        [("Battery", "battery."), ("Noise control", "noise."), ("Equalizer", "eq."), ("Connections", "connections.")];

    private static readonly FeatureId[] BudBatteries = [FeatureId.BatteryLeft, FeatureId.BatteryRight, FeatureId.BatteryCase];

    private static string Explain(ProfileMatch match)
    {
        var parts = new List<string>();
        if (match.SdpUuids.Count > 0)
        {
            parts.Add("services");
        }

        if (match.NamePattern is not null)
        {
            parts.Add("name");
        }

        if (match.Vid is not null || match.Pid is not null)
        {
            parts.Add("vendor/product ID");
        }

        return parts.Count == 0 ? "Matched by profile" : $"Matched by {(parts.Count > 1 ? $"{string.Join(", ", parts[..^1])} and {parts[^1]}" : parts[0])}";
    }

    private static string Reason(IEnumerable<Capability> capabilities) =>
        capabilities.Select(c => c.DisabledReason).Distinct().ToList() is [{ Length: > 0 } only] ? only : "Not available";

    private void RefreshLedger()
    {
        var all = DeviceCapabilities.For(_device).ToList();
        var available = all.Where(c => c.UiState == CapabilityUiState.Enabled).ToList();
        SummaryText = $"{available.Count} of {all.Count} features available · {CapabilityText.Tier(_device.Identity.Tier)}";
        AvailablePercent = all.Count == 0 ? 0 : 100.0 * available.Count / all.Count;
        FeatureCountText = $"{available.Count} of {all.Count}";
        SupportText = CapabilityText.Tier(_device.Identity.Tier);
        AvailableRows = available
            .Select(c => new CapabilityRow(CapabilityText.Name(c.Feature),
                $"{CapabilityText.Source(c.Source)} · {CapabilityText.Access(c.Access)}{(c.Verified == Verification.Verified ? " · verified" : "")}"))
            .ToList();

        var groups = new List<UnavailableGroup>();
        foreach (var (module, prefix) in Modules)
        {
            var missing = all.Where(c => c.UiState != CapabilityUiState.Enabled && c.Feature.Value.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            if (missing.Count == 0)
            {
                continue;
            }

            // Left, right and case batteries are one idea when none is reported.
            var buds = missing.Where(c => BudBatteries.Contains(c.Feature)).ToList();
            var collapse = buds.Count == BudBatteries.Length;
            var items = missing.Where(c => !collapse || !buds.Contains(c))
                .Select(c => new CapabilityRow(CapabilityText.Name(c.Feature), c.DisabledReason ?? "Not available")).ToList();
            if (collapse)
            {
                items.Add(new CapabilityRow("Left, right and case battery", Reason(buds)));
            }

            var partial = available.Any(c => c.Feature.Value.StartsWith(prefix, StringComparison.Ordinal));
            groups.Add(new UnavailableGroup(module, missing.Count, Reason(missing), items, partial));
        }

        UnavailableGroups = groups;

        Ledger.Clear();
        foreach (var c in all)
        {
            Ledger.Add(new LedgerRow(
                c.Feature.Value,
                CapabilityText.Name(c.Feature),
                CapabilityText.State(c),
                CapabilityText.Source(c.Source),
                c.Verified == Verification.Verified,
                CapabilityText.Access(c.Access),
                c.UiState == CapabilityUiState.Enabled));
        }
    }

    private void OnConnectionChanged(object? sender, EventArgs e) => _post(() =>
    {
        Log(_device.Connection == ConnectionState.Connected ? "Connected" : "Disconnected");
        RefreshLedger();
    });

    private void OnBatteryChanged(object? sender, BatteryState state) => _post(() =>
    {
        var text = Describe(state);
        if (text != _lastBattery)
        {
            _lastBattery = text;
            Log($"Battery: {text}");
        }
    });

    private void OnNoiseChanged(object? sender, NoiseControlState state) =>
        _post(() => Log($"Noise control: {NoiseControlPresentation.Label(state.Mode)}"));

    private void OnMultipointChanged(object? sender, MultipointState state) =>
        _post(() => Log($"Audio source: {state.ActiveHost?.Name ?? "none"}"));

    private void Log(string text)
    {
        Events.Insert(0, new EventRow(_time.GetLocalNow().ToString("HH:mm:ss", CultureInfo.InvariantCulture), text));
        while (Events.Count > MaxEvents)
        {
            Events.RemoveAt(Events.Count - 1);
        }
    }

    private static string Describe(BatteryState state) => string.Join(" · ", state.Readings
        .OrderBy(r => r.Component)
        .Select(r => $"{r.Component} {(r.Percent is { } p ? $"{p}%" : "?")}{(r.IsCharging ? " charging" : "")}"));

    public void Dispose()
    {
        _device.ConnectionChanged -= OnConnectionChanged;
        if (_battery is not null)
        {
            _battery.Changed -= OnBatteryChanged;
        }

        if (_noise is not null)
        {
            _noise.Changed -= OnNoiseChanged;
        }

        if (_multipoint is not null)
        {
            _multipoint.Changed -= OnMultipointChanged;
        }
    }
}

using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Samaat.App.Shared;
using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Connections;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Core.NoiseControl;

namespace Samaat.App.Features.Info;

public sealed record InfoRow(string Label, string Value);

public sealed record LedgerRow(string Feature, string Name, string State, string Source, bool Verified, string Access, bool IsEnabled);

public sealed record EventRow(string Time, string Text);

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
    private readonly Action<Action> _post;
    private readonly TimeProvider _time;
    private string? _lastBattery;

    public DeviceInfoViewModel(IEarbudDevice device, Action<Action>? post = null, TimeProvider? time = null)
    {
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
                new("Manufacturer", signals.Manufacturer ?? "—"),
                new("Class of device", signals.ClassOfDevice is { } cod ? $"0x{cod:X6}" : "—"),
                new("Vendor / product ID", signals.VendorId is { } vid ? $"0x{vid:X4} / 0x{signals.ProductId ?? 0:X4}" : "—"),
                new("Services", signals.Services.Count == 0 ? "—" : string.Join(", ", signals.Services)),
            ];

        ResolutionKind = _diagnostics?.Resolution.Kind ?? DriverResolutionKind.Generic;
        ResolutionText = _diagnostics?.Resolution.Description ?? "Unknown";
        VerifiedText = _diagnostics?.Resolution.Entry switch
        {
            null => "Not in the catalog",
            { Verified: true } => "Hardware-verified model",
            _ => "Recognised, not yet verified on hardware",
        };

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

        return sb.ToString();
    }

    private void RefreshLedger()
    {
        Ledger.Clear();
        foreach (var c in DeviceCapabilities.For(_device))
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
        .Select(r => $"{r.Component} {(r.Percent is { } p ? $"{p}%" : "?")}{(r.IsCharging ? " ⚡" : "")}"));

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

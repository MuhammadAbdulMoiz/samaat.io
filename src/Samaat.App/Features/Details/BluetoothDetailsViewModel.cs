using Samaat.App.Features.Overview;
using CommunityToolkit.Mvvm.ComponentModel;
using Samaat.Core.Bluetooth;
using Samaat.Core.Devices;
using Samaat.Core.Profiles;

namespace Samaat.App.Features.Details;

public sealed record DetailsRow(string Label, string Value)
{
    /// <summary>Identifiers worth pasting into a bug report or a search get a copy button.</summary>
    public bool CanCopy => Label is "Address" or "Vendor / product ID" && Value != "Not reported";

    public string CopyLabel => $"Copy {Label.ToLowerInvariant()}";
}

/// <summary>The Bluetooth details page for one device: what is known about it, and "Not reported" for what isn't.</summary>
public sealed partial class BluetoothDetailsViewModel : ObservableObject, IDisposable
{
    private const string NotReported = "Not reported";

    private readonly DeviceViewModel _device;
    private readonly DeviceProfile? _profile;
    private readonly IDeviceInfoFeature? _info;
    private readonly Action<Action> _post;
    private bool _disposed;

    /// <param name="post">Runs an action on the UI thread; feature events arrive on any thread.</param>
    public BluetoothDetailsViewModel(DeviceViewModel device, DeviceProfile? profile, Action<Action>? post = null)
    {
        _device = device;
        _profile = profile;
        _post = post ?? (action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
        _info = device.Device.GetFeature<IDeviceInfoFeature>();
        // Connection and battery source change over time; the rest is fixed for the device.
        device.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DeviceViewModel.ConnectionText) or nameof(DeviceViewModel.SourceText) or nameof(DeviceViewModel.TierText))
            {
                Rebuild();
            }
        };
        Rebuild();
        if (_info is not null)
        {
            _info.Changed += OnInfoChanged;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_info is not null)
        {
            _info.Changed -= OnInfoChanged;
        }
    }

    private void OnInfoChanged(object? sender, EventArgs e) => _post(() =>
    {
        if (!_disposed)
        {
            Rebuild();
        }
    });

    [ObservableProperty]
    public partial IReadOnlyList<DetailsRow> Rows { get; set; } = [];

    /// <summary>What the device is: name, model, firmware and vendor.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<DetailsRow> DeviceRows { get; set; } = [];

    /// <summary>How it is connected: address, services, connection and battery source.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<DetailsRow> ConnectionRows { get; set; } = [];

    public bool IsIdentified => _profile is not null;

    public string TierText => _device.TierText;

    public string TierDescription => _device.TierDescription;

    public string IdentifiedText => _profile is null ? "Not in the device database" : $"Identified as {_profile.DisplayName}";

    public string AdapterText => $"Vendor adapter: {_profile?.Adapter ?? "none yet"}";

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? NotReported : value;

    private void Rebuild()
    {
        var identity = _device.Device.Identity;
        var details = identity.Details;
        var model = _profile?.DisplayName
            ?? Or(string.Join(" ", new[] { identity.Manufacturer, identity.Model }.Where(s => !string.IsNullOrWhiteSpace(s))));
        var vendor = details?.VendorId is { } vid
            ? BluetoothVendors.Name(vid) is { } name ? $"{name} (0x{vid:X4})" : $"Unknown (0x{vid:X4})"
            : NotReported;
        var ids = details is { VendorId: { } v, ProductId: { } p } ? $"0x{v:X4} / 0x{p:X4}" : NotReported;

        DeviceRows =
        [
            new("Name", identity.Name),
            new("Model", model),
            new("Firmware", Or(_info?.FirmwareVersion ?? identity.FirmwareVersion)),
            new("Vendor", vendor),
            new("Vendor / product ID", ids),
        ];
        ConnectionRows =
        [
            new("Connection", Or(_device.ConnectionText)),
            new("Address", Or(details?.Address)),
            new("Services", details is { Services.Count: > 0 } ? string.Join(", ", details.Services) : NotReported),
            new("Battery source", Or(_device.SourceText)),
        ];
        Rows = [.. DeviceRows, new("Support", $"{_device.TierText}: {_device.TierDescription}"), .. ConnectionRows];
        OnPropertyChanged(nameof(TierText));
        OnPropertyChanged(nameof(TierDescription));
    }
}

using Samaat.App.Features.Overview;
using CommunityToolkit.Mvvm.ComponentModel;
using Samaat.Core.Bluetooth;
using Samaat.Core.Profiles;

namespace Samaat.App.Features.Details;

public sealed record DetailsRow(string Label, string Value);

/// <summary>The Bluetooth details page for one device: what is known about it, and "Not reported" for what isn't.</summary>
public sealed partial class BluetoothDetailsViewModel : ObservableObject
{
    private const string NotReported = "Not reported";

    private readonly DeviceViewModel _device;
    private readonly DeviceProfile? _profile;

    public BluetoothDetailsViewModel(DeviceViewModel device, DeviceProfile? profile)
    {
        _device = device;
        _profile = profile;
        // Connection and battery source change over time; the rest is fixed for the device.
        device.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DeviceViewModel.ConnectionText) or nameof(DeviceViewModel.SourceText))
            {
                Rebuild();
            }
        };
        Rebuild();
    }

    [ObservableProperty]
    public partial IReadOnlyList<DetailsRow> Rows { get; set; } = [];

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

        Rows =
        [
            new("Name", identity.Name),
            new("Model", model),
            new("Vendor", vendor),
            new("Vendor / product ID", ids),
            new("Address", Or(details?.Address)),
            new("Services", details is { Services.Count: > 0 } ? string.Join(", ", details.Services) : NotReported),
            new("Support", $"{_device.TierText}: {_device.TierDescription}"),
            new("Battery source", Or(_device.SourceText)),
            new("Connection", Or(_device.ConnectionText)),
        ];
    }
}

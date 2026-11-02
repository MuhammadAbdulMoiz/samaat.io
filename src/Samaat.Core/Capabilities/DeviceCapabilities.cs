using Samaat.Core.Battery;
using Samaat.Core.Connections;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Core.NoiseControl;

namespace Samaat.Core.Capabilities;

/// <summary>Every capability Samaat knows about for a device, across all modules.</summary>
public static class DeviceCapabilities
{
    public static IEnumerable<Capability> For(IEarbudDevice device)
    {
        var implemented = BatteryCapabilities.For(device)
            .Concat(NoiseControlCapabilities.For(device))
            .Concat(ConnectionCapabilities.For(device))
            .ToList();

        // A recognised model without a driver: the catalog knows more than the generic sources.
        if (device.GetFeature<IDeviceDiagnostics>()?.Resolution is { Kind: DriverResolutionKind.KnownModelNoDriver, Entry: { } entry })
        {
            var fromCatalog = CatalogCapabilities.For(entry, device.Connection).ToDictionary(c => c.Feature);
            return implemented.Select(c => c.Device == DeviceSupport.Unknown && fromCatalog.TryGetValue(c.Feature, out var known) ? known : c);
        }

        return implemented;
    }
}

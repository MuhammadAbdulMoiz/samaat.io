using Samaat.Core.Devices;
using Samaat.Transport;

namespace Samaat.Core.Drivers;

/// <summary>What a driver gets when it opens a device.</summary>
public sealed record DriverContext(DeviceSignals Signals, CatalogEntry Entry, IDeviceTransports Transports, TimeProvider Time);

/// <summary>
/// A vendor protocol family. It turns transports into feature modules (battery, noise control, ...).
/// The UI discovers what it can show from those features, so adding a driver needs no UI changes.
/// </summary>
public interface IEarbudDriver
{
    /// <summary>Matches <see cref="CatalogEntry.Driver"/>.</summary>
    string Id { get; }

    string DisplayName { get; }

    Task<IReadOnlyList<IFeature>> OpenAsync(DriverContext context, CancellationToken cancellationToken = default);
}

public enum DriverResolutionKind
{
    /// <summary>Not in the catalog: only standard and OS-level information.</summary>
    Generic,

    /// <summary>A known model whose driver isn't implemented and no protocol driver matches it.</summary>
    KnownModelNoDriver,

    /// <summary>A known model with its own driver, or with a matching protocol driver (generic entry) when it has none.</summary>
    Driver,
}

public sealed record DriverResolution(DriverResolutionKind Kind, CatalogEntry? Entry, IEarbudDriver? Driver)
{
    public string Description => Kind switch
    {
        DriverResolutionKind.Driver => $"{Entry!.DisplayName} · {Driver!.DisplayName} driver",
        DriverResolutionKind.KnownModelNoDriver => $"Recognised as {Entry!.DisplayName}; its Samaat adapter is not built yet",
        _ => "Not in Samaat's device catalog; using standard Bluetooth information",
    };
}

/// <summary>Picks the catalog entry and driver for a device.</summary>
public sealed class DriverRegistry(DeviceCatalog catalog)
{
    private readonly Dictionary<string, IEarbudDriver> _drivers = new(StringComparer.OrdinalIgnoreCase);

    public DeviceCatalog Catalog { get; } = catalog;

    public IReadOnlyCollection<IEarbudDriver> Drivers => _drivers.Values;

    public void Register(IEarbudDriver driver)
    {
        if (!_drivers.TryAdd(driver.Id, driver))
        {
            throw new ArgumentException($"A driver with id '{driver.Id}' is already registered.", nameof(driver));
        }
    }

    public DriverResolution Resolve(DeviceSignals signals)
    {
        var entry = Catalog.Match(signals);
        if (entry is null)
        {
            return new DriverResolution(DriverResolutionKind.Generic, null, null);
        }

        if (_drivers.TryGetValue(entry.Driver, out var driver))
        {
            return new DriverResolution(DriverResolutionKind.Driver, entry, driver);
        }

        // A model with no driver of its own still gets the best protocol driver its signals match; the model entry keeps the identity.
        var protocolDriver = Catalog.Entries
            .Where(e => e.Generic && _drivers.ContainsKey(e.Driver))
            .Select(e => (e.Driver, Score: e.Match.Score(signals)))
            .Where(x => x.Score is not null)
            .OrderByDescending(x => x.Score)
            .Select(x => _drivers[x.Driver])
            .FirstOrDefault();

        return protocolDriver is null
            ? new DriverResolution(DriverResolutionKind.KnownModelNoDriver, entry, null)
            : new DriverResolution(DriverResolutionKind.Driver, entry, protocolDriver);
    }
}

/// <summary>Optional feature every device source should provide: what was observed and how it was resolved.</summary>
public interface IDeviceDiagnostics : IFeature
{
    DeviceSignals Signals { get; }

    DriverResolution Resolution { get; }
}

public sealed record DeviceDiagnostics(DeviceSignals Signals, DriverResolution Resolution) : IDeviceDiagnostics;

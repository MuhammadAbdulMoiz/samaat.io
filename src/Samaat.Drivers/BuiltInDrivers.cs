using Samaat.Core.Drivers;
using Samaat.Protocols.Bose;
using Samaat.Protocols.FastPair;
using Samaat.Protocols.Jieli;
using Samaat.Protocols.Samsung;

namespace Samaat.Drivers;

/// <summary>
/// The drivers Samaat ships, in one place. A new vendor protocol is registered here and nowhere else:
/// every composition root (app, CLI, tests) builds its <see cref="DriverRegistry"/> through this class.
/// </summary>
public static class BuiltInDrivers
{
    /// <summary>A fresh instance of every shipped driver. Drivers hold no shared state, but each registry gets its own.</summary>
    public static IReadOnlyList<IEarbudDriver> Create() =>
    [
        new JieliDriver(),
        new FastPairDriver(),
        new BoseBmapDriver(),
        new SamsungDriver(),
    ];

    /// <summary>
    /// A registry with every shipped driver and the device catalog from <paramref name="devicesDirectory"/>
    /// (normally the <c>devices/</c> folder copied next to the executable). A missing folder gives an empty catalog.
    /// </summary>
    public static DriverRegistry CreateRegistry(string devicesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicesDirectory);

        var registry = new DriverRegistry(DeviceCatalog.FromDirectory(devicesDirectory));
        foreach (var driver in Create())
        {
            registry.Register(driver);
        }

        return registry;
    }
}

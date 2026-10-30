using Samaat.Core.Capabilities;
using Samaat.Core.Devices;

namespace Samaat.Core.Battery;

public interface IBatteryFeature : IFeature
{
    /// <summary>The components this feature reports.</summary>
    IReadOnlySet<BatteryComponent> Components { get; }

    /// <summary>
    /// True when <see cref="Components"/> is the device's complete list, so missing components are
    /// confirmed unsupported. Generic sources (OS, BLE Battery Service) can't know what a vendor
    /// protocol might expose, so they leave this false and missing components stay "unknown".
    /// </summary>
    bool ComponentsAreExhaustive { get; }

    CapabilitySource Source { get; }

    Verification Verification { get; }

    BatteryState Current { get; }

    /// <summary>Raised with the new snapshot whenever any component changes. May fire on any thread.</summary>
    event EventHandler<BatteryState>? Changed;

    /// <summary>Asks the device for fresh values. Push-based sources may treat this as a no-op.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

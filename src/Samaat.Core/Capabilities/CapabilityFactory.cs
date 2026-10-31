using Samaat.Core.Devices;

namespace Samaat.Core.Capabilities;

/// <summary>The shared rules that turn "what a feature reports" into a <see cref="Capability"/>.</summary>
public static class CapabilityFactory
{
    /// <param name="hasSource">Whether any feature module covers this area at all.</param>
    /// <param name="supported">Whether that module reports this particular capability.</param>
    /// <param name="exhaustive">Whether the module's list is complete, so absence means unsupported.</param>
    public static Capability Create(
        FeatureId id,
        bool hasSource,
        bool supported,
        bool exhaustive,
        ConnectionState connection,
        CapabilitySource source,
        Verification verification,
        CapabilityAccess access)
    {
        if (!hasSource)
        {
            return Capability.Unknown(id);
        }

        if (supported)
        {
            var runtime = connection == ConnectionState.Connected
                ? Availability.Available
                : Availability.Unavailable("Device disconnected");
            return new Capability(id, DeviceSupport.Supported, ImplementationStatus.Implemented, runtime, verification, access, source);
        }

        return exhaustive
            ? new Capability(id, DeviceSupport.Unsupported, ImplementationStatus.NotImplemented,
                Availability.Unavailable("Not supported by this model"), verification, access, source)
            : Capability.Unknown(id) with { Source = source, Access = access };
    }
}

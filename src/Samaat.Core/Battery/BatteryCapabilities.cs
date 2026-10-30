using Samaat.Core.Capabilities;
using Samaat.Core.Devices;

namespace Samaat.Core.Battery;

public static class BatteryCapabilities
{
    public static FeatureId FeatureFor(BatteryComponent component) => component switch
    {
        BatteryComponent.Combined => FeatureId.BatteryCombined,
        BatteryComponent.Left => FeatureId.BatteryLeft,
        BatteryComponent.Right => FeatureId.BatteryRight,
        BatteryComponent.Case => FeatureId.BatteryCase,
        _ => throw new ArgumentOutOfRangeException(nameof(component), component, null),
    };

    /// <summary>Builds one capability per battery component for a device.</summary>
    public static IEnumerable<Capability> For(IEarbudDevice device)
    {
        var feature = device.GetFeature<IBatteryFeature>();
        var runtime = device.Connection == ConnectionState.Connected
            ? Availability.Available
            : Availability.Unavailable("Device disconnected");

        foreach (var component in Enum.GetValues<BatteryComponent>())
        {
            var id = FeatureFor(component);
            if (feature is null)
            {
                yield return Capability.Unknown(id);
            }
            else if (feature.Components.Contains(component))
            {
                yield return new Capability(
                    id,
                    DeviceSupport.Supported,
                    ImplementationStatus.Implemented,
                    runtime,
                    feature.Verification,
                    CapabilityAccess.ReadOnly,
                    feature.Source);
            }
            else if (feature.ComponentsAreExhaustive)
            {
                yield return new Capability(
                    id,
                    DeviceSupport.Unsupported,
                    ImplementationStatus.NotImplemented,
                    Availability.Unavailable("Not supported by this model"),
                    feature.Verification,
                    CapabilityAccess.ReadOnly,
                    feature.Source);
            }
            else
            {
                yield return Capability.Unknown(id) with { Source = feature.Source };
            }
        }
    }
}

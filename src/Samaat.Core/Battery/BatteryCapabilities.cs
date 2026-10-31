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
        return Enum.GetValues<BatteryComponent>().Select(component => CapabilityFactory.Create(
            FeatureFor(component),
            hasSource: feature is not null,
            supported: feature?.Components.Contains(component) == true,
            exhaustive: feature?.ComponentsAreExhaustive == true,
            device.Connection,
            feature?.Source ?? CapabilitySource.Profile,
            feature?.Verification ?? Verification.Unverified,
            CapabilityAccess.ReadOnly));
    }
}

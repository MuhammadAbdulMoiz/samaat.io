namespace Samaat.Core.Capabilities;

/// <summary>Capabilities of one device. Features not present are reported as unknown, never unsupported.</summary>
public sealed class CapabilitySet
{
    private readonly Dictionary<FeatureId, Capability> _capabilities;

    public CapabilitySet(IEnumerable<Capability> capabilities)
    {
        _capabilities = capabilities.ToDictionary(c => c.Feature);
    }

    public static CapabilitySet Empty { get; } = new([]);

    public IReadOnlyCollection<Capability> All => _capabilities.Values;

    public Capability Get(FeatureId feature) =>
        _capabilities.TryGetValue(feature, out var capability) ? capability : Capability.Unknown(feature);
}

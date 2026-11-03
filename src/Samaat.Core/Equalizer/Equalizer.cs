using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Devices;

namespace Samaat.Core.Equalizer;

/// <summary>One of the device's built-in EQ presets. <see cref="Id"/> is the device's own number for it.</summary>
public sealed record EqPreset(int Id, string Name);

/// <summary>Device EQ stored on the buds. Presets only; custom bands aren't editable yet.</summary>
public interface IEqualizerFeature : IFeature
{
    /// <summary>The presets the device reported, in its order.</summary>
    IReadOnlyList<EqPreset> Presets { get; }

    CapabilitySource Source { get; }

    Verification Verification { get; }

    /// <summary>The active preset's id, or null when the device hasn't reported one.</summary>
    int? CurrentPresetId { get; }

    /// <summary>Raised with the new preset id whenever the device reports one, including changes made elsewhere. Any thread.</summary>
    event EventHandler<int?>? Changed;

    /// <summary>Completes once the device confirms (or refuses) the change.</summary>
    Task<CommandResult> SetPresetAsync(int presetId, CancellationToken cancellationToken = default);
}

public static class EqualizerCapabilities
{
    public static IEnumerable<Capability> For(IEarbudDevice device)
    {
        var feature = device.GetFeature<IEqualizerFeature>();
        yield return CapabilityFactory.Create(
            FeatureId.EqPresets,
            hasSource: feature is not null,
            supported: feature?.Presets.Count > 0,
            exhaustive: feature is not null,
            device.Connection,
            feature?.Source ?? CapabilitySource.Profile,
            feature?.Verification ?? Verification.Unverified,
            CapabilityAccess.ReadWrite);
    }
}

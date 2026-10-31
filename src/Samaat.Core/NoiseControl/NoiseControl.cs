using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Devices;

namespace Samaat.Core.NoiseControl;

public enum NoiseMode
{
    Off,
    NoiseCancelling,
    Transparency,
    Adaptive,
}

/// <summary>An inclusive range of device-defined steps, e.g. ANC strength 0–10.</summary>
public sealed record LevelRange(int Min, int Max)
{
    public bool Contains(int value) => value >= Min && value <= Max;
}

/// <summary>What a device's noise control can do. The UI is generated from this.</summary>
public sealed record NoiseControlOptions(
    IReadOnlyList<NoiseMode> Modes,
    LevelRange? AncLevel = null,
    LevelRange? AmbientLevel = null,
    bool WindReduction = false,
    bool ConversationAwareness = false)
{
    public bool Supports(NoiseMode mode) => Modes.Contains(mode);

    /// <summary>Returns null when the command is valid, otherwise the result to report.</summary>
    public CommandResult? ValidateMode(NoiseMode mode) =>
        Supports(mode) ? null : CommandResult.Unsupported($"{mode} isn't available on this device.");

    public CommandResult? ValidateAncLevel(int level) => ValidateLevel(AncLevel, level, "Noise cancelling strength");

    public CommandResult? ValidateAmbientLevel(int level) => ValidateLevel(AmbientLevel, level, "Ambient sound level");

    private static CommandResult? ValidateLevel(LevelRange? range, int level, string name) => range switch
    {
        null => CommandResult.Unsupported($"{name} can't be adjusted on this device."),
        _ when !range.Contains(level) => CommandResult.Rejected($"{name} must be between {range.Min} and {range.Max}."),
        _ => null,
    };
}

/// <summary>Current noise control settings. Null values aren't reported by the device.</summary>
public sealed record NoiseControlState(
    NoiseMode Mode,
    int? AncLevel = null,
    int? AmbientLevel = null,
    bool? WindReduction = null,
    bool? ConversationAwareness = null);

public interface INoiseControlFeature : IFeature
{
    NoiseControlOptions Options { get; }

    /// <summary>True when <see cref="Options"/> is the device's complete list (vendor adapter, simulator).</summary>
    bool OptionsAreExhaustive { get; }

    CapabilitySource Source { get; }

    Verification Verification { get; }

    NoiseControlState Current { get; }

    /// <summary>Raised whenever the device reports new settings, including changes made on the buds. Any thread.</summary>
    event EventHandler<NoiseControlState>? Changed;

    /// <summary>Each setter completes once the device confirms (or refuses) the change.</summary>
    Task<CommandResult> SetModeAsync(NoiseMode mode, CancellationToken cancellationToken = default);

    Task<CommandResult> SetAncLevelAsync(int level, CancellationToken cancellationToken = default);

    Task<CommandResult> SetAmbientLevelAsync(int level, CancellationToken cancellationToken = default);

    Task<CommandResult> SetWindReductionAsync(bool enabled, CancellationToken cancellationToken = default);

    Task<CommandResult> SetConversationAwarenessAsync(bool enabled, CancellationToken cancellationToken = default);
}

public static class NoiseControlCapabilities
{
    public static IEnumerable<Capability> For(IEarbudDevice device)
    {
        var feature = device.GetFeature<INoiseControlFeature>();
        var options = feature?.Options;

        Capability Make(FeatureId id, bool supported) => CapabilityFactory.Create(
            id,
            hasSource: feature is not null,
            supported,
            exhaustive: feature?.OptionsAreExhaustive == true,
            device.Connection,
            feature?.Source ?? CapabilitySource.Profile,
            feature?.Verification ?? Verification.Unverified,
            CapabilityAccess.ReadWrite);

        yield return Make(FeatureId.NoiseCancelling, options?.Supports(NoiseMode.NoiseCancelling) == true);
        yield return Make(FeatureId.Transparency, options?.Supports(NoiseMode.Transparency) == true);
        yield return Make(FeatureId.AdaptiveNoise, options?.Supports(NoiseMode.Adaptive) == true);
        yield return Make(FeatureId.AncLevel, options?.AncLevel is not null);
        yield return Make(FeatureId.AmbientLevel, options?.AmbientLevel is not null);
        yield return Make(FeatureId.WindReduction, options?.WindReduction == true);
        yield return Make(FeatureId.ConversationAwareness, options?.ConversationAwareness == true);
    }
}

public enum NoiseControlLayout
{
    /// <summary>No controllable modes: show the greyed-out card.</summary>
    None,

    /// <summary>Only off/on noise cancelling: a single switch.</summary>
    Toggle,

    /// <summary>Several modes: a segmented selector.</summary>
    Selector,
}

/// <summary>Turns options into UI decisions (docs/PLAN.md, Module 2 table).</summary>
public static class NoiseControlPresentation
{
    private static readonly NoiseMode[] CanonicalOrder =
        [NoiseMode.NoiseCancelling, NoiseMode.Adaptive, NoiseMode.Transparency, NoiseMode.Off];

    public static NoiseControlLayout LayoutFor(NoiseControlOptions? options)
    {
        var active = options?.Modes.Where(m => m != NoiseMode.Off).ToList() ?? [];
        return active switch
        {
            [] => NoiseControlLayout.None,
            [NoiseMode.NoiseCancelling] => NoiseControlLayout.Toggle,
            _ => NoiseControlLayout.Selector,
        };
    }

    public static IReadOnlyList<NoiseMode> OrderedModes(NoiseControlOptions options) =>
        CanonicalOrder.Where(options.Supports).ToList();

    public static bool ShowsAncLevel(NoiseControlOptions options, NoiseMode mode) =>
        options.AncLevel is not null && mode == NoiseMode.NoiseCancelling;

    public static bool ShowsAmbientLevel(NoiseControlOptions options, NoiseMode mode) =>
        options.AmbientLevel is not null && mode == NoiseMode.Transparency;

    public static string Label(NoiseMode mode) => mode switch
    {
        NoiseMode.NoiseCancelling => "Noise cancelling",
        NoiseMode.Adaptive => "Adaptive",
        NoiseMode.Transparency => "Transparency",
        _ => "Off",
    };

    public static string Description(NoiseMode mode) => mode switch
    {
        NoiseMode.NoiseCancelling => "Block out the world",
        NoiseMode.Adaptive => "Adjusts to your surroundings",
        NoiseMode.Transparency => "Hear what's around you",
        _ => "No processing",
    };
}

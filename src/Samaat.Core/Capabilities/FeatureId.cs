namespace Samaat.Core.Capabilities;

/// <summary>
/// Stable identifier of a feature, e.g. <c>battery.left</c>. String-backed so adapters and
/// device profiles can introduce new features without changing Core.
/// </summary>
public readonly record struct FeatureId(string Value)
{
    public static readonly FeatureId BatteryCombined = new("battery.combined");
    public static readonly FeatureId BatteryLeft = new("battery.left");
    public static readonly FeatureId BatteryRight = new("battery.right");
    public static readonly FeatureId BatteryCase = new("battery.case");

    public static readonly FeatureId NoiseCancelling = new("noise.anc");
    public static readonly FeatureId Transparency = new("noise.transparency");
    public static readonly FeatureId AdaptiveNoise = new("noise.adaptive");
    public static readonly FeatureId AncLevel = new("noise.ancLevel");
    public static readonly FeatureId AmbientLevel = new("noise.ambientLevel");
    public static readonly FeatureId WindReduction = new("noise.wind");
    public static readonly FeatureId ConversationAwareness = new("noise.conversation");

    public static readonly FeatureId ConnectedHosts = new("connections.hosts");
    public static readonly FeatureId SwitchHost = new("connections.switch");
    public static readonly FeatureId DisconnectHost = new("connections.disconnect");
    public static readonly FeatureId MultipointToggle = new("connections.multipoint");

    public static readonly FeatureId EqPresets = new("eq.presets");

    public override string ToString() => Value;
}

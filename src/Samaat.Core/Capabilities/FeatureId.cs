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

    public override string ToString() => Value;
}

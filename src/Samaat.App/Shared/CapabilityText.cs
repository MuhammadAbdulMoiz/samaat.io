using Samaat.Core.Capabilities;

namespace Samaat.App.Shared;

/// <summary>User-facing wording for capabilities, shared by Overview and Info.</summary>
public static class CapabilityText
{
    public static string Name(FeatureId feature) => feature.Value switch
    {
        "battery.left" => "Left bud battery",
        "battery.right" => "Right bud battery",
        "battery.case" => "Case battery",
        "battery.combined" => "Overall battery",
        "noise.anc" => "Noise cancelling",
        "noise.transparency" => "Transparency",
        "noise.adaptive" => "Adaptive mode",
        "noise.ancLevel" => "ANC strength",
        "noise.ambientLevel" => "Ambient level",
        "noise.wind" => "Wind reduction",
        "noise.conversation" => "Conversation awareness",
        "connections.hosts" => "Connected devices",
        "connections.switch" => "Switch audio source",
        "connections.disconnect" => "Disconnect a device",
        "connections.multipoint" => "Multipoint on/off",
        var other => other,
    };

    public static string State(Capability capability) =>
        capability.UiState == CapabilityUiState.Enabled ? "Available" : capability.DisabledReason ?? "";

    public static string Source(CapabilitySource source) => source switch
    {
        CapabilitySource.OperatingSystem => "Operating system",
        CapabilitySource.StandardService => "Standard service",
        CapabilitySource.RuntimeQuery => "Device query",
        CapabilitySource.Profile => "Catalog",
        _ => source.ToString(),
    };

    public static string Access(CapabilityAccess access) => access == CapabilityAccess.ReadWrite ? "Read / write" : "Read only";
}

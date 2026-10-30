namespace Samaat.Core.Capabilities;

/// <summary>Whether the physical device can do something, independent of Samaat.</summary>
public enum DeviceSupport
{
    Unknown,
    Supported,
    Unsupported,
}

/// <summary>Whether Samaat knows how to drive the feature on this device.</summary>
public enum ImplementationStatus
{
    NotImplemented,
    Implemented,
}

/// <summary>Whether the feature has been tested on real hardware with this firmware.</summary>
public enum Verification
{
    Unverified,
    Verified,
}

public enum CapabilityAccess
{
    ReadOnly,
    ReadWrite,
}

/// <summary>Where the knowledge about a capability came from.</summary>
public enum CapabilitySource
{
    Profile,
    RuntimeQuery,
    StandardService,
    OperatingSystem,
    Simulator,
}

/// <summary>Whether the feature can be used right now (e.g. not while disconnected).</summary>
public sealed record Availability(bool IsAvailable, string? Reason)
{
    public static Availability Available { get; } = new(true, null);

    public static Availability Unavailable(string reason) => new(false, reason);
}

/// <summary>How the UI should present a capability.</summary>
public enum CapabilityUiState
{
    /// <summary>Usable now.</summary>
    Enabled,

    /// <summary>Supported, but not usable at the moment or not implemented by Samaat yet.</summary>
    Disabled,

    /// <summary>Confirmed not supported by this model.</summary>
    Unsupported,

    /// <summary>Support could not be determined. Must not be presented as "unsupported".</summary>
    Undetected,
}

/// <summary>
/// One feature described on independent axes so the UI can always explain why a control
/// is disabled. See docs/PLAN.md §4.2.
/// </summary>
public sealed record Capability(
    FeatureId Feature,
    DeviceSupport Device,
    ImplementationStatus Implementation,
    Availability Runtime,
    Verification Verified,
    CapabilityAccess Access,
    CapabilitySource Source)
{
    public static Capability Unknown(FeatureId feature) => new(
        feature,
        DeviceSupport.Unknown,
        ImplementationStatus.NotImplemented,
        Availability.Unavailable("Couldn't detect"),
        Verification.Unverified,
        CapabilityAccess.ReadOnly,
        CapabilitySource.Profile);

    public CapabilityUiState UiState => Device switch
    {
        DeviceSupport.Unsupported => CapabilityUiState.Unsupported,
        DeviceSupport.Unknown => CapabilityUiState.Undetected,
        _ when Implementation == ImplementationStatus.NotImplemented => CapabilityUiState.Disabled,
        _ when !Runtime.IsAvailable => CapabilityUiState.Disabled,
        _ => CapabilityUiState.Enabled,
    };

    /// <summary>User-facing explanation for anything that isn't <see cref="CapabilityUiState.Enabled"/>.</summary>
    public string? DisabledReason => UiState switch
    {
        CapabilityUiState.Enabled => null,
        CapabilityUiState.Unsupported => "Not supported by this model",
        CapabilityUiState.Undetected => "Couldn't detect",
        _ when Implementation == ImplementationStatus.NotImplemented => "Supported by your device, not yet by Samaat",
        _ => Runtime.Reason ?? "Unavailable",
    };
}

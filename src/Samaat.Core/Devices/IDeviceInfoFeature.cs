namespace Samaat.Core.Devices;

/// <summary>Identity facts a device protocol reports at runtime, which Windows doesn't know.</summary>
public interface IDeviceInfoFeature : IFeature
{
    /// <summary>Null until the device has reported one.</summary>
    string? FirmwareVersion { get; }

    /// <summary>Raised when <see cref="FirmwareVersion"/> changes. May fire on any thread.</summary>
    event EventHandler? Changed;
}

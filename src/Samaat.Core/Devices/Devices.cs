namespace Samaat.Core.Devices;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
}

/// <summary>How much Samaat can do with a device. See docs/PLAN.md §2.</summary>
public enum SupportTier
{
    /// <summary>Any Bluetooth audio device: connection info and OS-reported battery.</summary>
    Basic,

    /// <summary>Uses standard Bluetooth services such as the BLE Battery Service.</summary>
    Standard,

    /// <summary>A vendor adapter is implemented for this model.</summary>
    Full,
}

public sealed record DeviceIdentity(
    string Id,
    string Name,
    string? Manufacturer = null,
    string? Model = null,
    string? FirmwareVersion = null,
    SupportTier Tier = SupportTier.Basic);

/// <summary>Marker for a capability module an adapter exposes (battery, noise control, ...).</summary>
public interface IFeature;

public interface IEarbudDevice
{
    DeviceIdentity Identity { get; }

    ConnectionState Connection { get; }

    /// <summary>The features this device and its adapter provide. Their presence is the capability set.</summary>
    IReadOnlyCollection<IFeature> Features { get; }

    event EventHandler? ConnectionChanged;
}

/// <summary>Produces devices: a platform's Bluetooth stack, the simulator, ...</summary>
public interface IDeviceSource : IAsyncDisposable
{
    string Name { get; }

    Task<IReadOnlyList<IEarbudDevice>> DiscoverAsync(CancellationToken cancellationToken = default);

    /// <summary>Raised when devices were added or removed and <see cref="DiscoverAsync"/> should be called again.</summary>
    event EventHandler? DevicesChanged;
}

public static class EarbudDeviceExtensions
{
    public static T? GetFeature<T>(this IEarbudDevice device) where T : class, IFeature =>
        device.Features.OfType<T>().FirstOrDefault();
}

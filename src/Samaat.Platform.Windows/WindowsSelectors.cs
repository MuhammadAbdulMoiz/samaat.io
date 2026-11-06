using Samaat.Core.Drivers;

namespace Samaat.Platform.Windows;

/// <summary>The device properties Samaat asks Windows for, and the AQS filters it queries with.</summary>
internal static class WindowsSelectors
{
    internal const string ContainerIdProperty = "System.Devices.Aep.ContainerId";
    internal const string ManufacturerProperty = "System.Devices.Aep.Manufacturer";
    internal const string ModelNameProperty = "System.Devices.Aep.ModelName";
    internal const string HardwareIdsProperty = "System.Devices.HardwareIds";

    /// <summary>Every PnP node of one physical device: Windows groups them by container ID.</summary>
    internal static string ContainerId(Guid containerId) => $"System.Devices.ContainerId:=\"{{{containerId}}}\"";

    internal static Guid? TryReadContainerId(IReadOnlyDictionary<string, object> properties) =>
        properties.TryGetValue(ContainerIdProperty, out var value) && value is Guid id ? id : null;

    internal static string? TryReadString(IReadOnlyDictionary<string, object> properties, string key) =>
        properties.TryGetValue(key, out var value) ? value as string : null;
}

/// <summary>Turns what Windows reports about a paired device into the <see cref="DeviceSignals"/> drivers match on.</summary>
internal static class WindowsSignals
{
    /// <param name="endpointName">The association endpoint's name; the user can rename it, so it wins when set.</param>
    /// <param name="bluetoothName">The name the device itself reports, used when the endpoint has none.</param>
    internal static DeviceSignals Create(
        string? endpointName,
        string bluetoothName,
        uint classOfDevice,
        (int Vid, int Pid)? ids,
        IReadOnlySet<Guid> serviceUuids,
        IReadOnlyDictionary<string, object> endpointProperties) =>
        new(
            string.IsNullOrWhiteSpace(endpointName) ? bluetoothName : endpointName,
            classOfDevice,
            (ushort?)ids?.Vid,
            (ushort?)ids?.Pid,
            serviceUuids,
            WindowsSelectors.TryReadString(endpointProperties, WindowsSelectors.ManufacturerProperty));
}

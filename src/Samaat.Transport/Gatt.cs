namespace Samaat.Transport;

/// <summary>
/// Minimal BLE GATT client. Platforms implement it over their Bluetooth stack; protocol code only
/// sees bytes, so it can be tested with fakes and recorded traces.
/// </summary>
public interface IGattClient
{
    Task<IReadOnlyList<IGattService>> GetServicesAsync(Guid serviceUuid, CancellationToken cancellationToken = default);
}

public interface IGattService
{
    Guid Uuid { get; }

    Task<IReadOnlyList<IGattCharacteristic>> GetCharacteristicsAsync(Guid characteristicUuid, CancellationToken cancellationToken = default);
}

public interface IGattCharacteristic
{
    Guid Uuid { get; }

    bool CanRead { get; }

    bool CanNotify { get; }

    bool CanWrite { get; }

    /// <summary>Returns false if the write failed or the characteristic can't be written.</summary>
    Task<bool> WriteAsync(ReadOnlyMemory<byte> data, bool withoutResponse, CancellationToken cancellationToken = default);

    /// <summary>Returns null if the read failed.</summary>
    Task<byte[]?> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns null if the descriptor is absent or the read failed.</summary>
    Task<byte[]?> ReadDescriptorAsync(Guid descriptorUuid, CancellationToken cancellationToken = default);

    /// <summary>Enables notifications. Dispose the result to unsubscribe.</summary>
    Task<IAsyncDisposable> SubscribeAsync(Action<byte[]> onValue, CancellationToken cancellationToken = default);
}

public static class BluetoothUuid
{
    private const string BaseSuffix = "-0000-1000-8000-00805f9b34fb";

    /// <summary>Expands a 16-bit SIG-assigned UUID onto the Bluetooth base UUID.</summary>
    public static Guid FromShort(ushort value) => Guid.Parse($"0000{value:x4}{BaseSuffix}");
}

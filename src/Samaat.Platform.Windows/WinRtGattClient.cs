using Samaat.Transport;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace Samaat.Platform.Windows;

/// <summary><see cref="IGattClient"/> over WinRT's <see cref="BluetoothLEDevice"/>.</summary>
internal sealed class WinRtGattClient(BluetoothLEDevice device) : IGattClient, IDisposable
{
    private readonly List<GattDeviceService> _services = [];

    public async Task<IReadOnlyList<IGattService>> GetServicesAsync(Guid serviceUuid, CancellationToken cancellationToken = default)
    {
        var result = await device.GetGattServicesForUuidAsync(serviceUuid, BluetoothCacheMode.Uncached).AsTask(cancellationToken);
        if (result.Status != GattCommunicationStatus.Success)
        {
            return [];
        }

        _services.AddRange(result.Services);
        return result.Services.Select(s => (IGattService)new Service(s)).ToList();
    }

    public void Dispose()
    {
        foreach (var service in _services)
        {
            service.Dispose();
        }

        _services.Clear();
        device.Dispose();
    }

    internal static byte[] ToArray(IBuffer buffer)
    {
        var bytes = new byte[buffer.Length];
        using var reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private sealed class Service(GattDeviceService service) : IGattService
    {
        public Guid Uuid => service.Uuid;

        public async Task<IReadOnlyList<IGattCharacteristic>> GetCharacteristicsAsync(Guid characteristicUuid, CancellationToken cancellationToken = default)
        {
            var result = await service.GetCharacteristicsForUuidAsync(characteristicUuid, BluetoothCacheMode.Uncached).AsTask(cancellationToken);
            return result.Status == GattCommunicationStatus.Success
                ? result.Characteristics.Select(c => (IGattCharacteristic)new Characteristic(c)).ToList()
                : [];
        }
    }

    private sealed class Characteristic(GattCharacteristic characteristic) : IGattCharacteristic
    {
        public Guid Uuid => characteristic.Uuid;

        public bool CanRead => characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read);

        public bool CanNotify => characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify);

        public async Task<byte[]?> ReadAsync(CancellationToken cancellationToken = default)
        {
            var result = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
            return result.Status == GattCommunicationStatus.Success ? ToArray(result.Value) : null;
        }

        public async Task<byte[]?> ReadDescriptorAsync(Guid descriptorUuid, CancellationToken cancellationToken = default)
        {
            var descriptors = await characteristic.GetDescriptorsForUuidAsync(descriptorUuid, BluetoothCacheMode.Uncached).AsTask(cancellationToken);
            if (descriptors.Status != GattCommunicationStatus.Success || descriptors.Descriptors.Count == 0)
            {
                return null;
            }

            var result = await descriptors.Descriptors[0].ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
            return result.Status == GattCommunicationStatus.Success ? ToArray(result.Value) : null;
        }

        public async Task<IAsyncDisposable> SubscribeAsync(Action<byte[]> onValue, CancellationToken cancellationToken = default)
        {
            void Handler(GattCharacteristic sender, GattValueChangedEventArgs args) => onValue(ToArray(args.CharacteristicValue));

            characteristic.ValueChanged += Handler;
            await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(cancellationToken);
            return new Subscription(characteristic, Handler);
        }
    }

    private sealed class Subscription(
        GattCharacteristic characteristic,
        global::Windows.Foundation.TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            characteristic.ValueChanged -= handler;
            try
            {
                await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.None);
            }
            catch (Exception)
            {
                // The device may already be gone; unsubscribing is best effort.
            }
        }
    }
}

using System.Collections.Concurrent;
using Android.Bluetooth;
using Android.Content;
using Samaat.Transport;

namespace Samaat.Platform.Android;

/// <summary>
/// <see cref="IGattClient"/> over Android's <see cref="BluetoothGatt"/>. Android allows one GATT operation at a time, so
/// every read and write goes through one queue and waits for its callback. Connects lazily on first use. Unverified on hardware.
/// </summary>
internal sealed class AndroidGattClient : BluetoothGattCallback, IGattClient
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(5);

    /// <summary>BluetoothStatusCodes.SUCCESS, returned by the Android 13+ write calls.</summary>
    private const int BluetoothStatusSuccess = 0;
    private static readonly Java.Util.UUID ClientConfig = Java.Util.UUID.FromString("00002902-0000-1000-8000-00805f9b34fb")!;

    private readonly Context _context;
    private readonly BluetoothDevice _device;
    private readonly SemaphoreSlim _queue = new(1, 1);
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly ConcurrentDictionary<string, Action<byte[]>> _subscribers = new();
    private BluetoothGatt? _gatt;
    private TaskCompletionSource<bool>? _ready;
    private TaskCompletionSource<(GattStatus Status, byte[]? Value)>? _pending;

    public AndroidGattClient(Context context, BluetoothDevice device)
    {
        _context = context;
        _device = device;
    }

    public async Task<IReadOnlyList<IGattService>> GetServicesAsync(Guid serviceUuid, CancellationToken cancellationToken = default)
    {
        var gatt = await EnsureReadyAsync(cancellationToken);
        if (gatt?.Services is not { } services)
        {
            return [];
        }

        return [.. services
            .Where(s => s.Uuid is { } u && Guid.Parse(u.ToString()) == serviceUuid)
            .Select(s => (IGattService)new Service(this, s))];
    }

    private async Task<BluetoothGatt?> EnsureReadyAsync(CancellationToken ct)
    {
        await _connectLock.WaitAsync(ct);
        try
        {
            if (_gatt is not null && _ready is { Task.IsCompletedSuccessfully: true } && _ready.Task.Result)
            {
                return _gatt;
            }

            _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _gatt?.Close();
            _gatt = _device.ConnectGatt(_context, false, this, BluetoothTransports.Le);
            if (_gatt is null)
            {
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            return await _ready.Task.WaitAsync(timeout.Token) ? _gatt : null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <summary>Runs one GATT operation: <paramref name="start"/> returns false when Android refused to start it.</summary>
    private async Task<(GattStatus Status, byte[]? Value)?> RunAsync(Func<BluetoothGatt, bool> start, CancellationToken ct)
    {
        var gatt = await EnsureReadyAsync(ct);
        if (gatt is null)
        {
            return null;
        }

        await _queue.WaitAsync(ct);
        try
        {
            var pending = new TaskCompletionSource<(GattStatus, byte[]?)>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = pending;
            if (!start(gatt))
            {
                return null;
            }

            return await pending.Task.WaitAsync(OperationTimeout, ct);
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            _pending = null;
            _queue.Release();
        }
    }

    private void Complete(GattStatus status, byte[]? value) => _pending?.TrySetResult((status, value));

    private static string Key(BluetoothGattCharacteristic c) => $"{c.Service?.Uuid}/{c.Uuid}/{c.InstanceId}";

    public override void OnConnectionStateChange(BluetoothGatt? gatt, GattStatus status, ProfileState newState)
    {
        if (newState == ProfileState.Connected && status == GattStatus.Success)
        {
            gatt?.DiscoverServices();
        }
        else if (newState == ProfileState.Disconnected)
        {
            _ready?.TrySetResult(false);
            _pending?.TrySetResult((GattStatus.Failure, null));
        }
    }

    public override void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status) => _ready?.TrySetResult(status == GattStatus.Success);

    // Android 13+ passes values to these overloads; older versions call the deprecated ones below.
    public override void OnCharacteristicRead(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, byte[] value, GattStatus status) =>
        Complete(status, value);

    public override void OnCharacteristicRead(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic, GattStatus status) =>
        Complete(status, characteristic?.GetValue());

    public override void OnCharacteristicWrite(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic, GattStatus status) =>
        Complete(status, null);

    public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, byte[] value) =>
        Notify(characteristic, value);

    public override void OnCharacteristicChanged(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic)
    {
        if (characteristic?.GetValue() is { } value)
        {
            Notify(characteristic, value);
        }
    }

    public override void OnDescriptorRead(BluetoothGatt gatt, BluetoothGattDescriptor descriptor, GattStatus status, byte[] value) =>
        Complete(status, value);

    public override void OnDescriptorRead(BluetoothGatt? gatt, BluetoothGattDescriptor? descriptor, GattStatus status) =>
        Complete(status, descriptor?.GetValue());

    public override void OnDescriptorWrite(BluetoothGatt? gatt, BluetoothGattDescriptor? descriptor, GattStatus status) =>
        Complete(status, null);

    private void Notify(BluetoothGattCharacteristic characteristic, byte[] value)
    {
        if (_subscribers.TryGetValue(Key(characteristic), out var handler))
        {
            handler(value);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _subscribers.Clear();
            try
            {
                _gatt?.Disconnect();
                _gatt?.Close();
            }
            catch (Java.Lang.Exception)
            {
                // Already gone.
            }

            _gatt = null;
        }

        base.Dispose(disposing);
    }

    private sealed class Service(AndroidGattClient client, BluetoothGattService service) : IGattService
    {
        public Guid Uuid { get; } = Guid.Parse(service.Uuid!.ToString());

        public Task<IReadOnlyList<IGattCharacteristic>> GetCharacteristicsAsync(Guid characteristicUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IGattCharacteristic>>([.. (service.Characteristics ?? [])
                .Where(c => c.Uuid is { } u && Guid.Parse(u.ToString()) == characteristicUuid)
                .Select(c => (IGattCharacteristic)new Characteristic(client, c))]);
    }

    private sealed class Characteristic(AndroidGattClient client, BluetoothGattCharacteristic characteristic) : IGattCharacteristic
    {
        public Guid Uuid { get; } = Guid.Parse(characteristic.Uuid!.ToString());

        public bool CanRead => characteristic.Properties.HasFlag(GattProperty.Read);

        public bool CanNotify => characteristic.Properties.HasFlag(GattProperty.Notify) || characteristic.Properties.HasFlag(GattProperty.Indicate);

        public bool CanWrite => characteristic.Properties.HasFlag(GattProperty.Write) || characteristic.Properties.HasFlag(GattProperty.WriteNoResponse);

        public async Task<bool> WriteAsync(ReadOnlyMemory<byte> data, bool withoutResponse, CancellationToken cancellationToken = default)
        {
            if (!CanWrite)
            {
                return false;
            }

            var bytes = data.ToArray();
            var type = withoutResponse ? GattWriteType.NoResponse : GattWriteType.Default;
            var result = await client.RunAsync(gatt =>
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(33))
                {
                    return gatt.WriteCharacteristic(characteristic, bytes, (int)type) == BluetoothStatusSuccess;
                }

                characteristic.WriteType = type;
                characteristic.SetValue(bytes);
                return gatt.WriteCharacteristic(characteristic);
            }, cancellationToken);
            return result?.Status == GattStatus.Success;
        }

        public async Task<byte[]?> ReadAsync(CancellationToken cancellationToken = default)
        {
            var result = await client.RunAsync(gatt => gatt.ReadCharacteristic(characteristic), cancellationToken);
            return result is { Status: GattStatus.Success } r ? r.Value ?? [] : null;
        }

        public async Task<byte[]?> ReadDescriptorAsync(Guid descriptorUuid, CancellationToken cancellationToken = default)
        {
            var descriptor = characteristic.GetDescriptor(Java.Util.UUID.FromString(descriptorUuid.ToString()));
            if (descriptor is null)
            {
                return null;
            }

            var result = await client.RunAsync(gatt => gatt.ReadDescriptor(descriptor), cancellationToken);
            return result is { Status: GattStatus.Success } r ? r.Value ?? [] : null;
        }

        public async Task<IAsyncDisposable> SubscribeAsync(Action<byte[]> onValue, CancellationToken cancellationToken = default)
        {
            var key = Key(characteristic);
            client._subscribers[key] = onValue;
            await SetNotificationsAsync(true, cancellationToken);
            return new Subscription(async () =>
            {
                client._subscribers.TryRemove(key, out _);
                await SetNotificationsAsync(false, CancellationToken.None);
            });
        }

        private async Task SetNotificationsAsync(bool enabled, CancellationToken ct)
        {
            var gatt = await client.EnsureReadyAsync(ct);
            if (gatt is null || !gatt.SetCharacteristicNotification(characteristic, enabled))
            {
                return;
            }

            if (characteristic.GetDescriptor(ClientConfig) is not { } config)
            {
                return;
            }

            var value = !enabled ? BluetoothGattDescriptor.DisableNotificationValue
                : characteristic.Properties.HasFlag(GattProperty.Notify) ? BluetoothGattDescriptor.EnableNotificationValue
                : BluetoothGattDescriptor.EnableIndicationValue;
            var bytes = value?.ToArray() ?? [];
            await client.RunAsync(g =>
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(33))
                {
                    return g.WriteDescriptor(config, bytes) == BluetoothStatusSuccess;
                }

                config.SetValue(bytes);
                return g.WriteDescriptor(config);
            }, ct);
        }
    }

    private sealed class Subscription(Func<Task> unsubscribe) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await unsubscribe();
    }
}

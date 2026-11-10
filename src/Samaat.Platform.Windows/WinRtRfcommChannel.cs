using Samaat.Core.Diagnostics;
using Samaat.Transport;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Samaat.Platform.Windows;

/// <summary><see cref="IRfcommChannel"/> over a WinRT <see cref="StreamSocket"/>. Unverified on hardware.</summary>
internal sealed class WinRtRfcommChannel : IRfcommChannel
{
    private const uint ReadChunk = 1024;

    private readonly StreamSocket _socket;
    private readonly DataWriter _writer;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly RfcommReceiveBuffer _received;
    private int _closed;
    private int _disposed;

    private WinRtRfcommChannel(Guid serviceUuid, StreamSocket socket)
    {
        _received = new RfcommReceiveBuffer(this);
        ServiceUuid = serviceUuid;
        _socket = socket;
        _writer = new DataWriter(socket.OutputStream);
        _ = ReadLoopAsync();
    }

    public Guid ServiceUuid { get; }

    public bool IsClosed => Volatile.Read(ref _closed) == 1;

    public event EventHandler<ReadOnlyMemory<byte>>? Received
    {
        add => _received.Add(value!);
        remove => _received.Remove(value!);
    }

    public event EventHandler? Closed;

    public static async Task<WinRtRfcommChannel?> OpenAsync(BluetoothDevice device, Guid serviceUuid, CancellationToken cancellationToken)
    {
        var services = await device.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(serviceUuid), BluetoothCacheMode.Uncached)
            .AsTask(cancellationToken);
        if (services.Error != BluetoothError.Success || services.Services.Count == 0)
        {
            return null;
        }

        var service = services.Services[0];
        var socket = new StreamSocket();
        try
        {
            await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName).AsTask(cancellationToken);
        }
        catch
        {
            socket.Dispose();
            service.Dispose();
            throw;
        }

        return new WinRtRfcommChannel(serviceUuid, socket);
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            _writer.WriteBytes(data.ToArray());
            await _writer.StoreAsync().AsTask(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            using var reader = new DataReader(_socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
            while (!_stop.IsCancellationRequested)
            {
                var count = await reader.LoadAsync(ReadChunk).AsTask(_stop.Token);
                if (count == 0)
                {
                    break;
                }

                var buffer = new byte[count];
                reader.ReadBytes(buffer);
                _received.Publish(buffer);
            }
        }
        catch (Exception ex)
        {
            // Link dropped or channel disposed (reported through Closed); a throwing Received handler lands here too.
            if (ex is not OperationCanceledException && !_stop.IsCancellationRequested)
            {
                SamaatLog.Warn("RFCOMM", "Read loop ended", ex);
            }
        }
        finally
        {
            RaiseClosed();
        }
    }

    private void RaiseClosed()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return ValueTask.CompletedTask;
        }

        _stop.Cancel();
        _writer.DetachStream();
        _writer.Dispose();
        _socket.Dispose();
        _stop.Dispose();
        // _writeLock is left undisposed: a WriteAsync in flight must still be able to Release() it.
        RaiseClosed();
        return ValueTask.CompletedTask;
    }
}

internal sealed class WinRtDeviceTransports(BluetoothDevice device, Samaat.Transport.IGattClient? gatt) : IDeviceTransports
{
    public Samaat.Transport.IGattClient? Gatt { get; } = gatt;

    public async Task<IRfcommChannel?> OpenRfcommAsync(Guid serviceUuid, CancellationToken cancellationToken = default) =>
        await WinRtRfcommChannel.OpenAsync(device, serviceUuid, cancellationToken);

    public Task<IRfcommChannel?> OpenRfcommChannelAsync(byte channel, CancellationToken cancellationToken = default) =>
        WinsockRfcommChannel.OpenAsync(device.BluetoothAddress, channel, cancellationToken);
}

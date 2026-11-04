using Android.Bluetooth;
using Samaat.Transport;

namespace Samaat.Platform.Android;

/// <summary><see cref="IRfcommChannel"/> over an Android <see cref="BluetoothSocket"/>. Unverified on hardware.</summary>
internal sealed class AndroidRfcommChannel : IRfcommChannel
{
    private const int ReadChunk = 1024;

    private readonly BluetoothSocket _socket;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly RfcommReceiveBuffer _received;
    private int _closed;

    private AndroidRfcommChannel(Guid serviceUuid, BluetoothSocket socket)
    {
        _received = new RfcommReceiveBuffer(this);
        ServiceUuid = serviceUuid;
        _socket = socket;
        _input = socket.InputStream ?? throw new IOException("The RFCOMM socket has no input stream.");
        _output = socket.OutputStream ?? throw new IOException("The RFCOMM socket has no output stream.");
        _ = Task.Run(ReadLoopAsync);
    }

    public Guid ServiceUuid { get; }

    public bool IsClosed => Volatile.Read(ref _closed) == 1;

    public event EventHandler<ReadOnlyMemory<byte>>? Received
    {
        add => _received.Add(value!);
        remove => _received.Remove(value!);
    }

    public event EventHandler? Closed;

    /// <summary>Null when the device doesn't offer the service or the connection fails.</summary>
    public static async Task<IRfcommChannel?> OpenAsync(BluetoothDevice device, Guid serviceUuid, CancellationToken cancellationToken)
    {
        BluetoothSocket? socket = null;
        try
        {
            socket = device.CreateRfcommSocketToServiceRecord(Java.Util.UUID.FromString(serviceUuid.ToString()));
            if (socket is null)
            {
                return null;
            }

            // Connect blocks; run it off the caller's thread and close the socket to abandon it on cancel.
            using (cancellationToken.Register(() => socket.Close()))
            {
                await Task.Run(socket.Connect, cancellationToken);
            }

            return new AndroidRfcommChannel(serviceUuid, socket);
        }
        catch (Exception ex) when (ex is Java.IO.IOException or IOException or Java.Lang.Exception or OperationCanceledException)
        {
            socket?.Close();
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _output.WriteAsync(data, cancellationToken);
            await _output.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        var buffer = new byte[ReadChunk];
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var count = await _input.ReadAsync(buffer, _stop.Token);
                if (count <= 0)
                {
                    break;
                }

                _received.Publish(buffer.AsMemory(0, count).ToArray());
            }
        }
        catch (Exception)
        {
            // Link dropped or channel disposed; reported through Closed.
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
        _stop.Cancel();
        try
        {
            _socket.Close();
        }
        catch (Java.IO.IOException)
        {
            // Already closed.
        }

        _socket.Dispose();
        _stop.Dispose();
        _writeLock.Dispose();
        RaiseClosed();
        return ValueTask.CompletedTask;
    }
}

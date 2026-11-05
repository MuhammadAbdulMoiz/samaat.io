using System.Net;
using System.Net.Sockets;
using Samaat.Transport;

namespace Samaat.Platform.Windows;

/// <summary>
/// <see cref="IRfcommChannel"/> over a Winsock Bluetooth socket opened by channel number. For services
/// with no SDP record Windows lists (Bose BMAP on channels 8 and 9), which the WinRT API can't reach.
/// </summary>
internal sealed class WinsockRfcommChannel : IRfcommChannel
{
    private const AddressFamily AfBth = (AddressFamily)32;
    private const ProtocolType BthProtoRfcomm = (ProtocolType)3;
    private const int ReadChunk = 1024;

    private readonly Socket _socket;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly RfcommReceiveBuffer _received;
    private int _closed;

    private WinsockRfcommChannel(Socket socket)
    {
        _received = new RfcommReceiveBuffer(this);
        _socket = socket;
        _ = ReadLoopAsync();
    }

    public Guid ServiceUuid { get; } = Guid.Parse("00001101-0000-1000-8000-00805f9b34fb");

    public bool IsClosed => Volatile.Read(ref _closed) == 1;

    public event EventHandler<ReadOnlyMemory<byte>>? Received
    {
        add => _received.Add(value!);
        remove => _received.Remove(value!);
    }

    public event EventHandler? Closed;

    /// <summary>Null when the channel doesn't open (or the open is cancelled).</summary>
    public static async Task<IRfcommChannel?> OpenAsync(ulong address, byte channel, CancellationToken cancellationToken)
    {
        var socket = new Socket(AfBth, SocketType.Stream, BthProtoRfcomm);
        try
        {
            // Dispose is the only way to abort a blocking Connect.
            using var abort = cancellationToken.Register(socket.Dispose);
            await Task.Run(() => socket.Connect(new BluetoothEndPoint(address, channel)), CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
            return new WinsockRfcommChannel(socket);
        }
        catch (Exception)
        {
            socket.Dispose();
            return null;
        }
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            while (!data.IsEmpty)
            {
                data = data[await _socket.SendAsync(data, cancellationToken)..];
            }
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
            var buffer = new byte[ReadChunk];
            while (!_stop.IsCancellationRequested)
            {
                var count = await _socket.ReceiveAsync(buffer, _stop.Token);
                if (count == 0)
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
        _socket.Dispose();
        RaiseClosed();
        return ValueTask.CompletedTask;
    }

    /// <summary>SOCKADDR_BTH: family, 64-bit address, service class GUID (zero), RFCOMM channel.</summary>
    private sealed class BluetoothEndPoint(ulong address, byte channel) : EndPoint
    {
        public override AddressFamily AddressFamily => AfBth;

        public override EndPoint Create(SocketAddress socketAddress) => this;

        public override SocketAddress Serialize()
        {
            var bytes = new SocketAddress(AfBth, 30);
            for (var i = 0; i < 8; i++)
            {
                bytes[2 + i] = (byte)(address >> (8 * i));
            }

            bytes[26] = channel;
            return bytes;
        }
    }
}

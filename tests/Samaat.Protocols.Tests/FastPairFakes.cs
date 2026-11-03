using Samaat.Transport;

namespace Samaat.Protocols.Tests;

/// <summary>Bytes captured from the real realme Buds T200 Lite (docs/hardware-tests/2026-11-03-realme-buds-t200-lite.md).</summary>
internal static class FastPairCaptured
{
    public static readonly byte[] Chunk1 = Convert.FromHexString("0301000367B85103020006F7A0C67986C6030300035A46FF");

    public static readonly byte[] Chunk2 = Convert.FromHexString("03090005312E302E30030A00080102030405060708");

    public static byte[] Greeting => [.. Chunk1, .. Chunk2];

    /// <summary>A later battery push: left 50 %, right 40 %, case unknown.</summary>
    public static readonly byte[] Battery50_40 = Convert.FromHexString("030300033228FF");
}

/// <summary>An RFCOMM channel that records every write (the driver must make none) and honours the replay contract.</summary>
internal sealed class FakeRfcommChannel : IRfcommChannel
{
    private readonly RfcommReceiveBuffer _received;
    private int _closed;

    public FakeRfcommChannel()
    {
        _received = new RfcommReceiveBuffer(this);
    }

    public Guid ServiceUuid { get; } = Guid.Parse("df21fe2c-2515-4fdb-8886-f12c4d67927c");

    public int Writes { get; private set; }

    public bool Disposed { get; private set; }

    public bool IsClosed => Volatile.Read(ref _closed) == 1;

    public event EventHandler<ReadOnlyMemory<byte>>? Received
    {
        add => _received.Add(value!);
        remove => _received.Remove(value!);
    }

    public event EventHandler? Closed;

    public void Emit(params byte[][] chunks)
    {
        foreach (var chunk in chunks)
        {
            _received.Publish(chunk);
        }
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        Writes++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeRfcommTransports(Func<Guid, CancellationToken, Task<IRfcommChannel?>> open) : IDeviceTransports
{
    public List<Guid> Opened { get; } = [];

    public IGattClient? Gatt => null;

    public Task<IRfcommChannel?> OpenRfcommAsync(Guid serviceUuid, CancellationToken cancellationToken = default)
    {
        Opened.Add(serviceUuid);
        return open(serviceUuid, cancellationToken);
    }
}

namespace Samaat.Transport;

/// <summary>
/// A Bluetooth Classic RFCOMM channel to a vendor service on a paired device. Most earbud control
/// protocols run over one of these (docs/PLAN.md §1). Codecs only see bytes.
/// </summary>
public interface IRfcommChannel : IAsyncDisposable
{
    Guid ServiceUuid { get; }

    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>Raised for each chunk read from the channel. Chunks aren't frames: codecs reassemble. Any thread.</summary>
    event EventHandler<ReadOnlyMemory<byte>>? Received;

    /// <summary>Raised once when the remote side closes or the link drops.</summary>
    event EventHandler? Closed;
}

/// <summary>Opens transports to one device. Platforms implement it; drivers ask for what their protocol needs.</summary>
public interface IDeviceTransports
{
    /// <summary>Returns null when the device doesn't expose that service or it can't be opened.</summary>
    Task<IRfcommChannel?> OpenRfcommAsync(Guid serviceUuid, CancellationToken cancellationToken = default);

    /// <summary>The BLE side of the same physical device, if there is one.</summary>
    IGattClient? Gatt { get; }
}

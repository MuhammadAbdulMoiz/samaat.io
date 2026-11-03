namespace Samaat.Transport;

/// <summary>
/// Backs an <see cref="IRfcommChannel.Received"/> event so chunks that arrive before the first subscriber
/// aren't lost: devices push their greeting on connect, usually before the driver can subscribe.
/// One lock orders everything: a chunk either joins the queue or goes straight to the subscribers, and the
/// first subscriber's replay finishes before any later chunk is delivered. Handlers run under that lock,
/// so they must not block on the channel; the lock is re-entrant for handlers that unsubscribe.
/// </summary>
public sealed class RfcommReceiveBuffer(object sender, int capacity = 4096)
{
    private readonly Lock _gate = new();
    private readonly Queue<byte[]> _pending = new();
    private int _pendingBytes;
    private bool _subscribed;
    private EventHandler<ReadOnlyMemory<byte>>? _handlers;

    public void Add(EventHandler<ReadOnlyMemory<byte>> handler)
    {
        lock (_gate)
        {
            _handlers += handler;
            if (_subscribed)
            {
                return;
            }

            _subscribed = true;
            _pendingBytes = 0;
            while (_pending.TryDequeue(out var chunk))
            {
                handler(sender, chunk);
            }
        }
    }

    public void Remove(EventHandler<ReadOnlyMemory<byte>> handler)
    {
        lock (_gate)
        {
            _handlers -= handler;
        }
    }

    /// <summary>Called by the channel's read loop for each chunk.</summary>
    public void Publish(ReadOnlyMemory<byte> chunk)
    {
        lock (_gate)
        {
            if (_subscribed)
            {
                _handlers?.Invoke(sender, chunk);
                return;
            }

            _pending.Enqueue(chunk.ToArray());
            _pendingBytes += chunk.Length;
            while (_pendingBytes > capacity && _pending.TryDequeue(out var dropped))
            {
                _pendingBytes -= dropped.Length;
            }
        }
    }
}

using System.Collections.Concurrent;
using System.Security.Cryptography;
using Samaat.Core.Commands;
using Samaat.Handoff.Pairing;
using Samaat.Handoff.Protocol;
using Samaat.Handoff.Security;

namespace Samaat.Handoff.Link;

/// <summary>Handles one incoming message. Return the reply for a request (null means unsupported), or null for a notification.</summary>
public delegate Task<ReplyMessage?> PeerMessageHandler(PeerMessage message, CancellationToken cancellationToken);

/// <summary>
/// An open, encrypted session between the two apps: typed messages, request/reply matching and a keep-alive.
/// Incoming requests run concurrently, so a long request (a 60 s connect) doesn't hold up progress messages.
/// </summary>
public sealed class PeerChannel : IAsyncDisposable
{
    public static readonly TimeSpan DefaultKeepAlive = TimeSpan.FromSeconds(15);

    private readonly FrameStream _frames;
    private readonly SessionCipher _send;
    private readonly SessionCipher _receive;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<ReplyMessage>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private int _nextId;
    private int _closed;
    private Task _readLoop = Task.CompletedTask;
    private Task _keepAlive = Task.CompletedTask;

    private PeerChannel(FrameStream frames, byte[] sendKey, byte[] receiveKey)
    {
        _frames = frames;
        _send = new SessionCipher(sendKey);
        _receive = new SessionCipher(receiveKey);
    }

    /// <summary>The other side's <see cref="HelloMessage"/>: its name and platform.</summary>
    public HelloMessage PeerHello { get; private set; } = new("", "", 0);

    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    /// <summary>Raised once when the connection ends, for any reason. Any thread.</summary>
    public event EventHandler? Closed;

    /// <summary>Sends our hello and checks the peer's. A wrong secret fails here.</summary>
    internal static async Task<PeerChannel> OpenAsync(FrameStream frames, byte[] sendKey, byte[] receiveKey, HelloMessage ownHello, CancellationToken ct)
    {
        var channel = new PeerChannel(frames, sendKey, receiveKey);
        try
        {
            await channel.SendAsync(ownHello, ct);
            var frame = await frames.ReadAsync(ct).WaitAsync(Handshake.StepTimeout, ct)
                ?? throw new HandshakeException("The other device closed the connection.");
            if (MessageCodec.Decode(channel._receive.Open(frame)) is not HelloMessage peer)
            {
                throw new HandshakeException("The other device didn't say hello.");
            }

            channel.PeerHello = peer;
            return channel;
        }
        catch (CryptographicException)
        {
            await channel.DisposeAsync();
            throw new HandshakeException("The pairing doesn't match on the two devices. Forget it on both and pair again.") { Refused = true };
        }
        catch (TimeoutException)
        {
            await channel.DisposeAsync();
            throw new HandshakeException("The other device stopped answering.");
        }
        catch
        {
            await channel.DisposeAsync();
            throw;
        }
    }

    /// <summary>Starts reading. Call once, after wiring <see cref="Closed"/>.</summary>
    public void Start(PeerMessageHandler handler, TimeSpan? keepAlive = null, TimeProvider? time = null)
    {
        _readLoop = Task.Run(() => ReadLoopAsync(handler, _cts.Token));
        if (keepAlive is { } interval && interval > TimeSpan.Zero)
        {
            _keepAlive = Task.Run(() => KeepAliveAsync(interval, time ?? TimeProvider.System, _cts.Token));
        }
    }

    public async Task SendAsync(PeerMessage message, CancellationToken ct = default)
    {
        if (message.Id == 0)
        {
            message = message with { Id = Interlocked.Increment(ref _nextId) };
        }

        await WriteAsync(message, ct);
    }

    /// <summary>Sends a request and waits for its reply. Never throws for a lost connection or a timeout: those come back as results.</summary>
    public async Task<ReplyMessage> RequestAsync(PeerMessage message, TimeSpan timeout, TimeProvider? time = null, CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var waiter = new TaskCompletionSource<ReplyMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = waiter;
        try
        {
            if (IsClosed)
            {
                return Lost(id);
            }

            await WriteAsync(message with { Id = id }, ct);
            return await waiter.Task.WaitAsync(timeout, time ?? TimeProvider.System, ct);
        }
        catch (TimeoutException)
        {
            return new ReplyMessage(id, CommandStatus.TimedOut, "The other device didn't answer in time.");
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            return Lost(id);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private static ReplyMessage Lost(int id) => new(id, CommandStatus.Failed, "The connection to the other device was lost.");

    private async Task WriteAsync(PeerMessage message, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(IsClosed, this);
            // Sealing and writing under one lock keeps the counter nonces in the order the peer reads them.
            await _frames.WriteAsync(_send.Seal(MessageCodec.Encode(message)), ct);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException)
        {
            _ = CloseAsync();
            throw new IOException("The connection to the other device was lost.", ex);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(PeerMessageHandler handler, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = await _frames.ReadAsync(ct);
                if (frame is null)
                {
                    break;
                }

                // Every frame must be opened, even unknown ones, to keep the counter in step.
                var message = MessageCodec.Decode(_receive.Open(frame));
                switch (message)
                {
                    case null:
                        continue;
                    case ReplyMessage reply:
                        if (_pending.TryGetValue(reply.ReplyTo, out var waiter))
                        {
                            waiter.TrySetResult(reply);
                        }

                        break;
                    case PingMessage ping:
                        _ = ReplySafelyAsync(new ReplyMessage(ping.Id, CommandStatus.Confirmed));
                        break;
                    default:
                        _ = Task.Run(() => DispatchAsync(handler, message, ct), ct);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException
            or InvalidDataException or CryptographicException or System.Net.Sockets.SocketException)
        {
            // A dropped connection, a closed channel or a tampered frame all end the session.
        }
        finally
        {
            _ = CloseAsync();
        }
    }

    private async Task DispatchAsync(PeerMessageHandler handler, PeerMessage message, CancellationToken ct)
    {
        ReplyMessage? reply;
        try
        {
            reply = await handler(message, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            reply = new ReplyMessage(message.Id, CommandStatus.Failed, ex.Message);
        }

        if (message.ExpectsReply)
        {
            await ReplySafelyAsync((reply ?? new ReplyMessage(0, CommandStatus.Unsupported, "The other device doesn't know that request.")) with { ReplyTo = message.Id });
        }
    }

    private async Task ReplySafelyAsync(ReplyMessage reply)
    {
        try
        {
            await SendAsync(reply);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The connection is gone; the requester will see that.
        }
    }

    private async Task KeepAliveAsync(TimeSpan interval, TimeProvider time, CancellationToken ct)
    {
        try
        {
            var misses = 0;
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(interval, time, ct);
                var reply = await RequestAsync(new PingMessage(), interval, time, ct);
                misses = reply.Status == CommandStatus.Confirmed ? 0 : misses + 1;
                if (misses >= 2 || IsClosed)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await CloseAsync();
    }

    private async Task CloseAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        await _cts.CancelAsync();
        try
        {
            await _frames.DisposeAsync();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or System.Net.Sockets.SocketException)
        {
            // Already broken.
        }

        foreach (var waiter in _pending.Values)
        {
            waiter.TrySetResult(new ReplyMessage(0, CommandStatus.Failed, "The connection to the other device was lost."));
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        await _readLoop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await _keepAlive.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _send.Dispose();
        _receive.Dispose();
        _writeLock.Dispose();
        _cts.Dispose();
    }
}

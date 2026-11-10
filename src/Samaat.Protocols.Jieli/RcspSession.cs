using System.Threading.Channels;
using Samaat.Transport;

namespace Samaat.Protocols.Jieli;

/// <summary>A command's answer: the status byte (0 = OK) and the params after the sequence number.</summary>
public sealed record RcspReply(byte Status, byte[] Params);

/// <summary>
/// One RCSP link to one pair of earbuds: subscribe AE02, authenticate, then parse frames. Device commands
/// go to <see cref="Packet"/>; responses go to the one command in flight. Features share it and the
/// battery feature owns it.
/// </summary>
public sealed class RcspSession : IAsyncDisposable
{
    private static readonly TimeSpan DefaultResponseTimeout = TimeSpan.FromSeconds(2);

    private readonly IGattCharacteristic _write;
    private readonly IGattCharacteristic _notify;
    private readonly TimeSpan _authStepTimeout;
    private readonly TimeSpan _responseTimeout;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _send = new(1, 1);
    private IAsyncDisposable? _subscription;
    private Channel<byte[]>? _authInbox;
    private RcspFrameParser _parser = new();
    private (byte Op, byte Sn, TaskCompletionSource<RcspReply?> Waiter)? _pending;
    private byte _sn;
    private bool _disposed;

    public RcspSession(IGattCharacteristic write, IGattCharacteristic notify, TimeSpan authStepTimeout, TimeSpan? responseTimeout = null)
    {
        _write = write;
        _notify = notify;
        _authStepTimeout = authStepTimeout;
        _responseTimeout = responseTimeout ?? DefaultResponseTimeout;
    }

    /// <summary>A device-initiated command (flags bit 7 set), e.g. the battery push or a sys-info update. Any thread.</summary>
    public event Action<RcspPacket>? Packet;

    /// <summary>Raised after every successful authentication, first start or restart. Any thread.</summary>
    public event Action? Authenticated;

    /// <summary>Subscribes and authenticates. False on any auth failure; only cancellation throws.</summary>
    public Task<bool> StartAsync(CancellationToken cancellationToken) => RunAsync(cancellationToken);

    /// <summary>Resubscribes and authenticates again; the buds push their state again afterwards.</summary>
    public Task<bool> RestartAsync(CancellationToken cancellationToken) => RunAsync(cancellationToken);

    /// <summary>
    /// Sends one command and waits for its response. One command is in flight at a time and the sequence
    /// number increments per command. Null on a failed write, timeout or disposal.
    /// </summary>
    public async Task<RcspReply?> SendAsync(RcspCommand command, CancellationToken cancellationToken)
    {
        // Belt and braces on top of RcspCommands: only sys-info reads and writes ever leave this session.
        if (command.Op is not (RcspCommands.GetSysInfoOp or RcspCommands.SetSysInfoOp))
        {
            return null;
        }

        await _send.WaitAsync(cancellationToken);
        try
        {
            TaskCompletionSource<RcspReply?> waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
            byte sn;
            lock (_gate)
            {
                if (_disposed)
                {
                    return null;
                }

                sn = _sn++;
                _pending = (command.Op, sn, waiter);
            }

            try
            {
                return await _write.WriteAsync(command.ToFrame(sn), withoutResponse: true, cancellationToken)
                    ? await waiter.Task.WaitAsync(_responseTimeout, cancellationToken)
                    : null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Timeouts and Bluetooth errors both mean "no answer".
                return null;
            }
            finally
            {
                lock (_gate)
                {
                    _pending = null;
                }
            }
        }
        finally
        {
            _send.Release();
        }
    }

    private async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (_disposed)
            {
                return false;
            }

            // No command may be written while the handshake runs: hold the send gate for all of it.
            FailPending();
            bool authenticated;
            List<RcspPacket> early = [];
            await _send.WaitAsync(cancellationToken);
            try
            {
                await ReleaseSubscriptionAsync();
                var inbox = Channel.CreateUnbounded<byte[]>();
                lock (_gate)
                {
                    _authInbox = inbox;
                    _parser = new RcspFrameParser();
                }

                _subscription = await _notify.SubscribeAsync(OnChunk, cancellationToken);
                authenticated = await RcspAuth.RunAsync(_write, inbox.Reader, _authStepTimeout, cancellationToken);

                lock (_gate)
                {
                    _authInbox = null;
                    while (authenticated && inbox.Reader.TryRead(out var chunk))
                    {
                        early.AddRange(_parser.Feed(chunk));
                    }
                }
            }
            finally
            {
                // A throw or cancellation above must not leave chunks routed to an inbox nobody reads.
                lock (_gate)
                {
                    _authInbox = null;
                }

                _send.Release();
            }

            Route(early);
            if (authenticated)
            {
                Authenticated?.Invoke();
            }

            return authenticated;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private void OnChunk(byte[] chunk)
    {
        IReadOnlyList<RcspPacket> packets;
        lock (_gate)
        {
            if (_authInbox is { } inbox)
            {
                inbox.Writer.TryWrite(chunk);
                return;
            }

            packets = _parser.Feed(chunk);
        }

        Route(packets);
    }

    private void Route(IReadOnlyList<RcspPacket> packets)
    {
        foreach (var packet in packets)
        {
            if (!packet.IsResponse)
            {
                Packet?.Invoke(packet);
            }
            else if (packet.Params.Length >= 1)
            {
                // A response's first body byte is the status; the sequence number follows it.
                TaskCompletionSource<RcspReply?>? waiter = null;
                lock (_gate)
                {
                    if (_pending is { } pending && pending.Op == packet.Op && pending.Sn == packet.Params[0])
                    {
                        waiter = pending.Waiter;
                    }
                }

                waiter?.TrySetResult(new RcspReply(packet.Sn, packet.Params[1..]));
            }
        }
    }

    private void FailPending()
    {
        TaskCompletionSource<RcspReply?>? waiter;
        lock (_gate)
        {
            waiter = _pending?.Waiter;
        }

        waiter?.TrySetResult(null);
    }

    private async Task ReleaseSubscriptionAsync()
    {
        if (Interlocked.Exchange(ref _subscription, null) is { } subscription)
        {
            await subscription.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        FailPending();
        // Wait out a RunAsync in progress so it can't assign a subscription after this one is released.
        await _lifecycle.WaitAsync();
        try
        {
            await ReleaseSubscriptionAsync();
        }
        finally
        {
            _lifecycle.Release();
        }
    }
}

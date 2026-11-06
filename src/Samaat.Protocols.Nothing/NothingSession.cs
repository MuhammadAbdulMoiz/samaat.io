// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/maniacx/BudsLink @ 0eb4d07dcd7eafabf425d98c607e02fc9519c94b, src/lib/devices/nothingBuds/{nothingBudsSocket.js,nothingBudsConfig.js,deviceConfigs/CMFBuds.js}, GPL-3.0 (BudsLink credits radiance-project/ear-web).
// Changes: rewritten in C# onto Samaat.Transport and Samaat.Core; ANC read offset and noise-cancelling byte corrected from Samaat's own CMF Buds captures (docs/hardware-tests/2026-11-06-cmf-buds.md).

using Samaat.Core.Commands;
using Samaat.Core.Diagnostics;
using Samaat.Transport;

namespace Samaat.Protocols.Nothing;

/// <summary>
/// One RFCOMM link to one Nothing/CMF device: frames the bytes, raises parsed messages (outside its lock), and sends
/// one request at a time, matching the reply or ack by sequence number. Reads are always allowed; writes (0xFxxx)
/// only when the driver allows them. The first feature created owns it and closes the channel on dispose.
/// </summary>
internal sealed class NothingSession(IRfcommChannel channel, bool allowWrites, TimeProvider time, TimeSpan commandTimeout) : IAsyncDisposable
{
    private const byte MaxSeq = 250;

    private readonly NothingReader _reader = new();
    private readonly Lock _gate = new();
    private readonly Lock _dispatch = new();
    private readonly Queue<NothingMessage> _held = new();
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _holding;
    private byte _seq;
    private byte _pendingSeq;
    private ushort _pendingCommand;
    private TaskCompletionSource<NothingMessage>? _pending;
    private int _disposed;

    public bool AllowWrites => allowWrites;

    public TimeProvider Time => time;

    /// <summary>Completes when the channel closes.</summary>
    public Task Closed => _closed.Task;

    /// <summary>Every parsed message, replies and pushes alike. Handlers must not throw.</summary>
    public event Action<NothingMessage>? Message;

    /// <summary>Starts listening. Call <see cref="Hold"/> and subscribe first: buffered chunks are replayed straight away.</summary>
    public void Attach()
    {
        channel.Closed += OnClosed;
        channel.Received += OnReceived;
        if (channel.IsClosed)
        {
            _closed.TrySetResult();
        }
    }

    /// <summary>
    /// Sends a request and returns the reply or ack with the same sequence number; null on timeout, a closed channel or a
    /// failed write. Throws <see cref="InvalidOperationException"/> for a write while writes are off.
    /// </summary>
    public async Task<NothingMessage?> RequestAsync(ushort command, byte[] payload, CancellationToken cancellationToken) =>
        (await SendAsync(command, payload, cancellationToken)).Reply;

    /// <summary>Sends a write: Confirmed on ack, Failed when the write throws or the channel closes, TimedOut otherwise.</summary>
    public async Task<CommandResult> WriteAsync(ushort command, byte[] payload, CancellationToken cancellationToken)
    {
        var (reply, failed) = await SendAsync(command, payload, cancellationToken);
        return reply is not null ? CommandResult.Confirmed
            : failed ? new CommandResult(CommandStatus.Failed, "The earbuds' connection failed.")
            : new CommandResult(CommandStatus.TimedOut);
    }

    private async Task<(NothingMessage? Reply, bool Failed)> SendAsync(ushort command, byte[] payload, CancellationToken cancellationToken)
    {
        if (NothingCommands.IsWrite(command) && !allowWrites)
        {
            throw new InvalidOperationException("This session is read-only.");
        }

        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            var reply = new TaskCompletionSource<NothingMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            byte seq;
            lock (_gate)
            {
                seq = _pendingSeq = _seq = _seq >= MaxSeq ? (byte)1 : (byte)(_seq + 1);
                _pendingCommand = (ushort)(command & 0x0FFF);
                _pending = reply;
            }

            try
            {
                await channel.WriteAsync(NothingFrame.Encode(command, seq, payload), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SamaatLog.Info("Nothing", "A command couldn't be written", ex);
                return (null, true);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var deadline = Task.Delay(commandTimeout, time, timeoutCts.Token);
            var finished = await Task.WhenAny(reply.Task, deadline, _closed.Task);
            await timeoutCts.CancelAsync();
            if (finished == reply.Task)
            {
                return (reply.Task.Result, false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return (null, finished == _closed.Task);
        }
        finally
        {
            lock (_gate)
            {
                _pending = null;
            }

            _requestGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        channel.Received -= OnReceived;
        channel.Closed -= OnClosed;
        await channel.DisposeAsync();
    }

    /// <summary>Queues messages in order until <see cref="Release"/>, so pushes before the features subscribe aren't lost.</summary>
    internal void Hold()
    {
        lock (_dispatch)
        {
            _holding = true;
        }
    }

    internal void Release()
    {
        lock (_dispatch)
        {
            _holding = false;
            while (_held.TryDequeue(out var message))
            {
                Message?.Invoke(message);
            }
        }
    }

    private void OnClosed(object? sender, EventArgs e) => _closed.TrySetResult();

    private void OnReceived(object? sender, ReadOnlyMemory<byte> chunk)
    {
        lock (_dispatch)
        {
            IReadOnlyList<NothingMessage> messages;
            lock (_gate)
            {
                messages = _reader.Feed(chunk.Span);
            }

            foreach (var message in messages)
            {
                lock (_gate)
                {
                    var isReply = (message.Command & 0xF000) is 0x4000 or 0x7000;
                    if (isReply && _pending is { } waiter && message.Seq == _pendingSeq && (message.Command & 0x0FFF) == _pendingCommand)
                    {
                        waiter.TrySetResult(message);
                    }
                }

                if (_holding)
                {
                    _held.Enqueue(message);
                }
                else
                {
                    Message?.Invoke(message);
                }
            }
        }
    }
}

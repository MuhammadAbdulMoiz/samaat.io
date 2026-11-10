// SPDX-License-Identifier: GPL-3.0-or-later
// Written for Samaat from docs/protocols/sony.md (clean-room: behaviour learned from Gadgetbridge, no code copied).

using System.Collections.Concurrent;
using Samaat.Core.Commands;
using Samaat.Core.Diagnostics;
using Samaat.Transport;

namespace Samaat.Protocols.Sony;

/// <summary>
/// One RFCOMM link to one Sony headset: frames the bytes, answers the headset's own commands with an ACK, sends one
/// command at a time and waits for its ACK (which also hands over the next sequence number). Init, queries and ACKs
/// are always allowed; the noise set command only when writes are allowed. Disposing it closes the channel; the
/// battery feature owns it (the noise feature only detaches its handler).
/// </summary>
internal sealed class SonySession(IRfcommChannel channel, bool allowWrites, TimeProvider time, TimeSpan commandTimeout) : IAsyncDisposable
{
    private readonly SonyReader _reader = new();
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly ConcurrentQueue<byte[]> _acks = new();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? _pendingAck;
    private byte _seq;
    private int _disposed;

    public bool AllowWrites => allowWrites;

    public TimeProvider Time => time;

    public TimeSpan CommandTimeout => commandTimeout;

    /// <summary>Completes when the channel closes.</summary>
    public Task Closed => _closed.Task;

    /// <summary>The payload of every inbound command, after its ACK is queued. Handlers must not throw.</summary>
    public event Action<byte[]>? Message;

    /// <summary>Starts listening. Subscribe to <see cref="Message"/> first: buffered chunks are replayed straight away.</summary>
    public void Attach()
    {
        channel.Closed += OnClosed;
        channel.Received += OnReceived;
        if (channel.IsClosed)
        {
            _closed.TrySetResult();
        }
    }

    /// <summary>Sends a command and waits for its ACK: Confirmed, TimedOut with none, Failed if the write fails. Throws <see cref="InvalidOperationException"/> for the set command when writes are off.</summary>
    public async Task<CommandResult> SendCommandAsync(byte[] payload, CancellationToken cancellationToken)
    {
        if (!allowWrites && payload is [SonyNcAsmCommand.CommandType, ..])
        {
            throw new InvalidOperationException("This session is read-only.");
        }

        // One command at a time, so an ACK can only confirm the command that is waiting for it.
        await _commandGate.WaitAsync(cancellationToken);
        var acked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        byte seq;
        lock (_gate)
        {
            _pendingAck = acked;
            seq = _seq;
        }

        try
        {
            try
            {
                await _writeGate.WaitAsync(cancellationToken);
                try
                {
                    await channel.WriteAsync(SonyFrame.Encode(SonyFrame.KindCommand, seq, payload), cancellationToken);
                }
                finally
                {
                    _writeGate.Release();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SamaatLog.Info("Sony", "A command couldn't be written", ex);
                return new CommandResult(CommandStatus.Failed);
            }

            await FlushAcksAsync();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var deadline = Task.Delay(commandTimeout, time, timeoutCts.Token);
            var finished = await Task.WhenAny(acked.Task, deadline, _closed.Task);
            if (finished != acked.Task)
            {
                await timeoutCts.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
                // A dropped link fails at once instead of waiting out the timeout.
                return new CommandResult(finished == _closed.Task ? CommandStatus.Failed : CommandStatus.TimedOut);
            }

            await timeoutCts.CancelAsync();
            return CommandResult.Confirmed;
        }
        finally
        {
            lock (_gate)
            {
                if (_pendingAck == acked)
                {
                    _pendingAck = null;
                }
            }

            _commandGate.Release();
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

    private void OnClosed(object? sender, EventArgs e) => _closed.TrySetResult();

    private void OnReceived(object? sender, ReadOnlyMemory<byte> chunk)
    {
        IReadOnlyList<SonyMessage> messages;
        lock (_gate)
        {
            messages = _reader.Feed(chunk.Span);
        }

        foreach (var message in messages)
        {
            if (message.Kind == SonyFrame.KindAck)
            {
                TaskCompletionSource? waiter;
                lock (_gate)
                {
                    waiter = _pendingAck;
                    if (waiter is not null)
                    {
                        _seq = message.Seq;
                    }
                }

                waiter?.TrySetResult();
            }
            else if (message.Kind == SonyFrame.KindCommand)
            {
                _acks.Enqueue(SonyFrame.Encode(SonyFrame.KindAck, (byte)(1 - message.Seq), []));
                _ = FlushAcksAsync();
                Message?.Invoke(message.Payload);
            }
        }
    }

    /// <summary>
    /// Writes queued ACKs. When another write holds the gate, that writer flushes after releasing it, so an ACK
    /// queued from inside a write (a reply that arrives while the command is still being written) is never lost.
    /// </summary>
    private async Task FlushAcksAsync()
    {
        while (!_acks.IsEmpty && await _writeGate.WaitAsync(0))
        {
            try
            {
                while (_acks.TryDequeue(out var ack))
                {
                    await channel.WriteAsync(ack, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                // A dead channel also closes; there is nobody left to answer.
                SamaatLog.Info("Sony", "An ACK couldn't be written", ex);
            }
            finally
            {
                _writeGate.Release();
            }
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/timschneeb/GalaxyBudsClient @ 754b6fdfb1c151b022cece775c8aaad4eb185afa, GalaxyBudsClient/Message/SppMessage.cs and GalaxyBudsClient/Message/Decoder/AcknowledgementDecoder.cs and GalaxyBudsClient/Message/Encoder/ManagerInfoEncoder.cs, Copyright (c) 2019-2026 Tim Schneeberger and contributors, GPL-3.0.
// Changes: rewritten onto Samaat.Transport (IRfcommChannel) and Samaat.Core features.

using Samaat.Core.Commands;
using Samaat.Core.Diagnostics;
using Samaat.Transport;

namespace Samaat.Protocols.Samsung;

/// <summary>
/// One RFCOMM link to one pair of buds: frames the bytes, raises parsed messages (outside its lock), and, only
/// when writes are allowed, sends commands and answers every extended status with MANAGER_INFO (the buds keep
/// pushing status once they have it). With writes off nothing is ever written. Disposing it closes the channel;
/// the battery feature owns it (the noise and equalizer features only detach their handlers).
/// </summary>
internal sealed class SamsungSession(IRfcommChannel channel, bool legacy, bool allowWrites, TimeProvider time, TimeSpan commandTimeout) : IAsyncDisposable
{
    private static readonly byte[] ManagerInfoPayload = [0x01, 0x01, 34];

    private readonly SppReader _reader = new(legacy);
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly Dictionary<byte, TaskCompletionSource> _waiters = [];
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    public bool AllowWrites => allowWrites;

    /// <summary>Completes when the channel closes.</summary>
    public Task Closed => _closed.Task;

    /// <summary>Every parsed message. Handlers must not throw.</summary>
    public event Action<SppMessage>? Message;

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

    /// <summary>Throws <see cref="InvalidOperationException"/> when writes aren't allowed.</summary>
    public async Task WriteAsync(byte id, byte[] payload, CancellationToken cancellationToken)
    {
        if (!allowWrites)
        {
            throw new InvalidOperationException("This session is read-only.");
        }

        var frame = SppFrame.Encode(id, payload, legacy);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await channel.WriteAsync(frame, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>Sends a command and completes on the buds' Ack for it: Confirmed, TimedOut with no Ack, Failed if the write fails.</summary>
    public async Task<CommandResult> SendAndConfirmAsync(byte id, byte[] payload, CancellationToken cancellationToken)
    {
        // One command at a time, so an Ack can only confirm the request that is waiting for it.
        await _commandGate.WaitAsync(cancellationToken);
        var acked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _waiters[id] = acked;
        }

        try
        {
            try
            {
                await WriteAsync(id, payload, cancellationToken);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or InvalidOperationException))
            {
                SamaatLog.Info("Samsung", "A command couldn't be written", ex);
                return new CommandResult(CommandStatus.Failed);
            }

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
                if (_waiters.TryGetValue(id, out var current) && current == acked)
                {
                    _waiters.Remove(id);
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
        IReadOnlyList<SppMessage> messages;
        lock (_gate)
        {
            messages = _reader.Feed(chunk.Span);
        }

        foreach (var message in messages)
        {
            Dispatch(message);
        }
    }

    private void Dispatch(SppMessage message)
    {
        if (message is { Id: SamsungIds.Ack, Payload.Length: >= 1 })
        {
            TaskCompletionSource? waiter;
            lock (_gate)
            {
                _waiters.TryGetValue(message.Payload[0], out waiter);
            }

            waiter?.TrySetResult();
        }
        else if (allowWrites && message is { Id: SamsungIds.ExtendedStatus, Payload.Length: >= 4 })
        {
            _ = SamaatLog.Guard("Samsung", "Replying to the status", () => WriteAsync(SamsungIds.ManagerInfo, ManagerInfoPayload, CancellationToken.None));
        }

        Message?.Invoke(message);
    }
}

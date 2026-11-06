// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/Oppzippy/OpenSCQ30 @ e75a68b70de8eaf2cf0ca364e132085a097557c5, lib/src/devices/soundcore/common/packet/packet_io_controller.rs and lib/src/devices/soundcore/common/packet/outbound/{request_state,set_sound_modes}.rs, Copyright (c) 2023-2026 Kyle Scheuing and contributors, GPL-3.0.
// Changes: rewritten in C# onto Samaat.Transport (IRfcommChannel) and Samaat.Core features.

using Samaat.Core.Commands;
using Samaat.Core.Diagnostics;
using Samaat.Transport;

namespace Samaat.Protocols.Soundcore;

/// <summary>Command bytes (c0, c1). Group 0x01 is state and battery, group 0x06 is sound modes.</summary>
internal static class SoundcoreCommands
{
    public const byte StatusGroup = 0x01;
    public const byte State = 0x01;
    public const byte BatteryLevel = 0x03;
    public const byte BatteryCharging = 0x04;
    public const byte SoundModesGroup = 0x06;
    public const byte SoundModesPush = 0x01;
    public const byte SoundModesSet = 0x81;
}

/// <summary>
/// One RFCOMM link to one Soundcore device: frames the bytes, raises parsed messages (outside its lock), sends the
/// state request (a query, always allowed) and, only when writes are allowed, other commands. Disposing it closes
/// the channel; the battery feature owns it (the noise and equalizer features only detach their handlers).
/// </summary>
internal sealed class SoundcoreSession(IRfcommChannel channel, bool allowWrites, TimeProvider time, TimeSpan commandTimeout) : IAsyncDisposable
{
    private readonly SoundcoreReader _reader = new();
    private readonly Lock _gate = new();
    private readonly Lock _dispatch = new();
    private readonly Queue<SoundcoreMessage> _held = new();
    private bool _holding;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly Dictionary<ushort, TaskCompletionSource> _waiters = [];
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    public bool AllowWrites => allowWrites;

    public TimeProvider Time => time;

    /// <summary>Completes when the channel closes.</summary>
    public Task Closed => _closed.Task;

    /// <summary>Every parsed message. Handlers must not throw.</summary>
    public event Action<SoundcoreMessage>? Message;

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

    /// <summary>Asks the device for its state. A query, so it is sent even when writes are off.</summary>
    public Task RequestStateAsync(CancellationToken cancellationToken) =>
        SendAsync(SoundcoreFrame.Encode(SoundcoreCommands.StatusGroup, SoundcoreCommands.State, []), cancellationToken);

    /// <summary>Throws <see cref="InvalidOperationException"/> when writes aren't allowed.</summary>
    public Task WriteAsync(byte c0, byte c1, byte[] body, CancellationToken cancellationToken)
    {
        if (!allowWrites)
        {
            throw new InvalidOperationException("This session is read-only.");
        }

        return SendAsync(SoundcoreFrame.Encode(c0, c1, body), cancellationToken);
    }

    /// <summary>Sends a command and completes on the device's inbound message with the same command: Confirmed, TimedOut with none, Failed if the write fails.</summary>
    public async Task<CommandResult> SendAndConfirmAsync(byte c0, byte c1, byte[] body, CancellationToken cancellationToken)
    {
        // One command at a time, so an ack can only confirm the request that is waiting for it.
        await _commandGate.WaitAsync(cancellationToken);
        var key = Key(c0, c1);
        var acked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _waiters[key] = acked;
        }

        try
        {
            try
            {
                await WriteAsync(c0, c1, body, cancellationToken);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or InvalidOperationException))
            {
                SamaatLog.Info("Soundcore", "A command couldn't be written", ex);
                return new CommandResult(CommandStatus.Failed);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var deadline = Task.Delay(commandTimeout, time, timeoutCts.Token);
            if (await Task.WhenAny(acked.Task, deadline) != acked.Task)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new CommandResult(CommandStatus.TimedOut);
            }

            await timeoutCts.CancelAsync();
            return CommandResult.Confirmed;
        }
        finally
        {
            lock (_gate)
            {
                if (_waiters.TryGetValue(key, out var current) && current == acked)
                {
                    _waiters.Remove(key);
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

    private static ushort Key(byte c0, byte c1) => (ushort)((c0 << 8) | c1);

    private async Task SendAsync(byte[] frame, CancellationToken cancellationToken)
    {
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

    private void OnClosed(object? sender, EventArgs e) => _closed.TrySetResult();

    /// <summary>
    /// Defers raising <see cref="Message"/> (queued in order) until <see cref="Release"/>. Called from inside a Message
    /// handler, i.e. on the dispatching thread already holding the dispatch lock; <see cref="Lock"/> is re-entrant, so
    /// taking it again here is safe.
    /// </summary>
    internal void Hold()
    {
        lock (_dispatch)
        {
            _holding = true;
        }
    }

    /// <summary>Stops holding and raises the queued messages in order. Later chunks wait on the dispatch lock, so order is kept.</summary>
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

    private void OnReceived(object? sender, ReadOnlyMemory<byte> chunk)
    {
        lock (_dispatch)
        {
            IReadOnlyList<SoundcoreMessage> messages;
            lock (_gate)
            {
                messages = _reader.Feed(chunk.Span);
            }

            foreach (var message in messages)
            {
                TaskCompletionSource? waiter;
                lock (_gate)
                {
                    _waiters.TryGetValue(Key(message.C0, message.C1), out waiter);
                }

                // Acks always complete their waiter; only the public Message raise is deferred while holding.
                waiter?.TrySetResult();
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

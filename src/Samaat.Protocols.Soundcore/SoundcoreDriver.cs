// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/Oppzippy/OpenSCQ30 @ e75a68b70de8eaf2cf0ca364e132085a097557c5, lib/src/devices/soundcore/soundcore.rs, lib/src/devices/soundcore/common/device.rs and lib/src/devices/soundcore/common/packet/packet_io_controller.rs, Copyright (c) 2023-2026 Kyle Scheuing and contributors, GPL-3.0.
// Changes: rewritten in C# onto Samaat.Transport (IRfcommChannel) and Samaat.Core features.

using Samaat.Core.Devices;
using Samaat.Core.Diagnostics;
using Samaat.Core.Drivers;
using Samaat.Transport;

namespace Samaat.Protocols.Soundcore;

/// <summary>
/// Soundcore headphones and earbuds over RFCOMM: battery, and noise mode and EQ preset where the model has them, from
/// the state the device sends when asked. With <c>allowWrites</c> false (the default) the only thing ever written is
/// the state request, and noise and EQ are read-only; with it true the noise setter sends commands.
/// </summary>
public sealed class SoundcoreDriver(bool allowWrites = false, TimeSpan? commandTimeout = null) : IEarbudDriver
{
    public const string DriverId = "soundcore-spp";

    internal const string ReadOnlyMessage = "Changing this on Soundcore devices isn't supported yet.";

    private static readonly TimeSpan[] StateRetryDelays = [TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(1500)];

    public string Id => DriverId;

    public string DisplayName => "Soundcore";

    /// <summary>Returns the features once a valid state arrives (the request is sent up to three times); otherwise nothing. Never throws except on cancellation.</summary>
    public async Task<IReadOnlyList<IFeature>> OpenAsync(DriverContext context, CancellationToken cancellationToken = default)
    {
        if (!SoundcoreModels.TryGet(context.Entry.Id, out var model))
        {
            return [];
        }

        IRfcommChannel? channel = null;
        SoundcoreSession? session = null;
        try
        {
            channel = await context.Transports.OpenRfcommAsync(SoundcoreService.Pick(context.Signals.Services), cancellationToken);
            if (channel is null)
            {
                return [];
            }

            session = new SoundcoreSession(channel, allowWrites, context.Time, commandTimeout ?? TimeSpan.FromSeconds(2));
            var first = new TaskCompletionSource<SoundcoreState>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnMessage(SoundcoreMessage message)
            {
                if (SoundcoreState.TryParse(model, message) is { } state)
                {
                    // Queue everything after the first state until the features have subscribed.
                    session.Hold();
                    first.TrySetResult(state);
                }
            }

            session.Message += OnMessage;
            session.Attach();
            var state0 = await WaitForStateAsync(session, first.Task, context.Time, cancellationToken);
            session.Message -= OnMessage;
            if (state0 is null)
            {
                SamaatLog.Info("Soundcore", "The device sent no state");
                await DisposeQuietlyAsync(session, channel);
                return [];
            }

            var features = new List<IFeature> { new SoundcoreBatteryFeature(session, model, state0) };
            if (model.SoundModesOffset is not null)
            {
                features.Add(new SoundcoreNoiseControlFeature(session, model, state0));
            }

            if (model.EqOffset is not null)
            {
                features.Add(new SoundcoreEqualizerFeature(session, model, state0));
            }

            session.Release();
            return features;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DisposeQuietlyAsync(session, channel);
            throw;
        }
        catch (Exception ex)
        {
            // A closed channel and Bluetooth errors all just mean no driver features.
            SamaatLog.Info("Soundcore", "The Soundcore link didn't open", ex);
            await DisposeQuietlyAsync(session, channel);
            return [];
        }
    }

    /// <summary>Sends the state request, then waits 500, 1000 and 1500 ms for a state, resending after each wait. Null if none came or the channel closed.</summary>
    private static async Task<SoundcoreState?> WaitForStateAsync(SoundcoreSession session, Task<SoundcoreState> first, TimeProvider time, CancellationToken cancellationToken)
    {
        foreach (var delay in StateRetryDelays)
        {
            await session.RequestStateAsync(cancellationToken);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var wait = Task.Delay(delay, time, timeoutCts.Token);
            var finished = await Task.WhenAny(first, session.Closed, wait);
            await timeoutCts.CancelAsync();
            if (finished == first)
            {
                return await first;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (finished == session.Closed)
            {
                return null;
            }
        }

        return null;
    }

    private static async Task DisposeQuietlyAsync(SoundcoreSession? session, IRfcommChannel? channel)
    {
        try
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }
            else if (channel is not null)
            {
                await channel.DisposeAsync();
            }
        }
        catch (Exception)
        {
            // Releasing a dead channel can fail; discovery must not.
        }
    }
}

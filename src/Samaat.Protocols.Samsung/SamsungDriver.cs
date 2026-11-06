// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/timschneeb/GalaxyBudsClient @ 754b6fdfb1c151b022cece775c8aaad4eb185afa, GalaxyBudsClient/Model/Specifications/*.cs and GalaxyBudsClient/Message/Decoder/ExtendedStatusUpdateDecoder.cs, Copyright (c) 2019-2026 Tim Schneeberger and contributors, GPL-3.0.
// Changes: rewritten onto Samaat.Transport (IRfcommChannel) and Samaat.Core features.

using Samaat.Core.Devices;
using Samaat.Core.Diagnostics;
using Samaat.Core.Drivers;
using Samaat.Transport;

namespace Samaat.Protocols.Samsung;

/// <summary>
/// Samsung Galaxy Buds over RFCOMM: battery, and noise mode and EQ preset where the model has them, from the
/// status the buds push. With <c>allowWrites</c> false (the default) nothing is ever written to the buds and noise
/// and EQ are read-only; with it true the driver answers each status with MANAGER_INFO and the setters send commands.
/// </summary>
public sealed class SamsungDriver(bool allowWrites = false, TimeSpan? firstStatusTimeout = null, TimeSpan? commandTimeout = null) : IEarbudDriver
{
    public const string DriverId = "samsung-spp";

    public string Id => DriverId;

    public string DisplayName => "Samsung Galaxy Buds";

    /// <summary>Returns the features once the first valid extended status arrives; otherwise nothing. Never throws.</summary>
    public async Task<IReadOnlyList<IFeature>> OpenAsync(DriverContext context, CancellationToken cancellationToken = default)
    {
        if (!SamsungModels.TryGet(context.Entry.Id, out var model))
        {
            return [];
        }

        IRfcommChannel? channel = null;
        SamsungSession? session = null;
        try
        {
            channel = await context.Transports.OpenRfcommAsync(model.Service, cancellationToken);
            if (channel is null)
            {
                return [];
            }

            session = new SamsungSession(channel, model.Legacy, allowWrites, context.Time, commandTimeout ?? TimeSpan.FromSeconds(2));
            var first = new TaskCompletionSource<SamsungStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnMessage(SppMessage message)
            {
                if (message.Id == SamsungIds.ExtendedStatus && SamsungStatus.TryParse(message, model) is { } status)
                {
                    first.TrySetResult(status);
                }
            }

            session.Message += OnMessage;
            session.Attach();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var deadline = Task.Delay(firstStatusTimeout ?? TimeSpan.FromSeconds(3), context.Time, timeoutCts.Token);
            if (await Task.WhenAny(first.Task, session.Closed, deadline) != first.Task)
            {
                session.Message -= OnMessage;
                cancellationToken.ThrowIfCancellationRequested();
                SamaatLog.Info("Samsung", "The buds sent no status");
                await DisposeQuietlyAsync(session, channel);
                return [];
            }

            await timeoutCts.CancelAsync();
            session.Message -= OnMessage;
            var status0 = await first.Task;

            // shortcut: a message between the first status and the other features subscribing is missed; the buds repeat status often.
            var features = new List<IFeature> { new SamsungBatteryFeature(session, model, status0, context.Time) };
            if (model.NoiseModes)
            {
                features.Add(new SamsungNoiseControlFeature(session, model, status0));
            }

            if (model.EqOffset is not null)
            {
                features.Add(new SamsungEqualizerFeature(session, model, status0));
            }

            return features;
        }
        catch (Exception ex)
        {
            // Timeouts, a closed channel and Bluetooth errors all just mean no driver features.
            SamaatLog.Info("Samsung", "The Galaxy Buds link didn't open", ex);
            await DisposeQuietlyAsync(session, channel);
            return [];
        }
    }

    private static async Task DisposeQuietlyAsync(SamsungSession? session, IRfcommChannel? channel)
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

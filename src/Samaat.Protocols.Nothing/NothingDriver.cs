// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/maniacx/BudsLink @ 0eb4d07dcd7eafabf425d98c607e02fc9519c94b, src/lib/devices/nothingBuds/{nothingBudsSocket.js,nothingBudsConfig.js,deviceConfigs/CMFBuds.js}, GPL-3.0 (BudsLink credits radiance-project/ear-web).
// Changes: rewritten in C# onto Samaat.Transport and Samaat.Core; ANC read offset and noise-cancelling byte corrected from Samaat's own CMF Buds captures (docs/hardware-tests/2026-11-06-cmf-buds.md).

using Samaat.Core.Devices;
using Samaat.Core.Diagnostics;
using Samaat.Core.Drivers;
using Samaat.Transport;

namespace Samaat.Protocols.Nothing;

/// <summary>
/// Nothing and CMF earbuds over RFCOMM: battery, noise control and EQ presets. It asks the model id first and
/// gives nothing for a model it doesn't know. With <c>allowWrites</c> false the noise and EQ setters don't send.
/// </summary>
public sealed class NothingDriver(bool allowWrites = true, TimeSpan? commandTimeout = null) : IEarbudDriver
{
    public const string DriverId = "nothing-spp";

    internal const string ReadOnlyMessage = "Changing this on Nothing devices is turned off.";

    private const int ModelAttempts = 2;

    private static readonly Guid Service = Guid.Parse("aeac4a03-dff5-498f-843a-34487cf133eb");

    public string Id => DriverId;

    public string DisplayName => "Nothing";

    /// <summary>Returns the features the buds answer for; nothing if the model is unknown or silent. Never throws except on cancellation.</summary>
    public async Task<IReadOnlyList<IFeature>> OpenAsync(DriverContext context, CancellationToken cancellationToken = default)
    {
        IRfcommChannel? channel = null;
        NothingSession? session = null;
        try
        {
            channel = await context.Transports.OpenRfcommAsync(Service, cancellationToken);
            if (channel is null)
            {
                return [];
            }

            session = new NothingSession(channel, allowWrites, context.Time, commandTimeout ?? TimeSpan.FromSeconds(1));
            session.Hold();
            session.Attach();
            if (await ReadModelAsync(session, cancellationToken) is not { } model)
            {
                SamaatLog.Info("Nothing", "The device didn't report a known model");
                await DisposeQuietlyAsync(session, channel);
                return [];
            }

            var battery = await session.RequestAsync(NothingCommands.Battery, [], cancellationToken);
            var anc = await session.RequestAsync(NothingCommands.AncRead, [], cancellationToken);
            var eq = await session.RequestAsync(NothingCommands.EqRead, [], cancellationToken);

            // The first feature built owns the channel.
            var features = new List<IFeature>();
            if (battery is { Payload.Length: > 0 } b)
            {
                features.Add(new NothingBatteryFeature(session, b, features.Count == 0));
            }

            if (anc is { } a && NothingAnc.RawOf(a) is not null)
            {
                features.Add(new NothingNoiseControlFeature(session, model, a, features.Count == 0));
            }

            if (eq is { Payload.Length: >= 1 } e)
            {
                features.Add(new NothingEqualizerFeature(session, model, e, features.Count == 0));
            }

            if (features.Count == 0)
            {
                await DisposeQuietlyAsync(session, channel);
                return [];
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
            SamaatLog.Info("Nothing", "The Nothing link didn't open", ex);
            await DisposeQuietlyAsync(session, channel);
            return [];
        }
    }

    private static async Task<NothingModel?> ReadModelAsync(NothingSession session, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < ModelAttempts && !session.Closed.IsCompleted; attempt++)
        {
            if (await session.RequestAsync(NothingCommands.ModelId, [], cancellationToken) is { Payload.Length: >= 2 } reply)
            {
                return NothingModels.TryGet((ushort)(reply.Payload[0] | (reply.Payload[1] << 8)), out var model) ? model : null;
            }
        }

        return null;
    }

    private static async Task DisposeQuietlyAsync(NothingSession? session, IRfcommChannel? channel)
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

using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Transport;
using Samaat.Core.Diagnostics;

namespace Samaat.Protocols.Bose;

/// <summary>
/// Bose BMAP on raw RFCOMM channels 8 and 9 (no SDP record, no handshake): reads the current noise mode.
/// Read-only; the one frame it writes is the audio-mode GET.
/// </summary>
public sealed class BoseBmapDriver(TimeSpan? replyTimeout = null) : IEarbudDriver
{
    public const string DriverId = "bose-bmap";

    /// <summary>Both answered on hardware.</summary>
    public static readonly byte[] Channels = [8, 9];

    public string Id => DriverId;

    public string DisplayName => "Bose BMAP";

    /// <summary>Returns the noise feature from the first channel that answers; otherwise nothing. Never throws.</summary>
    public async Task<IReadOnlyList<IFeature>> OpenAsync(DriverContext context, CancellationToken cancellationToken = default)
    {
        foreach (var number in Channels)
        {
            IRfcommChannel? channel = null;
            try
            {
                channel = await context.Transports.OpenRfcommChannelAsync(number, cancellationToken);
                if (channel is null)
                {
                    continue;
                }

                if (await BoseNoiseControlFeature.CreateAsync(channel, context.Time, replyTimeout ?? TimeSpan.FromSeconds(2), cancellationToken) is { } feature)
                {
                    return [feature];
                }
            }
            catch (Exception ex)
            {
                // Timeouts, a closed channel and Bluetooth errors all just mean this channel is no use.
                SamaatLog.Info("Bose", "A BMAP channel didn't answer", ex);
            }

            await DisposeQuietlyAsync(channel);
        }

        return [];
    }

    private static async Task DisposeQuietlyAsync(IRfcommChannel? channel)
    {
        try
        {
            if (channel is not null)
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

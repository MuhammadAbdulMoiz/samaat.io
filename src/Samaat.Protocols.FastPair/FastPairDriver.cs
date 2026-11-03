using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Transport;

namespace Samaat.Protocols.FastPair;

/// <summary>
/// Google Fast Pair Message Stream over RFCOMM: battery (left, right, case) and firmware from what the buds
/// push on connect. Strictly read-only; the driver never writes to the channel. The caller owns the deadline:
/// pass a token that expires when discovery should give up.
/// </summary>
public sealed class FastPairDriver(TimeSpan? refreshTimeout = null) : IEarbudDriver
{
    public const string DriverId = "fast-pair";

    public static readonly Guid MessageStreamService = Guid.Parse("df21fe2c-2515-4fdb-8886-f12c4d67927c");

    public string Id => DriverId;

    public string DisplayName => "Google Fast Pair";

    /// <summary>Returns the battery feature once a valid battery message has arrived; otherwise nothing. Never throws.</summary>
    public async Task<IReadOnlyList<IFeature>> OpenAsync(DriverContext context, CancellationToken cancellationToken = default)
    {
        IRfcommChannel? channel = null;
        FastPairBatteryFeature? feature = null;
        try
        {
            channel = await context.Transports.OpenRfcommAsync(MessageStreamService, cancellationToken);
            if (channel is null)
            {
                return [];
            }

            feature = FastPairBatteryFeature.Attach(
                channel,
                ct => context.Transports.OpenRfcommAsync(MessageStreamService, ct),
                context.Time,
                refreshTimeout ?? TimeSpan.FromSeconds(3));
            await feature.FirstReadingAsync(cancellationToken);
            return [feature];
        }
        catch (Exception)
        {
            // Timeouts, a closed channel and Bluetooth errors all just mean no driver features.
        }

        try
        {
            if (feature is not null)
            {
                await feature.DisposeAsync();
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

        return [];
    }
}

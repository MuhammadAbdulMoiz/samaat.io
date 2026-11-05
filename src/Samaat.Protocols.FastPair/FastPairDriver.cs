using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Transport;

namespace Samaat.Protocols.FastPair;

/// <summary>
/// Google Fast Pair Message Stream over RFCOMM: battery (left, right, case) and firmware from what the buds
/// push on connect. Writes only the two SASS get requests (see <see cref="FastPairMessages.SassGetCapability"/>); when the buds answer with SASS multipoint, a read-only multipoint feature is added. The caller owns the deadline:
/// pass a token that expires when discovery should give up.
/// </summary>
public sealed class FastPairDriver(TimeSpan? refreshTimeout = null, TimeSpan? sassTimeout = null) : IEarbudDriver
{
    public const string DriverId = "fast-pair";

    public static readonly Guid MessageStreamService = Guid.Parse("df21fe2c-2515-4fdb-8886-f12c4d67927c");

    public string Id => DriverId;

    public string DisplayName => "Google Fast Pair";

    /// <summary>Returns the battery feature once a valid battery message has arrived, plus multipoint if SASS reports it; otherwise nothing. Never throws.</summary>
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
            // The SASS deadline runs from the request, alongside the wait for the greeting.
            var sass = FastPairMultipointFeature.Start(feature).ReadyAsync(sassTimeout ?? TimeSpan.FromSeconds(1), cancellationToken);
            await feature.FirstReadingAsync(cancellationToken);
            try
            {
                if (await sass is { } multipoint)
                {
                    return [feature, multipoint];
                }
            }
            catch (Exception)
            {
                // Any SASS failure just means battery only.
            }

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

using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Transport;

namespace Samaat.Protocols.Jieli;

/// <summary>
/// Jieli RCSP over BLE (service AE00). Gives battery, noise control and EQ presets for models whose
/// catalog entry names this driver. The caller owns the deadline: pass a token that expires when
/// discovery should give up.
/// </summary>
public sealed class JieliDriver(TimeSpan? authStepTimeout = null, TimeSpan? responseTimeout = null) : IEarbudDriver
{
    public const string DriverId = "jieli-rcsp";

    public string Id => DriverId;

    public string DisplayName => "Jieli RCSP";

    /// <summary>
    /// Returns the battery feature once real data has arrived, plus noise control and EQ when the buds
    /// answered with usable lists. No battery push means no features at all. Never throws.
    /// </summary>
    public async Task<IReadOnlyList<IFeature>> OpenAsync(DriverContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (context.Transports.Gatt is not { } gatt
                || (await gatt.GetServicesAsync(JieliGatt.Service, cancellationToken)).FirstOrDefault() is not { } service)
            {
                return [];
            }

            var write = (await service.GetCharacteristicsAsync(JieliGatt.Write, cancellationToken)).FirstOrDefault(c => c.CanWrite);
            var notify = (await service.GetCharacteristicsAsync(JieliGatt.Notify, cancellationToken)).FirstOrDefault(c => c.CanNotify);
            if (write is null || notify is null)
            {
                return [];
            }

            return await OpenSessionAsync(write, notify, context.Time, cancellationToken);
        }
        catch (Exception)
        {
            // Timeouts and Bluetooth errors just mean no driver features.
            return [];
        }
    }

    private async Task<IReadOnlyList<IFeature>> OpenSessionAsync(
        IGattCharacteristic write,
        IGattCharacteristic notify,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var session = new RcspSession(write, notify, authStepTimeout ?? TimeSpan.FromSeconds(3), responseTimeout);

        // The battery feature listens before the session starts so the push right after auth isn't missed.
        var battery = JieliBatteryFeature.Attach(session, time);
        JieliNoiseControlFeature? noise = null;
        JieliEqualizerFeature? equalizer = null;
        try
        {
            if (await session.StartAsync(cancellationToken))
            {
                // Noise and EQ reads queue behind each other in the session while the battery push is awaited.
                await Task.WhenAll(
                    battery.FirstReadingAsync(cancellationToken),
                    Task.Run(
                        async () =>
                        {
                            noise = await JieliNoiseControlFeature.CreateAsync(session, cancellationToken);
                            equalizer = await JieliEqualizerFeature.CreateAsync(session, cancellationToken);
                        },
                        CancellationToken.None));
                return [battery, .. new IFeature?[] { noise, equalizer }.OfType<IFeature>()];
            }
        }
        catch (Exception)
        {
            // No battery data in time (or a transport error): release everything below.
        }

        if (noise is not null)
        {
            await noise.DisposeAsync();
        }

        if (equalizer is not null)
        {
            await equalizer.DisposeAsync();
        }

        await battery.DisposeAsync();
        return [];
    }
}

using Samaat.Core.Devices;
using Samaat.Core.Drivers;

namespace Samaat.Protocols.Jieli;

/// <summary>
/// Jieli RCSP over BLE (service AE00). Gives battery for models whose catalog entry names this driver.
/// The caller owns the deadline: pass a token that expires when discovery should give up.
/// </summary>
public sealed class JieliDriver(TimeSpan? authStepTimeout = null) : IEarbudDriver
{
    public const string DriverId = "jieli-rcsp";

    public string Id => DriverId;

    public string DisplayName => "Jieli RCSP";

    /// <summary>Returns the battery feature once real data has arrived, otherwise no features. Never throws.</summary>
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

            var battery = await JieliBatteryFeature.CreateAsync(write, notify, context.Time, authStepTimeout ?? TimeSpan.FromSeconds(3), cancellationToken);
            return battery is null ? [] : [battery];
        }
        catch (Exception)
        {
            // Timeouts and Bluetooth errors just mean no driver features.
            return [];
        }
    }
}

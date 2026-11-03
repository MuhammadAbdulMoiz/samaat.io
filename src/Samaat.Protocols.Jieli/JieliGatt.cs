using Samaat.Transport;

namespace Samaat.Protocols.Jieli;

/// <summary>Jieli's RCSP control service over BLE: write commands to AE01, receive on AE02.</summary>
public static class JieliGatt
{
    public static readonly Guid Service = BluetoothUuid.FromShort(0xAE00);

    public static readonly Guid Write = BluetoothUuid.FromShort(0xAE01);

    public static readonly Guid Notify = BluetoothUuid.FromShort(0xAE02);
}

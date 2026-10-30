using System.Buffers.Binary;
using Samaat.Core.Battery;
using Samaat.Transport;

namespace Samaat.Protocols.Standard;

/// <summary>Bluetooth SIG assigned numbers used by the Battery Service.</summary>
public static class GattUuids
{
    public static readonly Guid BatteryService = BluetoothUuid.FromShort(0x180F);
    public static readonly Guid BatteryLevel = BluetoothUuid.FromShort(0x2A19);
    public static readonly Guid CharacteristicPresentationFormat = BluetoothUuid.FromShort(0x2904);
}

/// <summary>Characteristic Presentation Format descriptor (0x2904), 7 bytes.</summary>
public sealed record PresentationFormat(byte Format, sbyte Exponent, ushort Unit, byte Namespace, ushort Description)
{
    /// <summary>Namespace 0x01 is the Bluetooth SIG namespace.</summary>
    public const byte SigNamespace = 0x01;

    // SIG namespace description values ("Namespace Descriptors" in Assigned Numbers).
    public const ushort DescriptionLeft = 0x010D;
    public const ushort DescriptionRight = 0x010E;

    public static PresentationFormat? Parse(ReadOnlySpan<byte> value)
    {
        if (value.Length < 7)
        {
            return null;
        }

        return new PresentationFormat(
            value[0],
            unchecked((sbyte)value[1]),
            BinaryPrimitives.ReadUInt16LittleEndian(value[2..]),
            value[4],
            BinaryPrimitives.ReadUInt16LittleEndian(value[5..]));
    }

    /// <summary>The earbud this characteristic describes, if the descriptor names one.</summary>
    public BatteryComponent? Position => (Namespace, Description) switch
    {
        (SigNamespace, DescriptionLeft) => BatteryComponent.Left,
        (SigNamespace, DescriptionRight) => BatteryComponent.Right,
        _ => null,
    };
}

public static class BatteryLevelParser
{
    /// <summary>Battery Level (0x2A19) is one unsigned byte, 0–100. Anything else is rejected.</summary>
    public static bool TryParse(ReadOnlySpan<byte> value, out int percent)
    {
        percent = 0;
        if (value.Length < 1 || value[0] > 100)
        {
            return false;
        }

        percent = value[0];
        return true;
    }
}

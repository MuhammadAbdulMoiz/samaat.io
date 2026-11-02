namespace Samaat.Core.Bluetooth;

public static class BluetoothServices
{
    private static readonly Guid BaseUuid = Guid.Parse("00000000-0000-1000-8000-00805f9b34fb");

    /// <summary>Expands a 16/32-bit assigned number to its 128-bit UUID.</summary>
    public static Guid FromShort(uint value)
    {
        var bytes = BaseUuid.ToByteArray();
        // Guid stores its first group little-endian.
        bytes[0] = (byte)value;
        bytes[1] = (byte)(value >> 8);
        bytes[2] = (byte)(value >> 16);
        bytes[3] = (byte)(value >> 24);
        return new Guid(bytes);
    }

    private static readonly Dictionary<Guid, string> Known = new()
    {
        [FromShort(0x1101)] = "SPP",
        [FromShort(0x1108)] = "HSP",
        [FromShort(0x1112)] = "HSP",
        [FromShort(0x110A)] = "A2DP",
        [FromShort(0x110B)] = "A2DP",
        [FromShort(0x110C)] = "AVRCP",
        [FromShort(0x110E)] = "AVRCP",
        [FromShort(0x110F)] = "AVRCP",
        [FromShort(0x111E)] = "HFP",
        [FromShort(0x111F)] = "HFP",
        [FromShort(0x1124)] = "HID",
        [FromShort(0x1131)] = "HSP",
        [FromShort(0x1200)] = "PnP information",
        [FromShort(0x1203)] = "Generic audio",
    };

    public static string Label(Guid uuid)
    {
        if (Known.TryGetValue(uuid, out var name))
        {
            return name;
        }

        var bytes = uuid.ToByteArray();
        var isShort = bytes.AsSpan(4).SequenceEqual(BaseUuid.ToByteArray().AsSpan(4));
        return isShort ? $"Service 0x{BitConverter.ToUInt32(bytes, 0):X4}" : $"Vendor ({uuid})";
    }

    /// <summary>Readable names for service class UUIDs, each name once, in first-seen order.</summary>
    public static IReadOnlyList<string> Labels(IEnumerable<Guid> uuids) => uuids.Select(Label).Distinct().ToList();
}

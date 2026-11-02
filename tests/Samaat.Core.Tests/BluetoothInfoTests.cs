using Samaat.Core.Bluetooth;
using Samaat.Core.Devices;

namespace Samaat.Core.Tests;

public class BluetoothInfoTests
{
    private static byte[] Uint16(ushort v) => [0x09, (byte)(v >> 8), (byte)v];

    private static byte[] Seq(params byte[][] items)
    {
        var body = items.SelectMany(i => i).ToArray();
        return [0x35, (byte)body.Length, .. body];
    }

    private static byte[] Text(string s) => [0x25, (byte)s.Length, .. System.Text.Encoding.ASCII.GetBytes(s)];

    private static byte[] Record(byte[] classes, string? name = null)
    {
        var items = new List<byte[]> { Uint16(0x0000), new byte[] { 0x0A, 0, 1, 0, 1 }, Uint16(0x0001), classes };
        if (name is not null)
        {
            items.Add(Uint16(0x0100));
            items.Add(Text(name));
        }

        return Seq(items.ToArray());
    }

    private static byte[] Uuid16(ushort v) => [0x19, (byte)(v >> 8), (byte)v];

    // fe010000-1234-5678-abcd-00805f9b34fb: the custom channel on the Quantum earbuds (docs/hardware-tests/2026-11-02-quantum.md).
    private static readonly byte[] JieliUuid =
        [0x1C, 0xFE, 0x01, 0x00, 0x00, 0x12, 0x34, 0x56, 0x78, 0xAB, 0xCD, 0x00, 0x80, 0x5F, 0x9B, 0x34, 0xFB];

    [Fact]
    public void Sdp_ParsesA2dpRecordWithName()
    {
        var record = SdpRecord.Parse(Record(Seq(Uuid16(0x110B)), "JL_A2DP"));

        Assert.NotNull(record);
        Assert.Equal([BluetoothServices.FromShort(0x110B)], record.ServiceClasses);
        Assert.Equal("JL_A2DP", record.Name);
    }

    [Fact]
    public void Sdp_ParsesMultipleClassesAndVendor128BitUuid()
    {
        var avrcp = SdpRecord.Parse(Record(Seq(Uuid16(0x110E), Uuid16(0x110F))));
        var vendor = SdpRecord.Parse(Record(Seq(JieliUuid), "JL_SPP"));

        Assert.Equal(2, avrcp!.ServiceClasses.Count);
        Assert.Equal(Guid.Parse("fe010000-1234-5678-abcd-00805f9b34fb"), Assert.Single(vendor!.ServiceClasses));
    }

    [Fact]
    public void Sdp_SkipsUnrelatedAttributes_AndParsesLongLengths()
    {
        // attribute 0x0004 holds a nested protocol list; attribute 0x0101 a 2-byte-length string.
        var protocols = Seq(Seq(Uuid16(0x0100)), Seq(Uuid16(0x0019), [0x09, 0x03, 0x00]));
        var longText = new byte[] { 0x26, 0x00, 0x03, (byte)'a', (byte)'b', (byte)'c' };
        var body = new[]
        {
            Uint16(0x0004), protocols, Uint16(0x0001), Seq(Uuid16(0x1101)), Uint16(0x0101), longText,
        }.SelectMany(b => b).ToArray();

        var record = SdpRecord.Parse([0x36, (byte)(body.Length >> 8), (byte)body.Length, .. body]);

        Assert.Equal([BluetoothServices.FromShort(0x1101)], record!.ServiceClasses);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x35 })]
    [InlineData(new byte[] { 0x35, 0x10, 0x09 })]
    [InlineData(new byte[] { 0x09, 0x00, 0x01 })]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF })]
    public void Sdp_MalformedInput_ReturnsNullWithoutThrowing(byte[] bytes) => Assert.Null(SdpRecord.Parse(bytes));

    [Fact]
    public void Services_LabelsWellKnownAndVendorUuids_Distinct()
    {
        Guid[] uuids =
        [
            BluetoothServices.FromShort(0x110B), BluetoothServices.FromShort(0x110E), BluetoothServices.FromShort(0x110F),
            BluetoothServices.FromShort(0x110C), BluetoothServices.FromShort(0x111E), BluetoothServices.FromShort(0x1101),
            Guid.Parse("fe010000-1234-5678-abcd-00805f9b34fb"), BluetoothServices.FromShort(0x7777),
        ];

        Assert.Equal(
            ["A2DP", "AVRCP", "HFP", "SPP", "Vendor (fe010000-1234-5678-abcd-00805f9b34fb)", "Service 0x7777"],
            BluetoothServices.Labels(uuids));
    }

    [Theory]
    [InlineData("BTHENUM\\DEV_F228995A7E79", null, null)]
    [InlineData("BTHENUM\\{0000111e-0000-1000-8000-00805f9b34fb}_VID&000105d6_PID&000a", 0x05D6, 0x000A)]
    [InlineData("USB\\VID_046D&PID_C52B", 0x046D, 0xC52B)]
    public void HardwareIds_ParsesVidPid(string id, int? vid, int? pid)
    {
        var parsed = HardwareIds.TryParseVidPid(["junk", id]);

        Assert.Equal(vid, parsed?.Vid);
        Assert.Equal(pid, parsed?.Pid);
    }

    [Fact]
    public void Vendors_NamesKnownIds_AndNullForUnknown()
    {
        Assert.Equal("Apple", BluetoothVendors.Name(0x004C));
        Assert.Contains("Jieli", BluetoothVendors.Name(0x05D6));
        Assert.Null(BluetoothVendors.Name(0xFFF0));
    }

    [Fact]
    public void Address_FormatsAsColonHex() =>
        Assert.Equal("F2:28:99:5A:7E:79", DeviceDetails.FormatAddress(0xF228995A7E79));
}

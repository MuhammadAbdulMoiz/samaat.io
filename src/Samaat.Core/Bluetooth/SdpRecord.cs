using System.Text;

namespace Samaat.Core.Bluetooth;

/// <summary>
/// The parts of an SDP service record Samaat shows: the service class UUIDs (attribute 0x0001)
/// and the service name (attribute 0x0100). Only enough of the SDP data-element encoding
/// (Bluetooth Core Vol 3 Part B §3) to walk a record.
/// </summary>
public sealed record SdpRecord(IReadOnlyList<Guid> ServiceClasses, string? Name)
{
    private const ushort ServiceClassIdList = 0x0001;
    private const ushort ServiceName = 0x0100;

    /// <summary>Returns null for anything that is not a well-formed record; never throws.</summary>
    public static SdpRecord? Parse(byte[] bytes)
    {
        try
        {
            var root = ReadElement(bytes, 0, out var end);
            if (root.Type != Seq || end != bytes.Length)
            {
                return null;
            }

            var classes = new List<Guid>();
            string? name = null;
            var items = Children(root);
            for (var i = 0; i + 1 < items.Count; i += 2)
            {
                if (items[i].Type != 1)
                {
                    continue;
                }

                var attribute = (ushort)ToNumber(items[i].Data);
                var value = items[i + 1];
                if (attribute == ServiceClassIdList && value.Type == Seq)
                {
                    classes.AddRange(Children(value).Where(c => c.Type == 3).Select(c => ToGuid(c.Data)));
                }
                else if (attribute == ServiceName && value.Type == 4)
                {
                    name = Encoding.UTF8.GetString(value.Data);
                }
            }

            return new SdpRecord(classes, name);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private const int Seq = 6;

    private readonly record struct Element(int Type, byte[] Data);

    private static List<Element> Children(Element sequence)
    {
        var children = new List<Element>();
        var pos = 0;
        while (pos < sequence.Data.Length)
        {
            children.Add(ReadElement(sequence.Data, pos, out pos));
        }

        return children;
    }

    private static Element ReadElement(byte[] data, int pos, out int end)
    {
        if (pos >= data.Length)
        {
            throw new FormatException("truncated");
        }

        var header = data[pos++];
        var type = header >> 3;
        var sizeIndex = header & 7;
        int length;
        switch (sizeIndex)
        {
            case 0:
                length = type == 0 ? 0 : 1;
                break;
            case <= 4:
                length = sizeIndex switch { 1 => 2, 2 => 4, 3 => 8, _ => 16 };
                break;
            default:
            {
                var width = sizeIndex switch { 5 => 1, 6 => 2, _ => 4 };
                if (pos + width > data.Length)
                {
                    throw new FormatException("truncated");
                }

                length = (int)ToNumber(data.AsSpan(pos, width));
                pos += width;
                break;
            }
        }

        if (length < 0 || pos + length > data.Length)
        {
            throw new FormatException("truncated");
        }

        end = pos + length;
        return new Element(type, data[pos..end]);
    }

    private static ulong ToNumber(ReadOnlySpan<byte> big)
    {
        ulong n = 0;
        foreach (var b in big)
        {
            n = (n << 8) | b;
        }

        return n;
    }

    private static Guid ToGuid(byte[] data) => data.Length switch
    {
        2 => BluetoothServices.FromShort((uint)ToNumber(data)),
        4 => BluetoothServices.FromShort((uint)ToNumber(data)),
        16 => Guid.Parse(Convert.ToHexString(data).Insert(20, "-").Insert(16, "-").Insert(12, "-").Insert(8, "-")),
        _ => throw new FormatException("bad uuid"),
    };
}

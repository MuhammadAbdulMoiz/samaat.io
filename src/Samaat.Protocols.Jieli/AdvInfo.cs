using System.Diagnostics.CodeAnalysis;

namespace Samaat.Protocols.Jieli;

/// <summary>One bud's or the case's level from the AdvInfo push.</summary>
public readonly record struct BudLevel(int Percent, bool Charging);

/// <summary>
/// The <c>0xC2</c> AdvInfo push. Layout of the params after sn, from the capture on a real ParadoX-E:
/// VID [0..1], other ids [2..6], EDR address [7..12], [13] marker, left [14], right [15], case [16], [17] unknown.
/// Each level byte: low 7 bits are the percent, bit 7 is charging, <c>0x00</c> means not reported.
/// </summary>
public sealed record AdvInfo(BudLevel? Left, BudLevel? Right, BudLevel? Case)
{
    public const byte Op = 0xC2;

    private const int LeftIndex = 14;
    private const int MinLength = 17;

    public static bool TryParse(ReadOnlySpan<byte> parameters, [NotNullWhen(true)] out AdvInfo? info)
    {
        if (parameters.Length < MinLength)
        {
            info = null;
            return false;
        }

        info = new AdvInfo(Level(parameters[LeftIndex]), Level(parameters[LeftIndex + 1]), Level(parameters[LeftIndex + 2]));
        return true;
    }

    private static BudLevel? Level(byte value)
    {
        var percent = value & 0x7F;
        return value == 0 || percent > 100 ? null : new BudLevel(percent, (value & 0x80) != 0);
    }
}

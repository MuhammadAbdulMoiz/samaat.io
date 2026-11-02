using System.Text.RegularExpressions;

namespace Samaat.Core.Bluetooth;

public static partial class HardwareIds
{
    // Bluetooth nodes: VID&<4-digit source><4-digit vendor>_PID&<product>, source 0001 = Bluetooth SIG, 0002 = USB-IF.
    // USB-style nodes: VID_xxxx&PID_xxxx.
    [GeneratedRegex(@"VID&[0-9a-f]{4}(?<vid>[0-9a-f]{4})_PID&(?<pid>[0-9a-f]{4})|VID_(?<vid>[0-9a-f]{4})&PID_(?<pid>[0-9a-f]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VidPid();

    /// <summary>The first vendor/product ID pair found in a device's PnP hardware IDs.</summary>
    public static (int Vid, int Pid)? TryParseVidPid(IEnumerable<string> hardwareIds)
    {
        foreach (var id in hardwareIds)
        {
            if (VidPid().Match(id) is { Success: true } m)
            {
                return (Convert.ToInt32(m.Groups["vid"].Value, 16), Convert.ToInt32(m.Groups["pid"].Value, 16));
            }
        }

        return null;
    }
}

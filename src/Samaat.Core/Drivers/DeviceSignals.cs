using System.Text.RegularExpressions;

namespace Samaat.Core.Drivers;

/// <summary>
/// Everything a platform can observe about a paired device without speaking its protocol.
/// Used to pick a catalog entry and driver.
/// </summary>
public sealed record DeviceSignals(
    string Name,
    uint? ClassOfDevice = null,
    ushort? VendorId = null,
    ushort? ProductId = null,
    IReadOnlySet<Guid>? ServiceUuids = null,
    string? Manufacturer = null)
{
    public IReadOnlySet<Guid> Services => ServiceUuids ?? new HashSet<Guid>();
}

/// <summary>
/// A catalog entry's matching rule. Every criterion given must match, and more specific
/// signals score higher, so "this exact VID/PID" beats "name starts with Acme".
/// </summary>
public sealed record DeviceMatch(
    string? NamePattern = null,
    ushort? VendorId = null,
    ushort? ProductId = null,
    Guid? ServiceUuid = null)
{
    private const int VendorProductWeight = 100;
    private const int ServiceWeight = 50;
    private const int NameWeight = 10;

    /// <summary>Null when the device doesn't match; otherwise how specific the match is.</summary>
    public int? Score(DeviceSignals signals)
    {
        var score = 0;
        if (VendorId is { } vid)
        {
            if (signals.VendorId != vid || (ProductId is { } pid && signals.ProductId != pid))
            {
                return null;
            }

            score += VendorProductWeight;
        }

        if (ServiceUuid is { } service)
        {
            if (!signals.Services.Contains(service))
            {
                return null;
            }

            score += ServiceWeight;
        }

        if (NamePattern is { } pattern)
        {
            if (!Regex.IsMatch(signals.Name, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            {
                return null;
            }

            score += NameWeight;
        }

        return score == 0 ? null : score;
    }
}

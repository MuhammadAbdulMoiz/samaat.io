using System.Text.Json;
using System.Text.RegularExpressions;

namespace Samaat.Core.Profiles;

/// <summary>What must hold for a device to be this model. Unset fields are not checked; see docs/PLAN.md §4.3.</summary>
public sealed record ProfileMatch(int? Vid, int? Pid, IReadOnlyList<Guid> SdpUuids, Regex? NamePattern)
{
    /// <summary>How many fields are specified; more specific profiles win.</summary>
    public int Specificity => (Vid is null ? 0 : 1) + (Pid is null ? 0 : 1) + (SdpUuids.Count > 0 ? 1 : 0) + (NamePattern is null ? 0 : 1);
}

public sealed record DeviceProfile(
    string Id,
    string DisplayName,
    ProfileMatch Match,
    string? Adapter,
    JsonElement? Features,
    IReadOnlyList<string> VerifiedFirmware,
    string? Notes);

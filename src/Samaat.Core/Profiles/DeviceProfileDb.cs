using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Samaat.Core.Devices;

namespace Samaat.Core.Profiles;

/// <summary>The device profiles shipped in <c>devices/&lt;vendor&gt;/&lt;model&gt;.json</c>.</summary>
public sealed class DeviceProfileDb
{
    private static readonly JsonDocumentOptions Lenient = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    private DeviceProfileDb(IReadOnlyList<DeviceProfile> profiles, IReadOnlyList<string> errors)
    {
        Profiles = profiles;
        Errors = errors;
    }

    public static DeviceProfileDb Empty { get; } = new([], []);

    public IReadOnlyList<DeviceProfile> Profiles { get; }

    /// <summary>One line per skipped file: its path and why.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Loads every .json file (except those starting with "_") under <paramref name="directory"/>. Never throws; bad files are skipped and listed in <see cref="Errors"/>.</summary>
    public static DeviceProfileDb Load(string directory)
    {
        var profiles = new List<DeviceProfile>();
        var errors = new List<string>();
        if (!Directory.Exists(directory))
        {
            return new DeviceProfileDb(profiles, errors);
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).StartsWith('_')) // examples, same rule as DeviceCatalog
                .Order(StringComparer.Ordinal).ToList();
        }
        catch (Exception e)
        {
            return new DeviceProfileDb(profiles, [$"{directory}: {e.Message}"]);
        }

        foreach (var file in files)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file), Lenient);
                profiles.Add(Parse(doc.RootElement));
            }
            catch (Exception e)
            {
                errors.Add($"{Path.GetRelativePath(directory, file)}: {e.Message}");
            }
        }

        return new DeviceProfileDb(profiles, errors);
    }

    /// <summary>The most specific profile whose specified match fields all agree with the device, or null.</summary>
    public DeviceProfile? Match(DeviceIdentity identity)
    {
        DeviceProfile? best = null;
        foreach (var profile in Profiles)
        {
            if (Matches(profile.Match, identity) && (best is null || profile.Match.Specificity > best.Match.Specificity))
            {
                best = profile;
            }
        }

        return best;
    }

    private static bool Matches(ProfileMatch m, DeviceIdentity identity)
    {
        var details = identity.Details;
        if (m.Vid is { } vid && details?.VendorId != vid)
        {
            return false;
        }

        if (m.Pid is { } pid && details?.ProductId != pid)
        {
            return false;
        }

        if (m.SdpUuids.Count > 0 && (details is null || !m.SdpUuids.All(details.ServiceUuids.Contains)))
        {
            return false;
        }

        try
        {
            return m.NamePattern is null || m.NamePattern.IsMatch(identity.Name);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static DeviceProfile Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("expected a JSON object");
        }

        var id = RequiredString(root, "id");
        var displayName = RequiredString(root, "displayName");
        if (!root.TryGetProperty("match", out var match) || match.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("missing \"match\"");
        }

        int? vid = null, pid = null;
        if (match.TryGetProperty("deviceId", out var deviceId) && deviceId.ValueKind == JsonValueKind.Object)
        {
            vid = Hex(deviceId, "vid");
            pid = Hex(deviceId, "pid");
        }

        var uuids = new List<Guid>();
        if (match.TryGetProperty("sdpUuids", out var sdp) && sdp.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in sdp.EnumerateArray())
            {
                uuids.Add(Guid.Parse(item.GetString() ?? ""));
            }
        }

        Regex? name = null;
        if (match.TryGetProperty("namePattern", out var pattern) && pattern.ValueKind == JsonValueKind.String)
        {
            name = new Regex(pattern.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }

        var parsedMatch = new ProfileMatch(vid, pid, uuids, name);
        if (parsedMatch.Specificity == 0)
        {
            throw new FormatException("\"match\" specifies nothing, so it would match every device");
        }

        var firmware = root.TryGetProperty("verifiedFirmware", out var fw) && fw.ValueKind == JsonValueKind.Array
            ? fw.EnumerateArray().Select(e => e.GetString() ?? "").ToList()
            : [];

        return new DeviceProfile(
            id,
            displayName,
            parsedMatch,
            OptionalString(root, "adapter"),
            root.TryGetProperty("features", out var features) ? features.Clone() : null,
            firmware,
            OptionalString(root, "notes"));
    }

    private static string RequiredString(JsonElement element, string name) =>
        OptionalString(element, name) is { Length: > 0 } value ? value : throw new FormatException($"missing \"{name}\"");

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Hex(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        var text = value.GetString() ?? "";
        var digits = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        return int.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new FormatException($"\"{name}\" is not a hex number: {text}");
    }
}

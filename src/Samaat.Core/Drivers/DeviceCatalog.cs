using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Samaat.Core.Capabilities;
using Samaat.Core.Devices;

namespace Samaat.Core.Drivers;

/// <summary>One known model. <see cref="Verified"/> separates "tested on hardware" from "recognised".</summary>
public sealed record CatalogEntry(
    string Id,
    string DisplayName,
    string Manufacturer,
    string Driver,
    DeviceMatch Match,
    bool Verified,
    IReadOnlyList<string> VerifiedFirmware,
    IReadOnlySet<FeatureId> Features);

/// <summary>Known models, loaded from <c>devices/**/*.json</c>. Adding a model is a data change.</summary>
public sealed class DeviceCatalog
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private DeviceCatalog(IReadOnlyList<CatalogEntry> entries, IReadOnlyList<string> errors)
    {
        Entries = entries;
        Errors = errors;
    }

    public static DeviceCatalog Empty { get; } = new([], []);

    public IReadOnlyList<CatalogEntry> Entries { get; }

    /// <summary>Entries that failed to load, with the reason. Shown in diagnostics rather than crashing.</summary>
    public IReadOnlyList<string> Errors { get; }

    public static DeviceCatalog Parse(IEnumerable<string> documents)
    {
        var entries = new List<CatalogEntry>();
        var errors = new List<string>();
        foreach (var document in documents)
        {
            try
            {
                var dto = JsonSerializer.Deserialize<EntryDto>(document, Json) ?? throw new JsonException("Empty document.");
                entries.Add(dto.ToEntry());
            }
            catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
            {
                errors.Add(ex.Message);
            }
        }

        return new DeviceCatalog(entries, errors);
    }

    /// <summary>Loads every <c>*.json</c> under a directory, skipping files that start with "_" (examples).</summary>
    public static DeviceCatalog FromDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return Empty;
        }

        var files = Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith('_'))
            .Order(StringComparer.Ordinal);
        return Parse(files.Select(File.ReadAllText));
    }

    public CatalogEntry? Match(DeviceSignals signals) =>
        Entries
            .Select(e => (Entry: e, Score: e.Match.Score(signals)))
            .Where(x => x.Score is not null)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Entry)
            .FirstOrDefault();

    private sealed class EntryDto
    {
        public string? Id { get; set; }

        public string? DisplayName { get; set; }

        public string? Manufacturer { get; set; }

        public string? Driver { get; set; }

        public MatchDto? Match { get; set; }

        public bool Verified { get; set; }

        public List<string>? VerifiedFirmware { get; set; }

        public List<string>? Features { get; set; }

        public CatalogEntry ToEntry()
        {
            if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Driver) || Match is null)
            {
                throw new FormatException($"Catalog entry '{Id ?? "?"}' needs an id, a driver and a match.");
            }

            return new CatalogEntry(
                Id,
                DisplayName ?? Id,
                Manufacturer ?? "",
                Driver,
                Match.ToMatch(),
                Verified,
                VerifiedFirmware ?? [],
                (Features ?? []).Select(f => new FeatureId(f)).ToHashSet());
        }
    }

    private sealed class MatchDto
    {
        public string? NamePattern { get; set; }

        [JsonPropertyName("vendorId")]
        public string? VendorId { get; set; }

        [JsonPropertyName("productId")]
        public string? ProductId { get; set; }

        public Guid? ServiceUuid { get; set; }

        public DeviceMatch ToMatch() => new(NamePattern, ParseHex(VendorId), ParseHex(ProductId), ServiceUuid);

        private static ushort? ParseHex(string? value) => value switch
        {
            null => null,
            _ when value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) => ushort.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            _ => ushort.Parse(value, CultureInfo.InvariantCulture),
        };
    }
}

public static class CatalogCapabilities
{
    /// <summary>
    /// What a recognised model can do according to the catalog, for when no driver is implemented:
    /// listed features are "supported by your device, not yet by Samaat".
    /// </summary>
    public static IEnumerable<Capability> For(CatalogEntry entry, ConnectionState connection) =>
        entry.Features.Select(feature => new Capability(
            feature,
            DeviceSupport.Supported,
            ImplementationStatus.NotImplemented,
            Availability.Unavailable("No Samaat adapter for this model yet"),
            entry.Verified ? Verification.Verified : Verification.Unverified,
            CapabilityAccess.ReadOnly,
            CapabilitySource.Profile));
}

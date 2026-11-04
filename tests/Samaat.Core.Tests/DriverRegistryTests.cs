using Samaat.Core.Capabilities;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;

namespace Samaat.Core.Tests;

public class DeviceMatchTests
{
    private static readonly Guid VendorService = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void AllSpecifiedCriteriaMustMatch()
    {
        var match = new DeviceMatch(NamePattern: "^Acme Buds", ServiceUuid: VendorService);

        Assert.NotNull(match.Score(new DeviceSignals("Acme Buds 2", ServiceUuids: new HashSet<Guid> { VendorService })));
        Assert.Null(match.Score(new DeviceSignals("Acme Buds 2")));
        Assert.Null(match.Score(new DeviceSignals("Other", ServiceUuids: new HashSet<Guid> { VendorService })));
    }

    [Fact]
    public void MoreSpecificSignalsScoreHigher()
    {
        var signals = new DeviceSignals("Acme Buds", VendorId: 0x1234, ProductId: 0x0001, ServiceUuids: new HashSet<Guid> { VendorService });

        var byName = new DeviceMatch(NamePattern: "Acme").Score(signals);
        var byService = new DeviceMatch(ServiceUuid: VendorService).Score(signals);
        var byVidPid = new DeviceMatch(VendorId: 0x1234, ProductId: 0x0001).Score(signals);

        Assert.True(byVidPid > byService);
        Assert.True(byService > byName);
    }

    [Fact]
    public void VendorIdAlone_ScoresBelowAServiceAndAboveAName()
    {
        var signals = new DeviceSignals("Acme Buds", VendorId: 0x1234, ProductId: 0x0001, ServiceUuids: new HashSet<Guid> { VendorService });

        var vendorOnly = new DeviceMatch(VendorId: 0x1234).Score(signals);

        Assert.True(vendorOnly < new DeviceMatch(ServiceUuid: VendorService).Score(signals));
        Assert.True(vendorOnly > new DeviceMatch(NamePattern: "Acme").Score(signals));
        Assert.True(new DeviceMatch(VendorId: 0x1234, ProductId: 0x0001).Score(signals) > new DeviceMatch(ServiceUuid: VendorService).Score(signals));
    }

    [Fact]
    public void EmptyMatch_NeverMatches() => Assert.Null(new DeviceMatch().Score(new DeviceSignals("Anything")));

    [Fact]
    public void NameMatchIsCaseInsensitive() =>
        Assert.NotNull(new DeviceMatch(NamePattern: "^acme").Score(new DeviceSignals("ACME Buds")));
}

public class DeviceCatalogTests
{
    private const string AcmeJson = """
        {
          "id": "acme.buds-2",
          "displayName": "Acme Buds 2",
          "manufacturer": "Acme",
          "driver": "acme-v1",
          "match": { "namePattern": "^Acme Buds 2", "vendorId": "0x1234", "productId": "0x0002" },
          "verified": false,
          "features": ["battery.left", "battery.right", "battery.case", "noise.anc", "noise.transparency"]
        }
        """;

    [Fact]
    public void ParsesEntries()
    {
        var catalog = DeviceCatalog.Parse([AcmeJson]);

        var entry = Assert.Single(catalog.Entries);
        Assert.Equal("acme.buds-2", entry.Id);
        Assert.Equal((ushort)0x1234, entry.Match.VendorId);
        Assert.False(entry.Verified);
        Assert.Contains(FeatureId.NoiseCancelling, entry.Features);
    }

    [Fact]
    public void FindsBestMatch()
    {
        var generic = """{ "id": "acme.any", "displayName": "Acme", "manufacturer": "Acme", "driver": "acme-v1", "match": { "namePattern": "^Acme" }, "features": [] }""";
        var catalog = DeviceCatalog.Parse([generic, AcmeJson]);

        var entry = catalog.Match(new DeviceSignals("Acme Buds 2", VendorId: 0x1234, ProductId: 0x0002));

        Assert.Equal("acme.buds-2", entry?.Id);
    }

    private const string GenericJson = """{ "id": "generic.proto", "driver": "proto", "generic": true, "match": { "serviceUuid": "df21fe2c-2515-4fdb-8886-f12c4d67927c" }, "features": [] }""";
    private const string ModelByNameJson = """{ "id": "acme.model", "driver": "none", "match": { "namePattern": "^Acme" }, "features": [] }""";
    private static readonly Guid ProtoService = Guid.Parse("df21fe2c-2515-4fdb-8886-f12c4d67927c");

    [Fact]
    public void GenericFlag_IsParsed()
    {
        var catalog = DeviceCatalog.Parse([GenericJson, ModelByNameJson]);

        Assert.True(catalog.Entries[0].Generic);
        Assert.False(catalog.Entries[1].Generic);
    }

    [Fact]
    public void ModelEntry_BeatsAGenericOneEvenWithALowerScore()
    {
        var catalog = DeviceCatalog.Parse([GenericJson, ModelByNameJson]);

        var both = new DeviceSignals("Acme Pods", ServiceUuids: new HashSet<Guid> { ProtoService });
        var genericOnly = new DeviceSignals("Other Pods", ServiceUuids: new HashSet<Guid> { ProtoService });

        Assert.Equal("acme.model", catalog.Match(both)?.Id);
        Assert.Equal("generic.proto", catalog.Match(genericOnly)?.Id);
    }

    [Fact]
    public void InvalidEntry_IsReportedNotThrown()
    {
        var catalog = DeviceCatalog.Parse(["{ not json", AcmeJson]);

        Assert.Single(catalog.Entries);
        Assert.Single(catalog.Errors);
    }

    [Fact]
    public void LoadsFromDirectory_SkippingUnderscoreFiles()
    {
        var dir = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(dir.FullName, "acme-buds-2.json"), AcmeJson);
        File.WriteAllText(Path.Combine(dir.FullName, "_example.json"), AcmeJson.Replace("acme.buds-2", "example"));

        var catalog = DeviceCatalog.FromDirectory(dir.FullName);

        Assert.Equal(["acme.buds-2"], catalog.Entries.Select(e => e.Id));
    }

    [Fact]
    public void ShippedExampleEntry_Parses()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Samaat.slnx")))
        {
            root = root.Parent;
        }

        var example = File.ReadAllText(Path.Combine(root!.FullName, "devices", "_example.json"));
        var catalog = DeviceCatalog.Parse([example]);

        Assert.Empty(catalog.Errors);
        Assert.Equal("acme.buds-2", catalog.Entries.Single().Id);
    }

    [Fact]
    public void MissingDirectory_IsEmpty() => Assert.Empty(DeviceCatalog.FromDirectory("/does/not/exist").Entries);
}

public class DriverRegistryTests
{
    private static readonly DeviceCatalog Catalog = DeviceCatalog.Parse(["""
        { "id": "acme.buds-2", "displayName": "Acme Buds 2", "manufacturer": "Acme", "driver": "acme-v1",
          "match": { "namePattern": "^Acme Buds 2" }, "verified": true, "features": ["battery.left", "noise.anc"] }
        """]);

    [Fact]
    public void UnknownDevice_ResolvesToGeneric()
    {
        var resolution = new DriverRegistry(Catalog).Resolve(new DeviceSignals("Mystery Buds"));

        Assert.Equal(DriverResolutionKind.Generic, resolution.Kind);
        Assert.Null(resolution.Entry);
    }

    [Fact]
    public void KnownModelWithoutDriver_IsRecognisedButNotImplemented()
    {
        var resolution = new DriverRegistry(Catalog).Resolve(new DeviceSignals("Acme Buds 2"));

        Assert.Equal(DriverResolutionKind.KnownModelNoDriver, resolution.Kind);
        Assert.Equal("acme.buds-2", resolution.Entry?.Id);
        Assert.Contains("not built yet", resolution.Description);
    }

    [Fact]
    public void KnownModelWithDriver_ResolvesDriver()
    {
        var registry = new DriverRegistry(Catalog);
        var driver = new FakeDriver("acme-v1");
        registry.Register(driver);

        var resolution = registry.Resolve(new DeviceSignals("Acme Buds 2"));

        Assert.Equal(DriverResolutionKind.Driver, resolution.Kind);
        Assert.Same(driver, resolution.Driver);
    }

    private static readonly Guid ProtoService = Guid.Parse("df21fe2c-2515-4fdb-8886-f12c4d67927c");

    private static readonly DeviceCatalog ModelAndProtocol = DeviceCatalog.Parse([
        """{ "id": "acme.model", "displayName": "Acme Model", "driver": "none", "match": { "namePattern": "^Acme" }, "features": [] }""",
        """{ "id": "generic.proto", "driver": "proto", "generic": true, "match": { "serviceUuid": "df21fe2c-2515-4fdb-8886-f12c4d67927c" }, "features": [] }""",
    ]);

    [Fact]
    public void ModelWithoutDriver_GetsMatchingProtocolDriver_KeepsModelEntry()
    {
        var registry = new DriverRegistry(ModelAndProtocol);
        var driver = new FakeDriver("proto");
        registry.Register(driver);

        var resolution = registry.Resolve(new DeviceSignals("Acme Pods", ServiceUuids: new HashSet<Guid> { ProtoService }));

        Assert.Equal(DriverResolutionKind.Driver, resolution.Kind);
        Assert.Equal("acme.model", resolution.Entry!.Id);
        Assert.Same(driver, resolution.Driver);
    }

    [Fact]
    public void ModelWithoutDriver_NoProtocolMatch_StaysKnownModelNoDriver()
    {
        var registry = new DriverRegistry(ModelAndProtocol);
        registry.Register(new FakeDriver("proto"));

        var resolution = registry.Resolve(new DeviceSignals("Acme Pods"));

        Assert.Equal(DriverResolutionKind.KnownModelNoDriver, resolution.Kind);
        Assert.Null(resolution.Driver);
    }

    [Fact]
    public void ModelWithoutDriver_GenericDriverNotRegistered_StaysKnownModelNoDriver()
    {
        var resolution = new DriverRegistry(ModelAndProtocol).Resolve(new DeviceSignals("Acme Pods", ServiceUuids: new HashSet<Guid> { ProtoService }));

        Assert.Equal(DriverResolutionKind.KnownModelNoDriver, resolution.Kind);
        Assert.Null(resolution.Driver);
    }

    [Fact]
    public void DuplicateDriverIds_AreRejected()
    {
        var registry = new DriverRegistry(Catalog);
        registry.Register(new FakeDriver("acme-v1"));

        Assert.Throws<ArgumentException>(() => registry.Register(new FakeDriver("acme-v1")));
    }

    [Fact]
    public void CatalogCapabilities_KnownFeaturesAreSupportedButNotImplemented()
    {
        var entry = Catalog.Entries[0];

        var capabilities = new CapabilitySet(CatalogCapabilities.For(entry, ConnectionState.Connected));

        var anc = capabilities.Get(FeatureId.NoiseCancelling);
        Assert.Equal(DeviceSupport.Supported, anc.Device);
        Assert.Equal("Supported by your device, not yet by Samaat", anc.DisabledReason);
        Assert.Equal(Verification.Verified, anc.Verified);
        Assert.Equal(CapabilityUiState.Undetected, capabilities.Get(FeatureId.Transparency).UiState);
    }

    private sealed class FakeDriver(string id) : IEarbudDriver
    {
        public string Id { get; } = id;

        public string DisplayName => Id;

        public Task<IReadOnlyList<IFeature>> OpenAsync(DriverContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IFeature>>([]);
    }
}

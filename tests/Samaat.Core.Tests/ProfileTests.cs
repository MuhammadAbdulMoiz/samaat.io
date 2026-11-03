using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Core.Profiles;

namespace Samaat.Core.Tests;

public sealed class ProfileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"));

    public ProfileTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Write(string name, string json)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private const string Quantum = """
        {
          "id": "zero-lifestyle.quantum-zbuds",
          "displayName": "Zero Lifestyle Quantum Zbuds",
          "match": { "deviceId": { "vid": "0x05D6", "pid": "0x000A" }, "sdpUuids": [], "namePattern": "^Quantum$" },
          "adapter": null,
          "features": { "battery.combined": {} },
          "verifiedFirmware": [],
          "notes": "hardware test"
        }
        """;

    private static DeviceIdentity Identity(string name, int? vid = 0x05D6, int? pid = 0x000A, params Guid[] uuids) =>
        new("id", name, Details: new DeviceDetails { VendorId = vid, ProductId = pid, ServiceUuids = uuids });

    [Fact]
    public void Load_ReadsProfile()
    {
        Write("zero/quantum.json", Quantum);

        var db = DeviceProfileDb.Load(_dir);

        var p = Assert.Single(db.Profiles);
        Assert.Empty(db.Errors);
        Assert.Equal("zero-lifestyle.quantum-zbuds", p.Id);
        Assert.Equal("Zero Lifestyle Quantum Zbuds", p.DisplayName);
        Assert.Equal(0x05D6, p.Match.Vid);
        Assert.Equal(0x000A, p.Match.Pid);
        Assert.Null(p.Adapter);
        Assert.Equal("hardware test", p.Notes);
        Assert.True(p.Features!.Value.TryGetProperty("battery.combined", out _));
    }

    [Fact]
    public void Load_BadFilesAreSkippedAndReported()
    {
        Write("good.json", Quantum);
        Write("syntax.json", "{ nope");
        Write("noid.json", """{ "displayName": "x", "match": { "namePattern": "x" } }""");
        Write("badhex.json", """{ "id": "a", "displayName": "a", "match": { "deviceId": { "vid": "zz", "pid": "0x1" } } }""");
        Write("badregex.json", """{ "id": "b", "displayName": "b", "match": { "namePattern": "(" } }""");
        Write("nomatch.json", """{ "id": "c", "displayName": "c", "match": {} }""");

        var db = DeviceProfileDb.Load(_dir);

        Assert.Equal("zero-lifestyle.quantum-zbuds", Assert.Single(db.Profiles).Id);
        Assert.Equal(5, db.Errors.Count);
        Assert.All(new[] { "syntax.json", "noid.json", "badhex.json", "badregex.json", "nomatch.json" },
            f => Assert.Contains(db.Errors, e => e.Contains(f)));
    }

    [Fact]
    public void Load_MissingDirectory_IsEmptyNotAnError()
    {
        var db = DeviceProfileDb.Load(Path.Combine(_dir, "nope"));

        Assert.Empty(db.Profiles);
        Assert.Empty(db.Errors);
    }

    [Fact]
    public void Match_Quantum()
    {
        Write("q.json", Quantum);
        var db = DeviceProfileDb.Load(_dir);

        Assert.Equal("zero-lifestyle.quantum-zbuds", db.Match(Identity("Quantum"))?.Id);
    }

    [Fact]
    public void Match_SameVidPidOtherName_DoesNotMatch()
    {
        Write("q.json", Quantum);
        var db = DeviceProfileDb.Load(_dir);

        Assert.Null(db.Match(Identity("buds-sniffer earbuds")));
        Assert.Null(db.Match(Identity("Quantum", vid: 0x004C)));
        Assert.Null(db.Match(new DeviceIdentity("id", "Quantum")));
    }

    [Fact]
    public void Match_MostSpecificWins_AndSdpUuidsMustAllBePresent()
    {
        var vendor = Guid.Parse("fe010000-1234-5678-abcd-00805f9b34fb");
        Write("a.json", """{ "id": "generic", "displayName": "Generic", "match": { "deviceId": { "vid": "0x05D6", "pid": "0x000A" } } }""");
        Write("b.json", $$"""{ "id": "specific", "displayName": "Specific", "match": { "deviceId": { "vid": "0x05D6", "pid": "0x000A" }, "sdpUuids": ["{{vendor}}"], "namePattern": "^Q" } }""");
        var db = DeviceProfileDb.Load(_dir);

        Assert.Equal("specific", db.Match(Identity("Quantum", 0x05D6, 0x000A, vendor))?.Id);
        Assert.Equal("generic", db.Match(Identity("Quantum"))?.Id);
    }

    [Theory]
    [InlineData("buds.png", true)]
    [InlineData("missing.png", false)]
    [InlineData("../outside.png", false)] // a profile can't point outside its own folder
    public void Load_ResolvesImageNextToTheProfile(string image, bool expected)
    {
        Write("zero/buds.png", "png");
        Write("outside.png", "png");
        Write("zero/quantum.json", Quantum.Replace("\"notes\"", $"\"image\": \"{image}\", \"notes\""));

        var profile = DeviceProfileDb.Load(_dir).Profiles.Single();

        Assert.Equal(expected ? Path.Combine(_dir, "zero", "buds.png") : null, profile.Image);
    }

    [Theory]
    [InlineData("case.png", true)]
    [InlineData("missing.png", false)] // the parts are used only as a full set
    [InlineData("../outside.png", false)]
    public void Load_ResolvesPartImagesNextToTheProfile(string caseImage, bool expected)
    {
        Write("zero/left.png", "png");
        Write("zero/right.png", "png");
        Write("zero/case.png", "png");
        Write("outside.png", "png");
        Write("zero/quantum.json", Quantum.Replace("\"notes\"",
            $$"""  "images": { "left": "left.png", "right": "right.png", "case": "{{caseImage}}" }, "notes" """));

        var profile = DeviceProfileDb.Load(_dir).Profiles.Single();

        var folder = Path.Combine(_dir, "zero");
        Assert.Equal(
            expected ? new ProfilePartImages(Path.Combine(folder, "left.png"), Path.Combine(folder, "right.png"), Path.Combine(folder, "case.png")) : null,
            profile.PartImages);
    }

    [Fact]
    public void Load_WithoutImages_HasNoPartImages()
    {
        Write("quantum.json", Quantum);

        Assert.Null(DeviceProfileDb.Load(_dir).Profiles.Single().PartImages);
    }

    [Fact]
    public void Load_SkipsUnderscoreFiles()
    {
        Write("good.json", Quantum);
        Write("_example.json", Quantum.Replace("zero-lifestyle.quantum-zbuds", "example"));

        var db = DeviceProfileDb.Load(_dir);

        Assert.Equal("zero-lifestyle.quantum-zbuds", Assert.Single(db.Profiles).Id);
    }

    [Fact]
    public void Load_SkipsGenericCatalogEntries_WithoutAnError()
    {
        Write("generic/proto.json", """{ "id": "generic.proto", "driver": "proto", "generic": true, "match": { "serviceUuid": "df21fe2c-2515-4fdb-8886-f12c4d67927c" } }""");

        var db = DeviceProfileDb.Load(_dir);

        Assert.Empty(db.Profiles);
        Assert.Empty(db.Errors);
    }

    // DeviceCatalog (driver resolution) and DeviceProfileDb (Bluetooth details) read the same devices/ folder.
    [Fact]
    public void ShippedDevicesFolder_LoadsInBothLoaders_WithoutErrors()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Samaat.slnx")))
        {
            root = root.Parent;
        }

        var devices = Path.Combine(root!.FullName, "devices");
        var catalog = DeviceCatalog.FromDirectory(devices);
        var profiles = DeviceProfileDb.Load(devices);

        Assert.Empty(catalog.Errors);
        Assert.Empty(profiles.Errors);
        Assert.Contains(catalog.Entries, e => e.Id == "zero-lifestyle.quantum-zbuds");
        Assert.Contains(profiles.Profiles, p => p.Id == "zero-lifestyle.quantum-zbuds");
        Assert.All(new[] { "generic.fast-pair", "generic.jieli" }, id =>
        {
            Assert.True(catalog.Entries.Single(e => e.Id == id).Generic);
            Assert.DoesNotContain(profiles.Profiles, p => p.Id == id);
        });
        Assert.DoesNotContain(profiles.Profiles, p => p.Id == "acme.buds-2");
    }
}

using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Core.Profiles;
using Samaat.Protocols.FastPair;
using Samaat.Protocols.Jieli;

namespace Samaat.Protocols.Tests;

/// <summary>The real <c>devices/</c> folder: each model must keep resolving to its own entry.</summary>
public sealed class ShippedDeviceTests
{
    private static readonly Guid MessageStream = Guid.Parse("df21fe2c-2515-4fdb-8886-f12c4d67927c");
    private static readonly IReadOnlySet<Guid> WithMessageStream = new HashSet<Guid> { MessageStream };

    private static readonly string Devices = Path.Combine(RepoRoot(), "devices");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Samaat.slnx")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    private static DriverRegistry Registry()
    {
        var registry = new DriverRegistry(DeviceCatalog.FromDirectory(Devices));
        registry.Register(new JieliDriver());
        registry.Register(new FastPairDriver());
        return registry;
    }

    private static DeviceSignals T200(IReadOnlySet<Guid>? services) =>
        new("realme Buds T200 Lite", VendorId: 0x05D6, ProductId: 0x000A, ServiceUuids: services);

    [Fact]
    public void T200Lite_ResolvesToFastPair()
    {
        var resolution = Registry().Resolve(T200(WithMessageStream));

        Assert.Equal(DriverResolutionKind.Driver, resolution.Kind);
        Assert.Equal("fast-pair", resolution.Driver!.Id);
        Assert.Equal("realme.buds-t200-lite", resolution.Entry!.Id);
    }

    [Fact]
    public void T200Lite_WithoutTheMessageStreamService_DoesNotResolveToFastPair()
    {
        var resolution = Registry().Resolve(T200(new HashSet<Guid>()));

        Assert.NotEqual("fast-pair", resolution.Driver?.Id);
        Assert.NotEqual("fast-pair", resolution.Entry?.Driver);
    }

    [Fact]
    public void ParadoxE_StillResolvesToJieli()
    {
        var resolution = Registry().Resolve(new DeviceSignals("ParadoX-E", VendorId: 0x05D6, ProductId: 0x000A));

        Assert.Equal("jieli-rcsp", resolution.Driver!.Id);
        Assert.Equal("audionic.paradox-e", resolution.Entry!.Id);
    }

    [Fact]
    public void Quantum_StillResolvesToItsOwnEntry_EvenThoughItHasTheSameService()
    {
        var resolution = Registry().Resolve(new DeviceSignals("Quantum", VendorId: 0x05D6, ProductId: 0x000A, ServiceUuids: WithMessageStream));

        Assert.Equal("zero-lifestyle.quantum-zbuds", resolution.Entry!.Id);
        Assert.Equal(DriverResolutionKind.KnownModelNoDriver, resolution.Kind);
    }

    [Fact]
    public void T200LiteProfile_LoadsWithArtworkAndMatchesTheIdentity()
    {
        var db = DeviceProfileDb.Load(Devices);
        var identity = new DeviceIdentity("id", "realme Buds T200 Lite",
            Details: new DeviceDetails { VendorId = 0x05D6, ProductId = 0x000A, ServiceUuids = [MessageStream] });

        var profile = db.Match(identity)!;

        Assert.Empty(db.Errors);
        Assert.Equal("realme.buds-t200-lite", profile.Id);
        Assert.Equal("fast-pair", profile.Adapter);
        Assert.True(File.Exists(profile.Image));
        Assert.True(File.Exists(profile.PartImages!.Left));
        Assert.True(File.Exists(profile.PartImages.Right));
        Assert.True(File.Exists(profile.PartImages.Case));
    }

    [Fact]
    public void ParadoxAndQuantumProfiles_StillMatchTheirOwnIdentities()
    {
        var db = DeviceProfileDb.Load(Devices);
        DeviceIdentity Id(string name) => new("id", name, Details: new DeviceDetails { VendorId = 0x05D6, ProductId = 0x000A, ServiceUuids = [MessageStream] });

        Assert.Equal("audionic.paradox-e", db.Match(Id("ParadoX-E"))!.Id);
        Assert.Equal("zero-lifestyle.quantum-zbuds", db.Match(Id("Quantum"))!.Id);
    }
}

using Samaat.Core.Battery;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Core.Equalizer;
using Samaat.Core.NoiseControl;
using Samaat.Core.Profiles;
using Samaat.Transport;
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

    [Fact]
    public void UnknownFastPairDevice_GetsTheFastPairDriver()
    {
        var resolution = Registry().Resolve(new DeviceSignals("Acme Pods", ServiceUuids: WithMessageStream));

        Assert.Equal(DriverResolutionKind.Driver, resolution.Kind);
        Assert.Equal("fast-pair", resolution.Driver!.Id);
        Assert.Equal("generic.fast-pair", resolution.Entry!.Id);
        Assert.Equal("Fast Pair earbuds", resolution.Entry.DisplayName);
    }

    [Fact]
    public void UnknownJieliDevice_GetsTheJieliDriver()
    {
        var resolution = Registry().Resolve(new DeviceSignals("Some Buds", VendorId: 0x05D6, ProductId: 0x000A));

        Assert.Equal("jieli-rcsp", resolution.Driver!.Id);
        Assert.Equal("generic.jieli", resolution.Entry!.Id);
    }

    [Fact]
    public void UnknownJieliDeviceWithTheMessageStream_PrefersFastPair()
    {
        var resolution = Registry().Resolve(new DeviceSignals("Other Buds", VendorId: 0x05D6, ProductId: 0x000A, ServiceUuids: WithMessageStream));

        Assert.Equal("generic.fast-pair", resolution.Entry!.Id);
    }

    [Fact]
    public void MicrosoftHeadset_KeepsItsOwnEntry_EvenWithTheMessageStream()
    {
        var resolution = Registry().Resolve(new DeviceSignals("Microsoft Modern Wireless Headset", ServiceUuids: WithMessageStream));

        Assert.Equal("microsoft.modern-wireless-headset", resolution.Entry!.Id);
        Assert.Equal(DriverResolutionKind.KnownModelNoDriver, resolution.Kind);
    }

    [Fact]
    public void Quantum_WithItsRealServices_IsItsOwnEntryWithNoDriver()
    {
        var signals = new DeviceSignals("Quantum", VendorId: 0x05D6, ProductId: 0x000A,
            ServiceUuids: new HashSet<Guid> { Guid.Parse("fe010000-1234-5678-abcd-00805f9b34fb") });

        var resolution = Registry().Resolve(signals);

        Assert.Equal("zero-lifestyle.quantum-zbuds", resolution.Entry!.Id);
        Assert.Equal(DriverResolutionKind.KnownModelNoDriver, resolution.Kind);
        Assert.Null(resolution.Driver);
        Assert.NotEqual("fast-pair", resolution.Entry.Driver);
        Assert.Null(GenericEntry("generic.fast-pair").Match.Score(signals));
    }

    private static CatalogEntry GenericEntry(string id) => DeviceCatalog.FromDirectory(Devices).Entries.Single(e => e.Id == id);

    [Fact]
    public void GenericEntries_AreNotProfiles()
    {
        var unknown = new DeviceIdentity("id", "Some Buds", Details: new DeviceDetails { VendorId = 0x05D6, ProductId = 0x000A });

        Assert.Null(DeviceProfileDb.Load(Devices).Match(unknown));
    }

    [Theory]
    [InlineData("ParadoX-E")]
    [InlineData("realme Buds T200 Lite")]
    public void ModelProfiles_StillHaveTheirImages(string name)
    {
        var identity = new DeviceIdentity("id", name, Details: new DeviceDetails { VendorId = 0x05D6, ProductId = 0x000A, ServiceUuids = [MessageStream] });

        Assert.True(File.Exists(DeviceProfileDb.Load(Devices).Match(identity)!.Image));
    }

    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);

    private static DriverContext GenericJieli(IGattClient gatt) =>
        new(new DeviceSignals("Some Buds"), GenericEntry("generic.jieli"), new FakeTransports(gatt), TimeProvider.System);

    // These pass before the catalog change only if generic.jieli exists, so they fail until it does; the driver behaviour itself already exists.
    [Fact]
    public async Task GenericJieli_WithoutAe00_GivesNoFeaturesQuickly()
    {
        using var budget = new CancellationTokenSource(500);
        var started = DateTime.UtcNow;

        Assert.Empty(await new JieliDriver(Short, Short).OpenAsync(GenericJieli(new FakeJieliGatt()), budget.Token));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task GenericJieli_WithBudsThatIgnoreSysInfo_GivesBatteryOnly()
    {
        using var budget = new CancellationTokenSource(1500);

        var features = await new JieliDriver(Short, Short).OpenAsync(GenericJieli(new FakeBuds(BudsMode.SilentSysInfo).Gatt), budget.Token);

        Assert.IsAssignableFrom<IBatteryFeature>(Assert.Single(features));
        Assert.Empty(features.OfType<INoiseControlFeature>());
        Assert.Empty(features.OfType<IEqualizerFeature>());
        await ((IAsyncDisposable)features[0]).DisposeAsync();
    }
}

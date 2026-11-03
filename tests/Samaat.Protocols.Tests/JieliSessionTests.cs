using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Protocols.Jieli;
using Samaat.Transport;

namespace Samaat.Protocols.Tests;

public sealed class RcspAuthTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(100);

    private static async Task<bool> Auth(FakeBuds buds, CancellationToken cancellationToken = default)
    {
        var inbox = Channel.CreateUnbounded<byte[]>();
        await buds.Notify.SubscribeAsync(chunk => inbox.Writer.TryWrite(chunk));
        return await RcspAuth.RunAsync(buds.Write, inbox.Reader, Short, cancellationToken);
    }

    [Fact]
    public async Task AuthenticatesAgainstTheBuds() => Assert.True(await Auth(new FakeBuds()));

    [Fact]
    public async Task WrongCipherReply_Fails() => Assert.False(await Auth(new FakeBuds(BudsMode.WrongCipher)));

    [Fact]
    public async Task Silence_FailsWithinTheStepTimeout()
    {
        var started = DateTime.UtcNow;

        Assert.False(await Auth(new FakeBuds(BudsMode.Silent)));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task FailedWrite_Fails()
    {
        var buds = new FakeBuds();
        buds.Write.OnWrite = _ => false;

        Assert.False(await Auth(buds));
    }
}

public sealed class JieliBatteryFeatureTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);

    private static Task<JieliBatteryFeature?> Create(FakeBuds buds, FakeTimeProvider? time = null, CancellationToken cancellationToken = default) =>
        JieliBatteryFeature.CreateAsync(buds.Write, buds.Notify, time ?? new FakeTimeProvider(), Short, cancellationToken);

    [Fact]
    public async Task DescribesAnUnverifiedDeviceProtocolSourceWithoutClaimingToBeExhaustive()
    {
        await using var feature = (await Create(new FakeBuds()))!;

        Assert.Equal(
            new[] { BatteryComponent.Left, BatteryComponent.Right, BatteryComponent.Case }.ToHashSet(),
            feature.Components.ToHashSet());
        Assert.False(feature.ComponentsAreExhaustive);
        Assert.Equal(CapabilitySource.RuntimeQuery, feature.Source);
        Assert.Equal(Verification.Unverified, feature.Verification);
    }

    [Fact]
    public async Task InitialPush_GivesLeftAndRightButNoCase()
    {
        var time = new FakeTimeProvider();
        await using var feature = (await Create(new FakeBuds(), time))!;

        Assert.Equal(100, feature.Current.Get(BatteryComponent.Left)!.Percent);
        Assert.Equal(100, feature.Current.Get(BatteryComponent.Right)!.Percent);
        Assert.Null(feature.Current.Get(BatteryComponent.Case));
        Assert.Equal(time.GetUtcNow(), feature.Current.Get(BatteryComponent.Left)!.UpdatedAt);
    }

    [Fact]
    public async Task LaterPush_UpdatesCurrentAndRaisesChanged()
    {
        var buds = new FakeBuds();
        await using var feature = (await Create(buds))!;
        BatteryState? seen = null;
        feature.Changed += (_, state) => seen = state;

        buds.Push(Captured.AdvInfoWith(0x80 | 50, 40, 30));

        Assert.NotNull(seen);
        Assert.Equal(50, feature.Current.Get(BatteryComponent.Left)!.Percent);
        Assert.True(feature.Current.Get(BatteryComponent.Left)!.IsCharging);
        Assert.Equal(ChargingState.NotCharging, feature.Current.Get(BatteryComponent.Right)!.Charging);
        Assert.Equal(40, feature.Current.Get(BatteryComponent.Right)!.Percent);
        Assert.Equal(30, feature.Current.Get(BatteryComponent.Case)!.Percent);
    }

    [Fact]
    public async Task Refresh_ResubscribesAndReauthenticates()
    {
        var buds = new FakeBuds();
        await using var feature = (await Create(buds))!;
        buds.Push(Captured.AdvInfoWith(50, 40, 0));

        await feature.RefreshAsync();

        Assert.Equal(2, buds.AuthStarts);
        Assert.Equal(100, feature.Current.Get(BatteryComponent.Left)!.Percent);
        Assert.True(buds.Notify.IsSubscribed);
    }

    [Fact]
    public async Task Refresh_AfterAuthFailureDoesNotThrow()
    {
        var buds = new FakeBuds();
        await using var feature = (await Create(buds))!;
        buds.Mode = BudsMode.Silent;

        await feature.RefreshAsync();

        Assert.Equal(100, feature.Current.Get(BatteryComponent.Left)!.Percent);
    }

    [Fact]
    public async Task CommandsThatExpectAResponse_AreIgnoredWithoutWriting()
    {
        var buds = new FakeBuds();
        await using var feature = (await Create(buds))!;
        var writes = buds.Writes.Count;

        buds.Notify.Notify(Convert.FromHexString("FEDCBAC0070005030000000" + "0EF"));

        Assert.Equal(writes, buds.Writes.Count);
    }

    [Fact]
    public async Task Dispose_Unsubscribes()
    {
        var buds = new FakeBuds();
        var feature = (await Create(buds))!;

        await feature.DisposeAsync();

        Assert.False(buds.Notify.IsSubscribed);
    }

    [Theory]
    [InlineData(BudsMode.WrongCipher)]
    [InlineData(BudsMode.Silent)]
    [InlineData(BudsMode.NoPush)]
    [InlineData(BudsMode.MalformedPush)]
    [InlineData(BudsMode.NonBatteryPush)]
    public async Task WithoutAValidInitialBatteryPush_NoFeatureIsReturnedAndTheSubscriptionIsReleased(BudsMode mode)
    {
        var buds = new FakeBuds(mode);
        using var expiry = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        var feature = await Create(buds, cancellationToken: expiry.Token);

        Assert.Null(feature);
        Assert.False(buds.Notify.IsSubscribed);
    }
}

public sealed class JieliDriverTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);

    private static readonly CatalogEntry Entry = new(
        "audionic.paradox-e", "Audionic ParadoX-E", "Audionic", JieliDriver.DriverId,
        new DeviceMatch("^ParadoX-E$", 0x05D6, 0x000A), false, [], new HashSet<FeatureId>());

    private static DriverContext Context(IGattClient? gatt) =>
        new(new DeviceSignals("ParadoX-E"), Entry, new FakeTransports(gatt), TimeProvider.System);

    private static async Task<IReadOnlyList<IFeature>> Open(IGattClient? gatt, int budgetMs = 500)
    {
        using var budget = new CancellationTokenSource(budgetMs);
        return await new JieliDriver(Short).OpenAsync(Context(gatt), budget.Token);
    }

    [Fact]
    public void Identifies() => Assert.Equal(("jieli-rcsp", "Jieli RCSP"), (new JieliDriver().Id, new JieliDriver().DisplayName));

    [Fact]
    public async Task NoGatt_GivesNoFeatures() => Assert.Empty(await Open(null));

    [Fact]
    public async Task NoControlService_GivesNoFeatures() => Assert.Empty(await Open(new FakeJieliGatt()));

    [Fact]
    public async Task ServiceWithoutTheNotifyCharacteristic_GivesNoFeatures()
    {
        var write = new FakeJieliCharacteristic(JieliGatt.Write, canWrite: true, canNotify: false);
        Assert.Empty(await Open(new FakeJieliGatt(new FakeJieliService(JieliGatt.Service, write))));
    }

    [Fact]
    public async Task HappyPath_GivesOneBatteryFeature()
    {
        var features = await Open(new FakeBuds().Gatt);

        var feature = Assert.IsAssignableFrom<IBatteryFeature>(Assert.Single(features));
        Assert.Equal(100, feature.Current.Get(BatteryComponent.Left)!.Percent);
        await ((IAsyncDisposable)feature).DisposeAsync();
    }

    [Fact]
    public async Task DiscoveryThatNeverAnswers_GivesNoFeaturesOnceTheBudgetExpires()
    {
        var gatt = new FakeBuds().Gatt;
        gatt.OnGetServices = ct => Task.Delay(Timeout.Infinite, ct);
        var started = DateTime.UtcNow;

        Assert.Empty(await Open(gatt, budgetMs: 150));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData(BudsMode.NoPush)]
    [InlineData(BudsMode.MalformedPush)]
    [InlineData(BudsMode.NonBatteryPush)]
    [InlineData(BudsMode.Silent)]
    public async Task AuthOkButNoBatteryData_GivesNoFeaturesAndReleasesTheSubscription(BudsMode mode)
    {
        var buds = new FakeBuds(mode);

        Assert.Empty(await Open(buds.Gatt, budgetMs: 300));
        Assert.False(buds.Notify.IsSubscribed);
    }

    // The earbuds are only ever written to for the auth handshake: never an RCSP command frame.
    [Fact]
    public async Task EveryWrite_IsAnAuthStepAndNoRcspFrameIsEverSent()
    {
        var all = new List<byte[]>();

        foreach (var mode in Enum.GetValues<BudsMode>())
        {
            var buds = new FakeBuds(mode);
            using var expiry = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
            var feature = await JieliBatteryFeature.CreateAsync(buds.Write, buds.Notify, TimeProvider.System, Short, expiry.Token);
            if (feature is not null)
            {
                await feature.RefreshAsync();
                await feature.DisposeAsync();
            }

            all.AddRange(buds.Writes);

            var viaDriver = new FakeBuds(mode);
            await Open(viaDriver.Gatt, budgetMs: 300);
            all.AddRange(viaDriver.Writes);
        }

        Assert.NotEmpty(all);
        Assert.All(all, write =>
        {
            var isAuth = (write.Length == 17 && write[0] is 0x00 or 0x01) || write.AsSpan().SequenceEqual("\u0002pass"u8);
            Assert.True(isAuth, Convert.ToHexString(write));
            Assert.False(write.Length >= 3 && write[0] == 0xFE && write[1] == 0xDC && write[2] == 0xBA, "RCSP frame written");
        });
    }
}

public sealed class ParadoxCatalogTests
{
    private static string DevicesFolder()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Samaat.slnx")))
        {
            root = root.Parent;
        }

        return Path.Combine(root!.FullName, "devices");
    }

    private static DriverRegistry Registry()
    {
        var registry = new DriverRegistry(DeviceCatalog.FromDirectory(DevicesFolder()));
        registry.Register(new JieliDriver());
        return registry;
    }

    // Signals the way WindowsDeviceSource builds them: name, class of device, PnP VID/PID, SDP services.
    private static DeviceSignals Windows(string name) =>
        new(name, 0x240404, 0x05D6, 0x000A, new HashSet<Guid> { BluetoothUuid.FromShort(0x110B) });

    [Fact]
    public void ParadoxE_ResolvesToTheJieliDriver()
    {
        var catalog = DeviceCatalog.FromDirectory(DevicesFolder());
        var resolution = Registry().Resolve(Windows("ParadoX-E"));

        Assert.Empty(catalog.Errors);
        Assert.Equal(DriverResolutionKind.Driver, resolution.Kind);
        Assert.Equal("jieli-rcsp", resolution.Driver!.Id);
        Assert.Equal("audionic.paradox-e", resolution.Entry!.Id);
        Assert.Contains(FeatureId.BatteryLeft, resolution.Entry.Features);
        Assert.Contains(FeatureId.BatteryCase, resolution.Entry.Features);
    }

    [Fact]
    public void Quantum_StillResolvesToItsOwnEntry()
    {
        var resolution = Registry().Resolve(Windows("Quantum"));

        Assert.Equal("zero-lifestyle.quantum-zbuds", resolution.Entry!.Id);
        Assert.NotEqual(DriverResolutionKind.Driver, resolution.Kind);
    }
}

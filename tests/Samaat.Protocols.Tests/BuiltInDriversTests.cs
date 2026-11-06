using Samaat.Core.Drivers;
using Samaat.Drivers;
using Samaat.Protocols.Bose;
using Samaat.Protocols.FastPair;
using Samaat.Protocols.Jieli;
using Samaat.Protocols.Samsung;

namespace Samaat.Protocols.Tests;

/// <summary>The one list of drivers that ships, shared by the app, the CLI and these tests.</summary>
public sealed class BuiltInDriversTests
{
    /// <summary>Catalog entries for models that only get Windows-level information use this instead of a driver id.</summary>
    private const string NoDriver = "none";

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

    [Fact]
    public void Create_ReturnsEveryProtocolDriverOnce()
    {
        var ids = BuiltInDrivers.Create().Select(d => d.Id).ToList();

        Assert.Equal([JieliDriver.DriverId, FastPairDriver.DriverId, BoseBmapDriver.DriverId, SamsungDriver.DriverId], ids);
    }

    [Fact]
    public void Create_ReturnsNewInstancesEachTime()
    {
        var first = BuiltInDrivers.Create();
        var second = BuiltInDrivers.Create();

        Assert.All(first.Zip(second), pair => Assert.NotSame(pair.First, pair.Second));
    }

    [Fact]
    public void CreateRegistry_RegistersEveryDriverAndLoadsTheCatalog()
    {
        var registry = BuiltInDrivers.CreateRegistry(Devices);

        Assert.Equal(BuiltInDrivers.Create().Select(d => d.Id).Order(), registry.Drivers.Select(d => d.Id).Order());
        Assert.NotEmpty(registry.Catalog.Entries);
        Assert.Empty(registry.Catalog.Errors);
    }

    [Fact]
    public void CreateRegistry_WithAMissingFolder_StillRegistersTheDrivers()
    {
        var registry = BuiltInDrivers.CreateRegistry(Path.Combine(Path.GetTempPath(), $"samaat-missing-{Guid.NewGuid():N}"));

        Assert.Empty(registry.Catalog.Entries);
        Assert.Equal(4, registry.Drivers.Count);
    }

    /// <summary>A typo in a catalog's "driver" field would silently demote that model to "recognised, no driver".</summary>
    [Fact]
    public void EveryShippedCatalogEntry_NamesABuiltInDriverOrNone()
    {
        var known = BuiltInDrivers.Create().Select(d => d.Id).Append(NoDriver).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unknown = DeviceCatalog.FromDirectory(Devices).Entries
            .Where(e => !known.Contains(e.Driver))
            .Select(e => $"{e.Id}: driver \"{e.Driver}\"")
            .ToList();

        Assert.Empty(unknown);
    }
}

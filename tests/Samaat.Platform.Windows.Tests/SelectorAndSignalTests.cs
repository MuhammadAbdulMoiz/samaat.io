using Samaat.Core.Drivers;

namespace Samaat.Platform.Windows.Tests;

/// <summary>The strings Windows is queried with, and how its answers become <see cref="DeviceSignals"/>.</summary>
public sealed class SelectorAndSignalTests
{
    private static readonly Guid Container = new("0f6a3c2e-1b2d-4e5f-8a9b-0c1d2e3f4a5b");

    [Fact]
    public void ContainerSelector_IsTheAqsFilterWithBraces()
    {
        Assert.Equal("System.Devices.ContainerId:=\"{0f6a3c2e-1b2d-4e5f-8a9b-0c1d2e3f4a5b}\"", WindowsSelectors.ContainerId(Container));
    }

    [Fact]
    public void ContainerId_ReadsAGuid_AndIgnoresAnythingElse()
    {
        Assert.Equal(Container, WindowsSelectors.TryReadContainerId(new Dictionary<string, object> { [WindowsSelectors.ContainerIdProperty] = Container }));
        Assert.Null(WindowsSelectors.TryReadContainerId(new Dictionary<string, object> { [WindowsSelectors.ContainerIdProperty] = Container.ToString() }));
        Assert.Null(WindowsSelectors.TryReadContainerId(new Dictionary<string, object>()));
    }

    [Fact]
    public void Signals_UseTheEndpointName_ManufacturerAndIds()
    {
        var uuids = new HashSet<Guid> { Guid.NewGuid() };
        var properties = new Dictionary<string, object> { [WindowsSelectors.ManufacturerProperty] = "Soundcore" };

        var signals = WindowsSignals.Create("Liberty 4 NC", "LE-Liberty", 0x240404, (0x05AC, 0x2002), uuids, properties);

        Assert.Equal("Liberty 4 NC", signals.Name);
        Assert.Equal((uint?)0x240404, signals.ClassOfDevice);
        Assert.Equal((ushort)0x05AC, signals.VendorId);
        Assert.Equal((ushort)0x2002, signals.ProductId);
        Assert.Same(uuids, signals.ServiceUuids);
        Assert.Equal("Soundcore", signals.Manufacturer);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Signals_FallBackToTheBluetoothName_WhenTheEndpointHasNone(string? endpointName)
    {
        var signals = WindowsSignals.Create(endpointName, "Buds Pro", 0, null, new HashSet<Guid>(), new Dictionary<string, object>());

        Assert.Equal("Buds Pro", signals.Name);
        Assert.Null(signals.VendorId);
        Assert.Null(signals.ProductId);
        Assert.Null(signals.Manufacturer);
    }

    [Fact]
    public void Signals_IgnoreAManufacturerThatIsNotText()
    {
        var properties = new Dictionary<string, object> { [WindowsSelectors.ManufacturerProperty] = 42 };

        Assert.Null(WindowsSignals.Create("Buds", "Buds", 0, null, new HashSet<Guid>(), properties).Manufacturer);
    }
}

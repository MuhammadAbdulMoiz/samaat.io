using Samaat.Core.Capabilities;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;

namespace Samaat.Core.Tests;

public class DeviceCapabilitiesTests
{
    private static readonly CatalogEntry Acme = DeviceCatalog.Parse(["""
        { "id": "acme", "displayName": "Acme Buds", "manufacturer": "Acme", "driver": "acme-v1",
          "match": { "namePattern": "^Acme" }, "features": ["battery.left", "noise.anc"] }
        """]).Entries[0];

    [Fact]
    public void CoversEveryModule()
    {
        var all = DeviceCapabilities.For(new FakeDevice(battery: null)).Select(c => c.Feature).ToHashSet();

        Assert.Contains(FeatureId.BatteryLeft, all);
        Assert.Contains(FeatureId.NoiseCancelling, all);
        Assert.Contains(FeatureId.SwitchHost, all);
    }

    [Fact]
    public void RecognisedModelWithoutDriver_FillsUnknownsFromCatalog()
    {
        var diagnostics = new DeviceDiagnostics(new DeviceSignals("Acme Buds"), new DriverResolution(DriverResolutionKind.KnownModelNoDriver, Acme, null));
        var device = new FakeDevice(battery: null, extraFeatures: [diagnostics]);

        var set = new CapabilitySet(DeviceCapabilities.For(device));

        Assert.Equal("Supported by your device, not yet by Samaat", set.Get(FeatureId.NoiseCancelling).DisabledReason);
        Assert.Equal(CapabilityUiState.Undetected, set.Get(FeatureId.Transparency).UiState);
    }

    [Fact]
    public void ImplementedFeatures_WinOverCatalog()
    {
        var diagnostics = new DeviceDiagnostics(new DeviceSignals("Acme Buds"), new DriverResolution(DriverResolutionKind.KnownModelNoDriver, Acme, null));
        var battery = new FakeBattery([Battery.BatteryComponent.Left], exhaustive: false);
        var device = new FakeDevice(battery, extraFeatures: [diagnostics]);

        var left = new CapabilitySet(DeviceCapabilities.For(device)).Get(FeatureId.BatteryLeft);

        Assert.Equal(CapabilityUiState.Enabled, left.UiState);
    }
}

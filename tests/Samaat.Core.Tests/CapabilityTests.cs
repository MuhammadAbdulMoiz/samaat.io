using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Devices;

namespace Samaat.Core.Tests;

public class CapabilityTests
{
    private static Capability Supported(Availability? runtime = null, ImplementationStatus implementation = ImplementationStatus.Implemented) => new(
        FeatureId.BatteryLeft,
        DeviceSupport.Supported,
        implementation,
        runtime ?? Availability.Available,
        Verification.Verified,
        CapabilityAccess.ReadOnly,
        CapabilitySource.RuntimeQuery);

    [Fact]
    public void SupportedImplementedAvailable_IsEnabled()
    {
        var capability = Supported();

        Assert.Equal(CapabilityUiState.Enabled, capability.UiState);
        Assert.Null(capability.DisabledReason);
    }

    [Fact]
    public void SupportedButUnavailable_IsDisabledWithRuntimeReason()
    {
        var capability = Supported(Availability.Unavailable("Case closed"));

        Assert.Equal(CapabilityUiState.Disabled, capability.UiState);
        Assert.Equal("Case closed", capability.DisabledReason);
    }

    [Fact]
    public void SupportedButNotImplemented_SaysSoInsteadOfUnsupported()
    {
        var capability = Supported(implementation: ImplementationStatus.NotImplemented);

        Assert.Equal(CapabilityUiState.Disabled, capability.UiState);
        Assert.Equal("Supported by your device, not yet by Samaat", capability.DisabledReason);
    }

    [Fact]
    public void Unsupported_IsGreyedOutAsUnsupported()
    {
        var capability = Supported() with { Device = DeviceSupport.Unsupported };

        Assert.Equal(CapabilityUiState.Unsupported, capability.UiState);
        Assert.Equal("Not supported by this model", capability.DisabledReason);
    }

    [Fact]
    public void MissingFeature_IsUnknownNotUnsupported()
    {
        var set = new CapabilitySet([Supported()]);

        var missing = set.Get(FeatureId.BatteryCase);

        Assert.Equal(DeviceSupport.Unknown, missing.Device);
        Assert.Equal(CapabilityUiState.Undetected, missing.UiState);
    }

    [Fact]
    public void BatteryCapabilities_NoBatteryFeature_AllUnknown()
    {
        var device = new FakeDevice(battery: null);

        Assert.All(BatteryCapabilities.For(device), c => Assert.Equal(DeviceSupport.Unknown, c.Device));
    }

    [Fact]
    public void BatteryCapabilities_BudLevels_MakeTheOverallLevelAvailable()
    {
        // The overall figure is derived from the buds (BatteryState.SummaryPercent), so it isn't "couldn't detect".
        var device = new FakeDevice(new FakeBattery([BatteryComponent.Left, BatteryComponent.Right], exhaustive: false));

        var set = new CapabilitySet(BatteryCapabilities.For(device));

        Assert.Equal(CapabilityUiState.Enabled, set.Get(FeatureId.BatteryCombined).UiState);
    }

    [Fact]
    public void BatteryCapabilities_NonExhaustiveSource_MissingComponentsStayUnknown()
    {
        var device = new FakeDevice(new FakeBattery([BatteryComponent.Combined], exhaustive: false));

        var set = new CapabilitySet(BatteryCapabilities.For(device));

        Assert.Equal(CapabilityUiState.Enabled, set.Get(FeatureId.BatteryCombined).UiState);
        Assert.Equal(CapabilityUiState.Undetected, set.Get(FeatureId.BatteryLeft).UiState);
        Assert.Equal(CapabilityUiState.Undetected, set.Get(FeatureId.BatteryCase).UiState);
    }

    [Fact]
    public void BatteryCapabilities_ExhaustiveSource_MissingComponentsAreUnsupported()
    {
        var device = new FakeDevice(new FakeBattery([BatteryComponent.Left, BatteryComponent.Right], exhaustive: true));

        var set = new CapabilitySet(BatteryCapabilities.For(device));

        Assert.Equal(CapabilityUiState.Enabled, set.Get(FeatureId.BatteryLeft).UiState);
        Assert.Equal(CapabilityUiState.Unsupported, set.Get(FeatureId.BatteryCase).UiState);
    }

    [Fact]
    public void BatteryCapabilities_Disconnected_SupportedButDisabled()
    {
        var device = new FakeDevice(new FakeBattery([BatteryComponent.Left], exhaustive: true), ConnectionState.Disconnected);

        var left = new CapabilitySet(BatteryCapabilities.For(device)).Get(FeatureId.BatteryLeft);

        Assert.Equal(CapabilityUiState.Disabled, left.UiState);
        Assert.Equal("Device disconnected", left.DisabledReason);
    }
}

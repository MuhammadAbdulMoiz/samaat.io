using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Samaat.App.Features.Overview;
using Samaat.App.Features.Sound;
using Samaat.Core.Capabilities;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Simulator;

namespace Samaat.App.Tests;

/// <summary>
/// A catalogued model whose own driver needs the buds connected (the Audionic ParadoX-E over Jieli RCSP):
/// while disconnected the driver gives nothing, and the app must ask for a connection instead of calling the model unsupported.
/// </summary>
public class WaitingForDriverTests
{
    private static readonly CatalogEntry Entry =
        new("audionic.paradox-e", "Audionic ParadoX-E", "Audionic", "jieli-rcsp", new DeviceMatch(NamePattern: "x"), false, [], new HashSet<FeatureId>());

    private static SimulatedEarbudDevice Device(DriverResolutionKind kind = DriverResolutionKind.Driver, bool fallback = false, bool connected = false)
    {
        var device = new SimulatedEarbudDevice(new DeviceIdentity("p", "ParadoX-E"), SimulatedBatteryLayout.CombinedOnly,
            extraFeatures: [new DeviceDiagnostics(new DeviceSignals("ParadoX-E"), new DriverResolution(kind, kind == DriverResolutionKind.Generic ? null : Entry, kind == DriverResolutionKind.Driver ? new SimulatorDriver() : null, fallback))]);
        device.SetConnected(connected);
        return device;
    }

    [Fact]
    public void NoiseControl_DisconnectedModelDriver_AsksToConnect()
    {
        var vm = new NoiseControlViewModel(Device(), post: a => a());

        Assert.True(vm.IsUnavailable);
        Assert.Equal("Connect to use noise control", vm.UnavailableTitle);
        Assert.Equal("Connect your earbuds and Samaat will read noise control from them.", vm.UnavailableText);
    }

    [Theory]
    [InlineData(DriverResolutionKind.Generic, false)]
    [InlineData(DriverResolutionKind.KnownModelNoDriver, false)]
    [InlineData(DriverResolutionKind.Driver, true)]
    public void NoiseControl_NoModelDriver_KeepsUnsupportedWording(DriverResolutionKind kind, bool fallback)
    {
        var vm = new NoiseControlViewModel(Device(kind, fallback), post: a => a());

        Assert.Equal("Not available for this model yet", vm.UnavailableTitle);
        Assert.Contains("own protocol", vm.UnavailableText);
    }

    [Fact]
    public void NoiseControl_ConnectionChange_RefreshesWording()
    {
        var device = Device();
        var vm = new NoiseControlViewModel(device, post: a => a());
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        device.SetConnected(true);

        Assert.Equal("Not available for this model yet", vm.UnavailableTitle);
        Assert.Contains(nameof(NoiseControlViewModel.UnavailableTitle), changed);
        Assert.Contains(nameof(NoiseControlViewModel.UnavailableText), changed);
    }

    [Fact]
    public void Equalizer_DisconnectedModelDriver_AsksToConnect()
    {
        var device = Device();
        var vm = new EqualizerViewModel(device, post: a => a());
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal("Connect your earbuds to change their EQ presets.", vm.UnavailableText);

        device.SetConnected(true);

        Assert.Contains("own protocol", vm.UnavailableText);
        Assert.Contains(nameof(EqualizerViewModel.UnavailableText), changed);
    }

    [AvaloniaFact]
    public void Overview_DisconnectedModelDriver_AsksToConnect()
    {
        var vm = new DeviceViewModel(Device(), new(), TimeProvider.System, (_, _) => { });

        Assert.Equal("Connect the earbuds to read each bud and the case", vm.BatteryReasonText);
        Assert.Equal("Disconnected · Connect for full support", vm.StatusLineText);
        Assert.Equal("Connect for full support", vm.Details.TierText);
    }

    [AvaloniaFact]
    public void Overview_GenericDevice_KeepsBasicWording()
    {
        var vm = new DeviceViewModel(Device(DriverResolutionKind.Generic), new(), TimeProvider.System, (_, _) => { });

        Assert.Equal("Left, right and case aren't reported for this device", vm.BatteryReasonText);
        Assert.Equal("Disconnected · Basic support", vm.StatusLineText);
    }

    [AvaloniaFact]
    public void Details_ConnectionChange_RefreshesTier()
    {
        var device = Device();
        var vm = new DeviceViewModel(device, new(), TimeProvider.System, (_, _) => { });
        var changed = new List<string?>();
        vm.Details.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        device.SetConnected(true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Basic support", vm.Details.TierText);
        Assert.Contains(nameof(vm.Details.TierText), changed);
    }
}

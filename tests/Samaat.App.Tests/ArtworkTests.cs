using Avalonia.Headless.XUnit;
using Samaat.App.Features.Overview;
using Samaat.Core.Devices;
using Samaat.Simulator;

namespace Samaat.App.Tests;

public class ArtworkTests
{
    private static DeviceViewModel Create(string name, string? model = null, SimulatedBatteryLayout layout = SimulatedBatteryLayout.CombinedOnly) =>
        new(new SimulatedEarbudDevice(new DeviceIdentity("id", name, Model: model), layout), new(), TimeProvider.System, (_, _) => { });

    [AvaloniaTheory]
    [InlineData("Quantum", "Zero Lifestyle Quantum Zbuds")] // single Windows level, but the model says buds
    [InlineData("Galaxy Buds2 Pro", null)]
    [InlineData("AirPods Pro", null)]
    public void EarbudsShowBudArtwork_EvenWithOneBatteryLevel(string name, string? model)
    {
        var device = Create(name, model);

        Assert.True(device.HasBuds);
        Assert.Equal($"{name} earbuds", device.ArtworkLabel);
    }

    [AvaloniaFact]
    public void HeadsetShowsHeadsetArtwork()
    {
        var device = Create("Microsoft Modern Wireless Headset");

        Assert.False(device.HasBuds);
        Assert.Equal("Microsoft Modern Wireless Headset headset", device.ArtworkLabel);
    }
}

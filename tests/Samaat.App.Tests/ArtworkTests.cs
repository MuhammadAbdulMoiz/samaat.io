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

    [AvaloniaTheory]
    [InlineData("ParadoX-E", "Audionic", "Audionic ParadoX-E", "Audionic ParadoX-E")] // maker already in the model
    [InlineData("Quantum", "Zero Lifestyle", "Quantum Zbuds", "Zero Lifestyle · Quantum Zbuds")]
    [InlineData("Microsoft Modern Wireless Headset", "Microsoft", "Microsoft Modern Wireless Headset", "")] // all repeat the title
    [InlineData("SHP-9500", "HP", "SHP-9500", "HP")] // "HP" inside "SHP" is not the maker's name
    public void SubtitleDoesNotRepeatItself(string name, string manufacturer, string model, string expected)
    {
        var device = new DeviceViewModel(
            new SimulatedEarbudDevice(new DeviceIdentity("id", name, manufacturer, model), SimulatedBatteryLayout.CombinedOnly),
            new(), TimeProvider.System, (_, _) => { });

        Assert.Equal(expected, device.Subtitle);
    }

    [AvaloniaFact]
    public void HeadsetShowsHeadsetArtwork()
    {
        var device = Create("Microsoft Modern Wireless Headset");

        Assert.False(device.HasBuds);
        Assert.Equal("Microsoft Modern Wireless Headset headset", device.ArtworkLabel);
    }
}

using Avalonia.Headless.XUnit;
using Samaat.App.Features.Details;
using Samaat.App.Features.Overview;
using Samaat.Core.Devices;
using Samaat.Core.Profiles;
using Samaat.Simulator;

namespace Samaat.App.Tests;

public class BluetoothDetailsViewModelTests
{
    private static BluetoothDetailsViewModel Create(DeviceIdentity identity, DeviceProfile? profile = null)
    {
        var device = new SimulatedEarbudDevice(identity, SimulatedBatteryLayout.CombinedOnly);
        return new DeviceViewModel(device, new(), TimeProvider.System, (_, _) => { }, profile).Details;
    }

    private static string Row(BluetoothDetailsViewModel info, string label) => info.Rows.Single(r => r.Label == label).Value;

    private static DeviceProfile Profile(string? adapter = null) => DeviceProfileDb.Load(WriteProfile(adapter)).Profiles.Single();

    private static string WriteProfile(string? adapter)
    {
        var dir = Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "p.json"), $$"""
            { "id": "x.y", "displayName": "Maker Model", "adapter": {{(adapter is null ? "null" : $"\"{adapter}\"")}},
              "match": { "namePattern": "^Buds$" } }
            """);
        return dir;
    }

    [AvaloniaFact]
    public void ShowsDetailsWhenReported()
    {
        var details = new DeviceDetails
        {
            Address = "F2:28:99:5A:7E:79", VendorId = 0x05D6, ProductId = 0x000A, Services = ["A2DP", "HFP"],
        };

        var info = Create(new DeviceIdentity("id", "Quantum", Details: details));

        Assert.Equal("Quantum", Row(info, "Name"));
        Assert.Equal("F2:28:99:5A:7E:79", Row(info, "Address"));
        Assert.Equal("0x05D6 / 0x000A", Row(info, "Vendor / product ID"));
        Assert.Contains("Jieli", Row(info, "Vendor"));
        Assert.Equal("A2DP, HFP", Row(info, "Services"));
        Assert.StartsWith("Basic support", Row(info, "Support"));
    }

    [AvaloniaFact]
    public void UnknownValuesSayNotReported_NeverBlank()
    {
        var info = Create(new DeviceIdentity("id", "Mystery"));

        Assert.All(info.Rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Value), r.Label));
        Assert.Equal("Not reported", Row(info, "Address"));
        Assert.Equal("Not reported", Row(info, "Vendor / product ID"));
        Assert.Equal("Not reported", Row(info, "Vendor"));
        Assert.Equal("Not reported", Row(info, "Services"));
        Assert.Equal("Not reported", Row(info, "Model"));
        Assert.Equal("Not in the device database", info.IdentifiedText);
    }

    [AvaloniaFact]
    public void ProfileNamesTheModelAndAdapterIsNoneYet()
    {
        var info = Create(new DeviceIdentity("id", "Buds"), Profile());

        Assert.Equal("Identified as Maker Model", info.IdentifiedText);
        Assert.Equal("Maker Model", Row(info, "Model"));
        Assert.Equal("Vendor adapter: none yet", info.AdapterText);
    }

    [AvaloniaFact]
    public void NamedAdapterIsShown()
    {
        var info = Create(new DeviceIdentity("id", "Buds"), Profile("jieli-rcsp"));

        Assert.Equal("Vendor adapter: jieli-rcsp", info.AdapterText);
    }

    [AvaloniaFact]
    public void OverviewSubtitleFallsBackToProfileName()
    {
        var device = new SimulatedEarbudDevice(new DeviceIdentity("id", "Buds"), SimulatedBatteryLayout.CombinedOnly);
        var vm = new DeviceViewModel(device, new(), TimeProvider.System, (_, _) => { }, Profile());

        Assert.Equal("Maker Model", vm.Subtitle);
    }
}

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

    private sealed class FakeInfo(string? firmware) : IDeviceInfoFeature
    {
        public string? FirmwareVersion { get; private set; } = firmware;

        public event EventHandler? Changed;

        public void Set(string? firmware)
        {
            FirmwareVersion = firmware;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static (DeviceViewModel Vm, BluetoothDetailsViewModel Details, Queue<Action> Posted) WithInfo(
        FakeInfo? info, string? identityFirmware = null)
    {
        var identity = new DeviceIdentity("id", "Buds", FirmwareVersion: identityFirmware);
        var device = new SimulatedEarbudDevice(identity, SimulatedBatteryLayout.CombinedOnly,
            extraFeatures: info is null ? null : [info]);
        var vm = new DeviceViewModel(device, new(), TimeProvider.System, (_, _) => { });
        var posted = new Queue<Action>();
        return (vm, new BluetoothDetailsViewModel(vm, null, posted.Enqueue), posted);
    }

    private static void Drain(Queue<Action> posted)
    {
        while (posted.TryDequeue(out var action))
        {
            action();
        }
    }

    [AvaloniaFact]
    public void FirmwareRow_ShowsTheFeatureValue()
    {
        var (_, details, _) = WithInfo(new FakeInfo("1.0.0"), identityFirmware: "9.9");

        Assert.Equal("1.0.0", Row(details, "Firmware"));
    }

    [AvaloniaFact]
    public void FirmwareRow_FallsBackToIdentityThenNotReported()
    {
        Assert.Equal("2.3", Row(WithInfo(null, identityFirmware: "2.3").Details, "Firmware"));
        Assert.Equal("2.3", Row(WithInfo(new FakeInfo(null), identityFirmware: "2.3").Details, "Firmware"));
        Assert.Equal("Not reported", Row(WithInfo(new FakeInfo(null)).Details, "Firmware"));
        Assert.Equal("Not reported", Row(WithInfo(null).Details, "Firmware"));
    }

    [AvaloniaFact]
    public async Task FirmwareRow_UpdatesOnTheUiThreadAfterAnOffThreadChange()
    {
        var info = new FakeInfo(null);
        var (_, details, posted) = WithInfo(info);

        await Task.Run(() => info.Set("1.0.0"));

        Assert.Equal("Not reported", Row(details, "Firmware")); // nothing touches the rows until the UI thread runs the post
        Assert.Single(posted);
        Drain(posted);
        Assert.Equal("1.0.0", Row(details, "Firmware"));
    }

    [AvaloniaFact]
    public void FirmwareRow_DoesNotUpdateAfterDisposal()
    {
        var info = new FakeInfo(null);
        var (_, details, posted) = WithInfo(info);

        details.Dispose();
        info.Set("1.0.0");
        Drain(posted);

        Assert.Empty(posted);
        Assert.Equal("Not reported", Row(details, "Firmware"));
    }

    [AvaloniaFact]
    public void FirmwareRow_PostedChangeIsIgnoredIfDisposedBeforeItRuns()
    {
        var info = new FakeInfo(null);
        var (_, details, posted) = WithInfo(info);

        info.Set("1.0.0");
        details.Dispose();
        Drain(posted);

        Assert.Equal("Not reported", Row(details, "Firmware"));
    }

    [AvaloniaFact]
    public void DisposingTheDeviceViewModel_StopsTheDetailsFromListening()
    {
        var info = new FakeInfo(null);
        var (vm, _, _) = WithInfo(info);

        vm.Dispose();
        info.Set("1.0.0");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("Not reported", Row(vm.Details, "Firmware"));
    }

    [AvaloniaFact]
    public void OverviewSubtitleFallsBackToProfileName()
    {
        var device = new SimulatedEarbudDevice(new DeviceIdentity("id", "Buds"), SimulatedBatteryLayout.CombinedOnly);
        var vm = new DeviceViewModel(device, new(), TimeProvider.System, (_, _) => { }, Profile());

        Assert.Equal("Maker Model", vm.Subtitle);
    }
}

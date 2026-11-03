using System.Text.RegularExpressions;
using Samaat.App.Features.Info;
using Samaat.Core.Bluetooth;
using Samaat.Core.Capabilities;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Core.NoiseControl;
using Samaat.Core.Profiles;
using Samaat.Simulator;

namespace Samaat.App.Tests;

public class DeviceInfoViewModelTests
{
    private static async Task<(DeviceInfoViewModel Vm, SimulatedEarbudDevice Pro)> CreateAsync()
    {
        await using var source = new SimulatorDeviceSource(TimeSpan.FromHours(1), commandLatency: TimeSpan.Zero);
        var pro = source.Devices.Single(d => d.Identity.Id == "sim-buds-pro");
        return (new DeviceInfoViewModel(pro, post: a => a()), pro);
    }

    // A Quantum Zbuds-like device: one Windows battery level and no protocol, so 14 of 15 features can't be detected.
    private static DeviceInfoViewModel Basic(DeviceDetails? details = null, DeviceProfile? profile = null, DeviceDiagnostics? diagnostics = null) =>
        new(new SimulatedEarbudDevice(new DeviceIdentity("q", "Quantum Zbuds", Details: details), SimulatedBatteryLayout.CombinedOnly,
            extraFeatures: diagnostics is null ? [] : [diagnostics]), post: a => a(), profile: profile);

    private static DeviceProfile Profile(ProfileMatch match) =>
        new("zero.quantum", "Zero Lifestyle Quantum Zbuds", match, null, null, [], null);

    private static ProfileMatch NameOnly() => new(null, null, [], new Regex("Quantum"));

    private static DeviceDiagnostics Catalogued(DriverResolutionKind kind, params Guid[] services)
    {
        var entry = new CatalogEntry("acme.pods", "Acme Pods", "Acme", "acme", new DeviceMatch(NamePattern: "x"), true, [], new HashSet<FeatureId>());
        return new DeviceDiagnostics(new DeviceSignals("Quantum Zbuds", ServiceUuids: services.ToHashSet()),
            new DriverResolution(kind, kind == DriverResolutionKind.Generic ? null : entry, null));
    }

    [Fact]
    public void BasicDevice_ShowsOneAvailableRowAndCollapsedGroupsForTheRest()
    {
        var vm = Basic();

        Assert.Equal("1 of 16 features available · Basic support", vm.SummaryText);
        Assert.Equal("Overall battery", Assert.Single(vm.AvailableRows).Name);
        Assert.Equal(["Battery", "Noise control", "Equalizer", "Connections"], vm.UnavailableGroups.Select(g => g.Module));
        Assert.Equal([3, 7, 1, 4], vm.UnavailableGroups.Select(g => g.Count));
        Assert.Equal("Noise control · 7 features", vm.UnavailableGroups[1].Title);
        Assert.Equal("Left, right and case battery", Assert.Single(vm.UnavailableGroups[0].Items).Name);
        Assert.Equal(7, vm.UnavailableGroups[1].Items.Count);
        Assert.Equal(4, vm.UnavailableGroups[3].Items.Count);
        Assert.All(vm.UnavailableGroups, g => Assert.False(string.IsNullOrWhiteSpace(g.Reason)));
    }

    [Fact]
    public async Task BudsPro_ListsMostFeaturesAsAvailable_WithoutANoiseGroup()
    {
        var (vm, _) = await CreateAsync();

        Assert.True(vm.AvailableRows.Count >= 12);
        Assert.DoesNotContain(vm.UnavailableGroups, g => g.Module == "Noise control");
        Assert.Equal("Simulator · Read / write · verified", vm.AvailableRows.Single(r => r.Name == "Noise cancelling").Meta);
    }

    [Fact]
    public void ProfileMatchAlone_IsRecognised_AndNeverSaysNotInTheCatalog()
    {
        var vm = Basic(profile: Profile(NameOnly()));

        Assert.True(vm.IsRecognised);
        Assert.Equal("Recognised as Zero Lifestyle Quantum Zbuds", vm.RecognitionTitle);
        Assert.Equal("Matched by name · Samaat adapter: none yet", vm.RecognitionMeta);
        Assert.DoesNotContain("catalog", vm.RecognitionTitle + vm.RecognitionMeta, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CatalogMatch_NamesTheCatalogModel_AndWinsOverTheProfile()
    {
        var vm = Basic(profile: Profile(NameOnly()), diagnostics: Catalogued(DriverResolutionKind.KnownModelNoDriver));

        Assert.True(vm.IsRecognised);
        Assert.Equal("Recognised as Acme Pods", vm.RecognitionTitle);
        Assert.Contains("adapter is not built yet", vm.RecognitionMeta);
    }

    [Fact]
    public void NeitherMatching_IsNotRecognised()
    {
        var vm = Basic(diagnostics: Catalogued(DriverResolutionKind.Generic));

        Assert.False(vm.IsRecognised);
        Assert.Equal("Not recognised", vm.RecognitionTitle);
        Assert.Equal("Samaat adapter: none yet", vm.RecognitionMeta);
    }

    [Theory]
    [InlineData("name", "Matched by name")]
    [InlineData("nameAndIds", "Matched by name and vendor/product ID")]
    [InlineData("servicesAndName", "Matched by services and name")]
    public void MatchExplanation_UsesOnlyThePopulatedFields(string kind, string expected)
    {
        var match = kind switch
        {
            "name" => NameOnly(),
            "nameAndIds" => new ProfileMatch(0x05D6, 0x000A, [], new Regex("Quantum")),
            _ => new ProfileMatch(null, null, [BluetoothServices.FromShort(0x111E)], new Regex("Quantum")),
        };

        Assert.StartsWith(expected + " ·", Basic(profile: Profile(match)).RecognitionMeta);
    }

    [Fact]
    public void TechnicalDetails_ShowFriendlyServiceLabels_WhileTheReportKeepsEveryRawUuid()
    {
        var hfp = BluetoothServices.FromShort(0x111E);
        var spp = BluetoothServices.FromShort(0x1101);
        var vendor = Guid.Parse("fe010000-1111-2222-3333-444455556666");
        var vm = Basic(new DeviceDetails { VendorId = 0x05D6, ProductId = 0x000A }, diagnostics: Catalogued(DriverResolutionKind.Generic, hfp, spp, vendor));

        var services = vm.TechnicalRows.Single(r => r.Label == "Services").Value;
        Assert.Contains("HFP", services);
        Assert.Contains("SPP", services);
        Assert.Contains("Vendor (fe010000-", services);
        Assert.DoesNotContain("0000111e", services);
        Assert.Contains(vm.TechnicalRows, r => r.Label == "Device ID" && r.Value == "q");

        var report = vm.BuildReport();
        foreach (var uuid in new[] { hfp, spp, vendor })
        {
            Assert.Contains(uuid.ToString(), report);
        }
    }

    [Fact]
    public async Task ShowsIdentityAndResolution()
    {
        var (vm, _) = await CreateAsync();

        Assert.Contains(vm.Identity, r => r.Label == "Model" && r.Value == "Buds Pro (simulated)");
        Assert.Contains(vm.Signals, r => r.Label == "Name" && r.Value == "Simulated Buds Pro");
        Assert.Equal(DriverResolutionKind.Driver, vm.ResolutionKind);
        Assert.Contains("Simulator", vm.ResolutionText);
    }

    [Fact]
    public async Task LedgerListsEveryCapabilityWithSourceAndVerification()
    {
        var (vm, _) = await CreateAsync();

        var anc = vm.Ledger.Single(r => r.Feature == "noise.anc");
        Assert.Equal("Available", anc.State);
        Assert.Equal("Simulator", anc.Source);
        Assert.True(anc.Verified);
        Assert.Equal("Read / write", anc.Access);
        Assert.Contains(vm.Ledger, r => r.Feature == "battery.combined" && r.State == "Not supported by this model");
    }

    [Fact]
    public async Task EventLogRecordsDeviceChanges()
    {
        var (vm, pro) = await CreateAsync();

        await pro.GetFeature<INoiseControlFeature>()!.SetModeAsync(NoiseMode.Transparency);
        pro.Tick();
        pro.SetConnected(false);

        Assert.Contains(vm.Events, e => e.Text == "Noise control: Transparency");
        Assert.Contains(vm.Events, e => e.Text.StartsWith("Battery: "));
        Assert.Equal("Disconnected", vm.Events[0].Text);
    }

    [Fact]
    public async Task EventLogIsBounded()
    {
        var (vm, pro) = await CreateAsync();

        for (var i = 0; i < 200; i++)
        {
            pro.Tick();
        }

        Assert.Equal(DeviceInfoViewModel.MaxEvents, vm.Events.Count);
    }

    [Fact]
    public async Task ReportContainsWhatAHardwareBugReportNeeds()
    {
        var (vm, _) = await CreateAsync();

        var report = vm.BuildReport();

        Assert.Contains("Simulated Buds Pro", report);
        Assert.Contains("Resolution:", report);
        Assert.Contains("noise.anc", report);
        Assert.Contains("Samaat ", report);
    }
}

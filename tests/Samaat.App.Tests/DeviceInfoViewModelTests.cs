using Samaat.App.Features.Info;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Core.NoiseControl;
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

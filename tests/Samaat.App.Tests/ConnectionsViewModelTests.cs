using Samaat.App.ViewModels;
using Samaat.Core.Connections;
using Samaat.Core.Devices;
using Samaat.Simulator;

namespace Samaat.App.Tests;

public class ConnectionsViewModelTests
{
    private static (ConnectionsViewModel Vm, SimulatedMultipoint Feature) Create(MultipointOptions? options = null)
    {
        var feature = new SimulatedMultipoint(
            options ?? new MultipointOptions(2, CanSwitch: true, CanDisconnect: true, CanToggleMultipoint: true),
            new MultipointState(true,
            [
                new HostLink("pc", "This PC", HostKind.Computer, HostLinkState.Active, IsThisHost: true),
                new HostLink("phone", "Phone", HostKind.Phone, HostLinkState.Connected, IsThisHost: false),
                new HostLink("tablet", "Tablet", HostKind.Tablet, HostLinkState.Paired, IsThisHost: false),
            ]),
            TimeSpan.Zero);
        var device = new SimulatedEarbudDevice(new DeviceIdentity("d", "Buds"), SimulatedBatteryLayout.BudsAndCase, extraFeatures: [feature]);
        return (new ConnectionsViewModel(device, post: a => a()), feature);
    }

    [Fact]
    public void ListsHostsActiveFirst()
    {
        var (vm, _) = Create();

        Assert.True(vm.HasMultipoint);
        Assert.Equal(["pc", "phone", "tablet"], vm.Hosts.Select(h => h.Id));
        Assert.Equal("Playing audio", vm.Hosts[0].StateText);
        Assert.Equal("Up to 2 devices at once", vm.LimitText);
    }

    [Fact]
    public void ActionsFollowStateAndOptions()
    {
        var (vm, _) = Create();
        var pc = vm.Hosts.Single(h => h.Id == "pc");
        var phone = vm.Hosts.Single(h => h.Id == "phone");
        var tablet = vm.Hosts.Single(h => h.Id == "tablet");

        Assert.False(pc.CanSwitch);
        Assert.False(pc.CanDisconnect);
        Assert.True(phone.CanSwitch);
        Assert.True(phone.CanDisconnect);
        Assert.True(tablet.CanSwitch);
        Assert.False(tablet.CanDisconnect);
        Assert.Equal("Connect", tablet.SwitchText);
        Assert.Equal("Switch audio here", phone.SwitchText);
    }

    [Fact]
    public async Task Switch_UpdatesHosts()
    {
        var (vm, feature) = Create();

        await vm.Hosts.Single(h => h.Id == "phone").SwitchCommand.ExecuteAsync(null);

        Assert.Equal("phone", feature.Current.ActiveHost?.Id);
        Assert.Equal("phone", vm.Hosts[0].Id);
        Assert.True(vm.Hosts[0].IsActive);
    }

    [Fact]
    public async Task FailedCommand_ShowsErrorAndKeepsState()
    {
        var (vm, feature) = Create();
        feature.FailNextCommand = true;

        await vm.Hosts.Single(h => h.Id == "phone").DisconnectCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorText);
        Assert.Equal(HostLinkState.Connected, feature.Current.Hosts.Single(h => h.Id == "phone").State);
    }

    [Fact]
    public async Task TurningMultipointOff_RollsBackOnFailure()
    {
        var (vm, feature) = Create();
        feature.FailNextCommand = true;

        vm.MultipointEnabled = false;
        await vm.PendingCommand;

        Assert.True(vm.MultipointEnabled);
        Assert.NotNull(vm.ErrorText);
    }

    [Fact]
    public async Task TurningMultipointOff_DropsStandbyHosts()
    {
        var (vm, _) = Create();

        vm.MultipointEnabled = false;
        await vm.PendingCommand;

        Assert.Equal("Not connected", vm.Hosts.Single(h => h.Id == "phone").StateText);
    }

    [Fact]
    public void NoFeature_ShowsOnlyThisComputerWithExplanation()
    {
        var device = new SimulatedEarbudDevice(new DeviceIdentity("h", "Headset"), SimulatedBatteryLayout.CombinedOnly);

        var vm = new ConnectionsViewModel(device, post: a => a());

        Assert.False(vm.HasMultipoint);
        Assert.Equal("Connected to this computer", vm.ThisComputerText);
        Assert.Contains("other devices", vm.UnavailableText);
    }
}

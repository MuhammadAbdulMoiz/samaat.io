using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Connections;
using Samaat.Core.Devices;
using Samaat.Simulator;

namespace Samaat.Core.Tests;

public class MultipointRulesTests
{
    private static readonly MultipointOptions TwoAtOnce = new(MaxConnections: 2, CanSwitch: true, CanDisconnect: true, CanToggleMultipoint: true);

    private static MultipointState State(bool enabled = true, params HostLink[] hosts) => new(enabled, hosts);

    private static HostLink Host(string id, HostLinkState state, bool thisHost = false) =>
        new(id, id, HostKind.Phone, state, thisHost);

    private static HostLinkState StateOf(MultipointState s, string id) => s.Hosts.Single(h => h.Id == id).State;

    [Fact]
    public void SwitchToConnected_SwapsActive()
    {
        var state = State(true, Host("pc", HostLinkState.Active, true), Host("phone", HostLinkState.Connected));

        var (next, result) = MultipointRules.SwitchTo(state, TwoAtOnce, "phone");

        Assert.True(result.Succeeded);
        Assert.Equal(HostLinkState.Active, StateOf(next, "phone"));
        Assert.Equal(HostLinkState.Connected, StateOf(next, "pc"));
    }

    [Fact]
    public void SwitchToPaired_WithFreeSlot_Connects()
    {
        var state = State(true, Host("pc", HostLinkState.Active, true), Host("tablet", HostLinkState.Paired));

        var (next, _) = MultipointRules.SwitchTo(state, TwoAtOnce, "tablet");

        Assert.Equal(HostLinkState.Active, StateOf(next, "tablet"));
        Assert.Equal(HostLinkState.Connected, StateOf(next, "pc"));
    }

    [Fact]
    public void SwitchToPaired_AtLimit_DropsTheStandbyHost()
    {
        var state = State(true,
            Host("pc", HostLinkState.Active, true),
            Host("phone", HostLinkState.Connected),
            Host("tablet", HostLinkState.Paired));

        var (next, result) = MultipointRules.SwitchTo(state, TwoAtOnce, "tablet");

        Assert.True(result.Succeeded);
        Assert.Equal(HostLinkState.Active, StateOf(next, "tablet"));
        Assert.Equal(HostLinkState.Connected, StateOf(next, "pc"));
        Assert.Equal(HostLinkState.Paired, StateOf(next, "phone"));
        Assert.Equal(2, next.ConnectedCount);
    }

    [Fact]
    public void SwitchWithMultipointOff_LeavesOnlyOneConnection()
    {
        var state = State(false, Host("pc", HostLinkState.Active, true), Host("phone", HostLinkState.Paired));

        var (next, _) = MultipointRules.SwitchTo(state, TwoAtOnce, "phone");

        Assert.Equal(HostLinkState.Active, StateOf(next, "phone"));
        Assert.Equal(HostLinkState.Paired, StateOf(next, "pc"));
    }

    [Fact]
    public void SwitchToActive_IsNoOp()
    {
        var state = State(true, Host("pc", HostLinkState.Active, true));

        var (next, result) = MultipointRules.SwitchTo(state, TwoAtOnce, "pc");

        Assert.True(result.Succeeded);
        Assert.Same(state, next);
    }

    [Fact]
    public void SwitchToUnknownOrUnavailable_IsRejected()
    {
        var state = State(true, Host("pc", HostLinkState.Active, true), Host("tv", HostLinkState.Unavailable));

        Assert.Equal(CommandStatus.Rejected, MultipointRules.SwitchTo(state, TwoAtOnce, "nope").Result.Status);
        Assert.Equal(CommandStatus.Rejected, MultipointRules.SwitchTo(state, TwoAtOnce, "tv").Result.Status);
    }

    [Fact]
    public void Switch_WhenNotSupported_IsUnsupported()
    {
        var state = State(true, Host("pc", HostLinkState.Active, true), Host("phone", HostLinkState.Connected));

        var (_, result) = MultipointRules.SwitchTo(state, TwoAtOnce with { CanSwitch = false }, "phone");

        Assert.Equal(CommandStatus.Unsupported, result.Status);
    }

    [Fact]
    public void DisconnectActive_PromotesStandby()
    {
        var state = State(true, Host("pc", HostLinkState.Connected, true), Host("phone", HostLinkState.Active));

        var (next, result) = MultipointRules.Disconnect(state, TwoAtOnce, "phone");

        Assert.True(result.Succeeded);
        Assert.Equal(HostLinkState.Paired, StateOf(next, "phone"));
        Assert.Equal(HostLinkState.Active, StateOf(next, "pc"));
    }

    [Fact]
    public void DisconnectThisHost_IsRejectedWithGuidance()
    {
        var state = State(true, Host("pc", HostLinkState.Active, true));

        var (next, result) = MultipointRules.Disconnect(state, TwoAtOnce, "pc");

        Assert.Equal(CommandStatus.Rejected, result.Status);
        Assert.Contains("Bluetooth settings", result.Message);
        Assert.Same(state, next);
    }

    [Fact]
    public void DisconnectPaired_IsRejected()
    {
        var state = State(true, Host("pc", HostLinkState.Active, true), Host("phone", HostLinkState.Paired));

        Assert.Equal(CommandStatus.Rejected, MultipointRules.Disconnect(state, TwoAtOnce, "phone").Result.Status);
    }

    [Fact]
    public void TurningMultipointOff_DropsStandbyHosts()
    {
        var state = State(true, Host("pc", HostLinkState.Active, true), Host("phone", HostLinkState.Connected));

        var (next, result) = MultipointRules.SetMultipointEnabled(state, TwoAtOnce, false);

        Assert.True(result.Succeeded);
        Assert.False(next.MultipointEnabled);
        Assert.Equal(HostLinkState.Paired, StateOf(next, "phone"));
        Assert.Equal(HostLinkState.Active, StateOf(next, "pc"));
    }

    [Fact]
    public void TogglingMultipoint_WhenNotSupported_IsUnsupported() =>
        Assert.Equal(CommandStatus.Unsupported,
            MultipointRules.SetMultipointEnabled(State(true), TwoAtOnce with { CanToggleMultipoint = false }, false).Result.Status);
}

public class ConnectionCapabilitiesTests
{
    [Fact]
    public void NoFeature_AllUnknown() =>
        Assert.All(ConnectionCapabilities.For(new FakeDevice(battery: null)), c => Assert.Equal(DeviceSupport.Unknown, c.Device));

    [Fact]
    public void Options_MapToCapabilities()
    {
        var feature = new SimulatedMultipoint(
            new MultipointOptions(MaxConnections: 2, CanSwitch: true, CanDisconnect: false, CanToggleMultipoint: false),
            new MultipointState(true, []), TimeSpan.Zero);
        var set = new CapabilitySet(ConnectionCapabilities.For(new FakeDevice(battery: null, extraFeatures: [feature])));

        Assert.Equal(CapabilityUiState.Enabled, set.Get(FeatureId.ConnectedHosts).UiState);
        Assert.Equal(CapabilityUiState.Enabled, set.Get(FeatureId.SwitchHost).UiState);
        Assert.Equal(CapabilityUiState.Unsupported, set.Get(FeatureId.DisconnectHost).UiState);
        Assert.Equal(CapabilityUiState.Unsupported, set.Get(FeatureId.MultipointToggle).UiState);
    }
}

public class SimulatedMultipointTests
{
    private static SimulatedMultipoint Create(TimeSpan? latency = null) => new(
        new MultipointOptions(2, true, true, true),
        new MultipointState(true,
        [
            new HostLink("pc", "This PC", HostKind.Computer, HostLinkState.Active, IsThisHost: true),
            new HostLink("phone", "Phone", HostKind.Phone, HostLinkState.Connected, IsThisHost: false),
        ]),
        latency ?? TimeSpan.Zero);

    [Fact]
    public async Task Switch_UpdatesStateAndRaisesChanged()
    {
        var feature = Create();
        MultipointState? raised = null;
        feature.Changed += (_, s) => raised = s;

        var result = await feature.SwitchToAsync("phone");

        Assert.True(result.Succeeded);
        Assert.Equal("phone", feature.Current.ActiveHost?.Id);
        Assert.Same(feature.Current, raised);
    }

    [Fact]
    public async Task RejectedCommand_LeavesStateAndDoesNotRaise()
    {
        var feature = Create();
        var raised = false;
        feature.Changed += (_, _) => raised = true;

        var result = await feature.DisconnectAsync("pc");

        Assert.Equal(CommandStatus.Rejected, result.Status);
        Assert.False(raised);
    }

    [Fact]
    public async Task FailNextCommand_Rejects()
    {
        var feature = Create();
        feature.FailNextCommand = true;

        Assert.Equal(CommandStatus.Rejected, (await feature.SwitchToAsync("phone")).Status);
        Assert.Equal("pc", feature.Current.ActiveHost?.Id);
    }
}

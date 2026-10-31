using Samaat.App.ViewModels;
using Samaat.Core.Devices;
using Samaat.Core.NoiseControl;
using Samaat.Simulator;

namespace Samaat.App.Tests;

public class NoiseControlViewModelTests
{
    private static readonly NoiseControlOptions Full = new(
        [NoiseMode.Off, NoiseMode.NoiseCancelling, NoiseMode.Transparency, NoiseMode.Adaptive],
        AncLevel: new LevelRange(0, 10),
        AmbientLevel: new LevelRange(0, 10),
        WindReduction: true,
        ConversationAwareness: true);

    private static (NoiseControlViewModel Vm, SimulatedNoiseControl Feature, SimulatedEarbudDevice Device) Create(NoiseControlOptions? options = null)
    {
        var feature = new SimulatedNoiseControl(options ?? Full, TimeSpan.Zero);
        var device = new SimulatedEarbudDevice(new DeviceIdentity("d", "Buds"), SimulatedBatteryLayout.BudsAndCase, extraFeatures: [feature]);
        return (new NoiseControlViewModel(device, post: a => a()), feature, device);
    }

    [Fact]
    public void Selector_ListsModesInCanonicalOrder()
    {
        var (vm, _, _) = Create();

        Assert.True(vm.IsSelector);
        Assert.Equal([NoiseMode.NoiseCancelling, NoiseMode.Adaptive, NoiseMode.Transparency, NoiseMode.Off], vm.Modes.Select(m => m.Mode));
        Assert.True(vm.Modes.Single(m => m.Mode == NoiseMode.Off).IsSelected);
    }

    [Fact]
    public async Task SelectingAMode_SendsCommandAndUpdatesSelection()
    {
        var (vm, feature, _) = Create();

        await vm.Modes.Single(m => m.Mode == NoiseMode.Transparency).SelectCommand.ExecuteAsync(null);

        Assert.Equal(NoiseMode.Transparency, feature.Current.Mode);
        Assert.Equal(NoiseMode.Transparency, vm.SelectedMode);
        Assert.True(vm.ShowAmbientLevel);
        Assert.False(vm.ShowAncLevel);
        Assert.Null(vm.ErrorText);
    }

    [Fact]
    public async Task FailedCommand_RollsBackAndExplains()
    {
        var (vm, feature, _) = Create();
        feature.FailNextCommand = true;

        await vm.Modes.Single(m => m.Mode == NoiseMode.NoiseCancelling).SelectCommand.ExecuteAsync(null);

        Assert.Equal(NoiseMode.Off, vm.SelectedMode);
        Assert.True(vm.Modes.Single(m => m.Mode == NoiseMode.Off).IsSelected);
        Assert.NotNull(vm.ErrorText);
    }

    [Fact]
    public void ChangeMadeOnTheBuds_IsReflected()
    {
        var (vm, feature, _) = Create();

        feature.ApplyFromDevice(feature.Current with { Mode = NoiseMode.NoiseCancelling, AncLevel = 4 });

        Assert.Equal(NoiseMode.NoiseCancelling, vm.SelectedMode);
        Assert.True(vm.ShowAncLevel);
        Assert.Equal(4, vm.AncLevel);
    }

    [Fact]
    public async Task AncLevel_IsSentToTheDevice()
    {
        var (vm, feature, _) = Create();
        await vm.Modes.Single(m => m.Mode == NoiseMode.NoiseCancelling).SelectCommand.ExecuteAsync(null);

        vm.AncLevel = 6;
        await vm.PendingCommand;

        Assert.Equal(6, feature.Current.AncLevel);
    }

    [Fact]
    public async Task AncOnlyDevice_IsAToggle()
    {
        var (vm, feature, _) = Create(new NoiseControlOptions([NoiseMode.Off, NoiseMode.NoiseCancelling]));

        Assert.True(vm.IsToggle);
        Assert.False(vm.IsSelector);
        vm.IsAncOn = true;
        await vm.PendingCommand;

        Assert.Equal(NoiseMode.NoiseCancelling, feature.Current.Mode);
    }

    [Fact]
    public async Task Toggles_AreSentToTheDevice()
    {
        var (vm, feature, _) = Create();

        vm.WindReduction = true;
        await vm.PendingCommand;
        vm.ConversationAwareness = true;
        await vm.PendingCommand;

        Assert.True(feature.Current.WindReduction);
        Assert.True(feature.Current.ConversationAwareness);
    }

    [Fact]
    public void NoFeature_ExplainsWhy()
    {
        var device = new SimulatedEarbudDevice(new DeviceIdentity("h", "Headset"), SimulatedBatteryLayout.CombinedOnly);

        var vm = new NoiseControlViewModel(device, post: a => a());

        Assert.True(vm.IsUnavailable);
        Assert.Contains("Samaat", vm.UnavailableText);
    }

    [Fact]
    public void Disconnected_DisablesControls()
    {
        var (vm, _, device) = Create();

        device.SetConnected(false);

        Assert.False(vm.CanControl);
        Assert.Equal("Connect your earbuds to change noise control.", vm.StatusText);
    }
}

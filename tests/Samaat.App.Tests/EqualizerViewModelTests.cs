using Samaat.App.Features.Sound;
using Samaat.Core.Devices;
using Samaat.Core.Equalizer;
using Samaat.Simulator;

namespace Samaat.App.Tests;

public class EqualizerViewModelTests
{
    private static (EqualizerViewModel Vm, SimulatedEqualizer Feature, SimulatedEarbudDevice Device) Create()
    {
        var feature = new SimulatedEqualizer([new EqPreset(0, "Standard"), new EqPreset(1, "Bass boost"), new EqPreset(2, "Voice")], TimeSpan.Zero);
        var device = new SimulatedEarbudDevice(new DeviceIdentity("d", "Buds"), SimulatedBatteryLayout.BudsAndCase, extraFeatures: [feature]);
        return (new EqualizerViewModel(device, post: a => a()), feature, device);
    }

    [Fact]
    public void ListsPresetsInDeviceOrder_WithCurrentSelected()
    {
        var (vm, _, _) = Create();

        Assert.Equal(["Standard", "Bass boost", "Voice"], vm.Presets.Select(p => p.Label));
        Assert.Equal("Standard", Assert.Single(vm.Presets, p => p.IsSelected).Label);
        Assert.True(vm.CanControl);
        Assert.False(vm.IsUnavailable);
    }

    [Fact]
    public async Task SelectingAPreset_SendsAndConfirms()
    {
        var (vm, feature, _) = Create();

        await vm.Presets[1].SelectCommand.ExecuteAsync(null);

        Assert.Equal(1, feature.CurrentPresetId);
        Assert.True(vm.Presets[1].IsSelected);
        Assert.False(vm.Presets[0].IsSelected);
        Assert.Null(vm.ErrorText);
        Assert.Equal("Bass boost preset", vm.StatusText);
    }

    [Fact]
    public async Task FailedCommand_RollsBackAndExplains()
    {
        var (vm, feature, _) = Create();
        feature.FailNextCommand = true;

        await vm.Presets[1].SelectCommand.ExecuteAsync(null);

        Assert.True(vm.Presets[0].IsSelected);
        Assert.False(vm.Presets[1].IsSelected);
        Assert.NotNull(vm.ErrorText);
    }

    [Fact]
    public void ChangeMadeOnTheBuds_IsReflected()
    {
        var (vm, feature, _) = Create();

        feature.ApplyFromDevice(2);

        Assert.True(vm.Presets[2].IsSelected);
        Assert.False(vm.Presets[0].IsSelected);
    }

    [Fact]
    public void DeviceWithoutEqualizer_IsUnavailable()
    {
        var device = new SimulatedEarbudDevice(new DeviceIdentity("d", "Buds"), SimulatedBatteryLayout.BudsOnly);
        var vm = new EqualizerViewModel(device, post: a => a());

        Assert.True(vm.IsUnavailable);
        Assert.Empty(vm.Presets);
        Assert.False(vm.CanControl);
        Assert.Contains("own protocol", vm.UnavailableText);
    }

    [Fact]
    public void Disconnected_DisablesControl()
    {
        var (vm, _, device) = Create();

        device.SetConnected(false);

        Assert.False(vm.CanControl);
        Assert.Equal("Connect your earbuds to change the EQ.", vm.StatusText);
    }
}

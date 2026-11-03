using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Devices;
using Samaat.Core.Equalizer;
using Samaat.Simulator;

namespace Samaat.Core.Tests;

public class EqualizerTests
{
    private static SimulatedEqualizer Create() =>
        new([new EqPreset(0, "Standard"), new EqPreset(1, "Bass boost")], TimeSpan.Zero);

    [Fact]
    public async Task SetPreset_ConfirmsAndRaisesChanged()
    {
        var eq = Create();
        int? published = null;
        eq.Changed += (_, id) => published = id;

        var result = await eq.SetPresetAsync(1);

        Assert.True(result.Succeeded);
        Assert.Equal(1, eq.CurrentPresetId);
        Assert.Equal(1, published);
    }

    [Fact]
    public async Task UnknownPreset_IsUnsupportedAndLeavesState()
    {
        var eq = Create();

        var result = await eq.SetPresetAsync(9);

        Assert.Equal(CommandStatus.Unsupported, result.Status);
        Assert.Equal(0, eq.CurrentPresetId);
    }

    [Fact]
    public async Task FailedCommand_LeavesState()
    {
        var eq = Create();
        eq.FailNextCommand = true;

        var result = await eq.SetPresetAsync(1);

        Assert.False(result.Succeeded);
        Assert.Equal(0, eq.CurrentPresetId);
    }

    [Fact]
    public void ApplyFromDevice_PublishesTheChange()
    {
        var eq = Create();

        eq.ApplyFromDevice(1);

        Assert.Equal(1, eq.CurrentPresetId);
    }

    [Fact]
    public async Task EqPresetsCapability_IsAvailableOnBudsProOnly()
    {
        await using var source = new SimulatorDeviceSource(TimeSpan.FromHours(1));

        var pro = new CapabilitySet(DeviceCapabilities.For(source.Devices[0])).Get(FeatureId.EqPresets);
        var lite = new CapabilitySet(DeviceCapabilities.For(source.Devices[1])).Get(FeatureId.EqPresets);

        Assert.Equal(CapabilityUiState.Enabled, pro.UiState);
        Assert.Equal(CapabilityUiState.Undetected, lite.UiState);
    }
}

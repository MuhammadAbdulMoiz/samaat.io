using Microsoft.Extensions.Time.Testing;
using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Devices;
using Samaat.Core.NoiseControl;
using Samaat.Simulator;

namespace Samaat.Core.Tests;

public class NoiseControlOptionsTests
{
    private static readonly NoiseControlOptions Full = new(
        [NoiseMode.Off, NoiseMode.NoiseCancelling, NoiseMode.Transparency, NoiseMode.Adaptive],
        AncLevel: new LevelRange(0, 10),
        AmbientLevel: new LevelRange(1, 5),
        WindReduction: true,
        ConversationAwareness: true);

    [Fact]
    public void Validate_UnsupportedMode_IsUnsupported()
    {
        var options = new NoiseControlOptions([NoiseMode.Off, NoiseMode.NoiseCancelling]);

        Assert.Equal(CommandStatus.Unsupported, options.ValidateMode(NoiseMode.Transparency)?.Status);
        Assert.Null(options.ValidateMode(NoiseMode.NoiseCancelling));
    }

    [Theory]
    [InlineData(-1, CommandStatus.Rejected)]
    [InlineData(11, CommandStatus.Rejected)]
    public void Validate_AncLevelOutOfRange_IsRejected(int level, CommandStatus expected) =>
        Assert.Equal(expected, Full.ValidateAncLevel(level)?.Status);

    [Fact]
    public void Validate_AncLevelWithoutRange_IsUnsupported() =>
        Assert.Equal(CommandStatus.Unsupported, new NoiseControlOptions([NoiseMode.Off]).ValidateAncLevel(3)?.Status);

    [Fact]
    public void Validate_AmbientInRange_IsValid() => Assert.Null(Full.ValidateAmbientLevel(5));
}

public class NoiseControlPresentationTests
{
    [Fact]
    public void NoFeature_IsNone() => Assert.Equal(NoiseControlLayout.None, NoiseControlPresentation.LayoutFor(null));

    [Fact]
    public void AncOnly_IsToggle() =>
        Assert.Equal(NoiseControlLayout.Toggle, NoiseControlPresentation.LayoutFor(new([NoiseMode.Off, NoiseMode.NoiseCancelling])));

    [Fact]
    public void SeveralModes_IsSelector() =>
        Assert.Equal(NoiseControlLayout.Selector,
            NoiseControlPresentation.LayoutFor(new([NoiseMode.Off, NoiseMode.NoiseCancelling, NoiseMode.Transparency])));

    [Fact]
    public void OnlyOffMode_IsNone() =>
        Assert.Equal(NoiseControlLayout.None, NoiseControlPresentation.LayoutFor(new([NoiseMode.Off])));

    [Fact]
    public void Modes_AreShownInCanonicalOrder()
    {
        var ordered = NoiseControlPresentation.OrderedModes(new([NoiseMode.Transparency, NoiseMode.Adaptive, NoiseMode.NoiseCancelling, NoiseMode.Off]));

        Assert.Equal([NoiseMode.NoiseCancelling, NoiseMode.Adaptive, NoiseMode.Transparency, NoiseMode.Off], ordered);
    }

    [Fact]
    public void AncSlider_OnlyInCancellingModeWithRange()
    {
        var options = new NoiseControlOptions([NoiseMode.Off, NoiseMode.NoiseCancelling, NoiseMode.Transparency], AncLevel: new(0, 10), AmbientLevel: new(0, 5));

        Assert.True(NoiseControlPresentation.ShowsAncLevel(options, NoiseMode.NoiseCancelling));
        Assert.False(NoiseControlPresentation.ShowsAncLevel(options, NoiseMode.Transparency));
        Assert.True(NoiseControlPresentation.ShowsAmbientLevel(options, NoiseMode.Transparency));
        Assert.False(NoiseControlPresentation.ShowsAmbientLevel(options, NoiseMode.Off));
    }
}

public class NoiseControlCapabilitiesTests
{
    [Fact]
    public void NoFeature_AllUnknown()
    {
        var device = new FakeDevice(battery: null);

        Assert.All(NoiseControlCapabilities.For(device), c => Assert.Equal(DeviceSupport.Unknown, c.Device));
    }

    [Fact]
    public void ExhaustiveOptions_MissingFeaturesUnsupported_PresentReadWrite()
    {
        var feature = new SimulatedNoiseControl(new NoiseControlOptions([NoiseMode.Off, NoiseMode.NoiseCancelling]), TimeSpan.Zero);
        var device = new FakeDevice(battery: null, extraFeatures: [feature]);

        var set = new CapabilitySet(NoiseControlCapabilities.For(device));

        Assert.Equal(CapabilityUiState.Enabled, set.Get(FeatureId.NoiseCancelling).UiState);
        Assert.Equal(CapabilityAccess.ReadWrite, set.Get(FeatureId.NoiseCancelling).Access);
        Assert.Equal(CapabilityUiState.Unsupported, set.Get(FeatureId.Transparency).UiState);
        Assert.Equal(CapabilityUiState.Unsupported, set.Get(FeatureId.AncLevel).UiState);
    }

    [Fact]
    public void Disconnected_SupportedButDisabled()
    {
        var feature = new SimulatedNoiseControl(new NoiseControlOptions([NoiseMode.Off, NoiseMode.NoiseCancelling]), TimeSpan.Zero);
        var device = new FakeDevice(battery: null, ConnectionState.Disconnected, [feature]);

        var anc = new CapabilitySet(NoiseControlCapabilities.For(device)).Get(FeatureId.NoiseCancelling);

        Assert.Equal(CapabilityUiState.Disabled, anc.UiState);
    }
}

public class SimulatedNoiseControlTests
{
    private static readonly NoiseControlOptions Full = new(
        [NoiseMode.Off, NoiseMode.NoiseCancelling, NoiseMode.Transparency, NoiseMode.Adaptive],
        AncLevel: new LevelRange(0, 10),
        AmbientLevel: new LevelRange(0, 10),
        WindReduction: true,
        ConversationAwareness: true);

    [Fact]
    public async Task SetMode_ConfirmsAndRaisesChanged()
    {
        var feature = new SimulatedNoiseControl(Full, TimeSpan.Zero);
        NoiseControlState? raised = null;
        feature.Changed += (_, s) => raised = s;

        var result = await feature.SetModeAsync(NoiseMode.Transparency);

        Assert.True(result.Succeeded);
        Assert.Equal(NoiseMode.Transparency, feature.Current.Mode);
        Assert.Same(feature.Current, raised);
    }

    [Fact]
    public async Task SetMode_WaitsForDeviceLatency()
    {
        var time = new FakeTimeProvider();
        var feature = new SimulatedNoiseControl(Full, TimeSpan.FromMilliseconds(300), time);

        var pending = feature.SetModeAsync(NoiseMode.NoiseCancelling);
        Assert.False(pending.IsCompleted);
        Assert.Equal(NoiseMode.Off, feature.Current.Mode);

        time.Advance(TimeSpan.FromMilliseconds(300));
        Assert.True((await pending).Succeeded);
        Assert.Equal(NoiseMode.NoiseCancelling, feature.Current.Mode);
    }

    [Fact]
    public async Task UnsupportedMode_DoesNotChangeState()
    {
        var feature = new SimulatedNoiseControl(new NoiseControlOptions([NoiseMode.Off, NoiseMode.NoiseCancelling]), TimeSpan.Zero);

        var result = await feature.SetModeAsync(NoiseMode.Adaptive);

        Assert.Equal(CommandStatus.Unsupported, result.Status);
        Assert.Equal(NoiseMode.Off, feature.Current.Mode);
    }

    [Fact]
    public async Task Levels_AreValidatedAndApplied()
    {
        var feature = new SimulatedNoiseControl(Full, TimeSpan.Zero);

        Assert.True((await feature.SetAncLevelAsync(7)).Succeeded);
        Assert.Equal(CommandStatus.Rejected, (await feature.SetAncLevelAsync(42)).Status);
        Assert.True((await feature.SetAmbientLevelAsync(3)).Succeeded);

        Assert.Equal(7, feature.Current.AncLevel);
        Assert.Equal(3, feature.Current.AmbientLevel);
    }

    [Fact]
    public async Task Toggles_AreApplied()
    {
        var feature = new SimulatedNoiseControl(Full, TimeSpan.Zero);

        await feature.SetWindReductionAsync(true);
        await feature.SetConversationAwarenessAsync(true);

        Assert.True(feature.Current.WindReduction);
        Assert.True(feature.Current.ConversationAwareness);
    }

    [Fact]
    public async Task FailNextCommand_RejectsOnceWithoutChangingState()
    {
        var feature = new SimulatedNoiseControl(Full, TimeSpan.Zero) { FailNextCommand = true };

        var first = await feature.SetModeAsync(NoiseMode.NoiseCancelling);
        var second = await feature.SetModeAsync(NoiseMode.NoiseCancelling);

        Assert.Equal(CommandStatus.Rejected, first.Status);
        Assert.True(second.Succeeded);
    }

    [Fact]
    public async Task UnsupportedToggles_AreUnsupported()
    {
        var feature = new SimulatedNoiseControl(new NoiseControlOptions([NoiseMode.Off, NoiseMode.NoiseCancelling]), TimeSpan.Zero);

        Assert.Equal(CommandStatus.Unsupported, (await feature.SetWindReductionAsync(true)).Status);
        Assert.Null(feature.Current.WindReduction);
    }
}

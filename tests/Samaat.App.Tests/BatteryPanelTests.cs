using Avalonia.Headless.XUnit;
using Samaat.App.Features.Overview;
using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Devices;

namespace Samaat.App.Tests;

public class BatteryPanelTests
{
    private sealed class CaseOnlyBattery : IBatteryFeature
    {
        public IReadOnlySet<BatteryComponent> Components { get; } = new HashSet<BatteryComponent> { BatteryComponent.Combined, BatteryComponent.Case };
        public bool ComponentsAreExhaustive => false;
        public CapabilitySource Source => CapabilitySource.Simulator;
        public Verification Verification => Verification.Unverified;
        public BatteryState Current { get; } = BatteryState.Empty.With(
            [new BatteryReading(BatteryComponent.Combined, 70, DateTimeOffset.UtcNow), new BatteryReading(BatteryComponent.Case, 40, DateTimeOffset.UtcNow)]);
        public event EventHandler<BatteryState>? Changed { add { } remove { } }
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Device : IEarbudDevice
    {
        public DeviceIdentity Identity { get; } = new("id", "Case Buds");
        public ConnectionState Connection => ConnectionState.Connected;
        public IReadOnlyCollection<IFeature> Features { get; } = [new CaseOnlyBattery()];
        public event EventHandler? ConnectionChanged { add { } remove { } }
    }

    [AvaloniaFact]
    public void CaseLevelWithoutBuds_KeepsAllFigures()
    {
        var vm = new DeviceViewModel(new Device(), new(), TimeProvider.System, (_, _) => { });

        Assert.Equal(["Overall", "Left bud", "Right bud", "Case"], vm.ShownFigures.Select(f => f.Label));
        Assert.Equal("40", vm.ShownFigures.Single(f => f.Label == "Case").Value);
        Assert.Equal("The left bud and right bud aren't reported by this device.", vm.BatteryReasonText);
    }
}

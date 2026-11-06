using Avalonia.Headless.XUnit;
using Samaat.App.Features.Overview;
using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Devices;

namespace Samaat.App.Tests;

/// <summary>The Battery card's Refresh never silently does nothing: no new reading means it says so.</summary>
public class BatteryRefreshTests
{
    private sealed class Battery(bool answers) : IBatteryFeature
    {
        public IReadOnlySet<BatteryComponent> Components { get; } = new HashSet<BatteryComponent> { BatteryComponent.Left, BatteryComponent.Right };
        public bool ComponentsAreExhaustive => false;
        public CapabilitySource Source => CapabilitySource.RuntimeQuery;
        public Verification Verification => Verification.Unverified;
        public BatteryState Current { get; } = BatteryState.Empty.With(
            [new BatteryReading(BatteryComponent.Left, 90, DateTimeOffset.UtcNow), new BatteryReading(BatteryComponent.Right, 90, DateTimeOffset.UtcNow)]);
        public event EventHandler<BatteryState>? Changed;

        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            if (answers)
            {
                // Same levels, same timestamp: still a reading the buds just sent.
                Changed?.Invoke(this, Current);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class Device(IBatteryFeature battery) : IEarbudDevice
    {
        public DeviceIdentity Identity { get; } = new("p", "ParadoX-E");
        public ConnectionState Connection => ConnectionState.Connected;
        public IReadOnlyCollection<IFeature> Features { get; } = [battery];
        public event EventHandler? ConnectionChanged { add { } remove { } }
    }

    private static (DeviceViewModel Vm, List<string> Messages) Create(bool answers)
    {
        var messages = new List<string>();
        var vm = new DeviceViewModel(new Device(new Battery(answers)), new(), TimeProvider.System, (_, _) => { }, onMessage: messages.Add);
        return (vm, messages);
    }

    [AvaloniaFact]
    public async Task Refresh_NoNewReading_SaysSo()
    {
        var (vm, messages) = Create(answers: false);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["ParadoX-E sent no new battery reading. Samaat shows its next update as soon as it arrives."], messages);
    }

    [AvaloniaFact]
    public async Task Refresh_NewReading_StaysQuiet()
    {
        var (vm, messages) = Create(answers: true);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Empty(messages);
    }
}

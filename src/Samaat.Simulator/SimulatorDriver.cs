using Samaat.Core.Devices;
using Samaat.Core.Drivers;

namespace Samaat.Simulator;

/// <summary>Stands in for a vendor driver in diagnostics; simulated devices build their features directly.</summary>
public sealed class SimulatorDriver : IEarbudDriver
{
    public static SimulatorDriver Instance { get; } = new();

    public string Id => "simulator";

    public string DisplayName => "Simulator";

    public Task<IReadOnlyList<IFeature>> OpenAsync(DriverContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IFeature>>([]);
}

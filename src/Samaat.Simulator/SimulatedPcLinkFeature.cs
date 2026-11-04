using Samaat.Core.Commands;
using Samaat.Core.Connections;

namespace Samaat.Simulator;

/// <summary>Connects or disconnects a simulated device after the simulator's usual command latency.</summary>
public sealed class SimulatedPcLinkFeature(SimulatedEarbudDevice device, TimeSpan latency, TimeProvider? time = null) : IPcLinkFeature
{
    private readonly SimulatedCommandChannel _channel = new(latency, time);

    public bool FailNextCommand
    {
        get => _channel.FailNextCommand;
        set => _channel.FailNextCommand = value;
    }

    public Task<CommandResult> ConnectAsync(CancellationToken ct = default) =>
        _channel.SendAsync(() => null, () => device.SetConnected(true), ct);

    public Task<CommandResult> DisconnectAsync(CancellationToken ct = default) =>
        _channel.SendAsync(() => null, () => device.SetConnected(false), ct);
}

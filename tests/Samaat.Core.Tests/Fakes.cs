using Samaat.Core.Audio;
using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Devices;

namespace Samaat.Core.Tests;

internal sealed class FakeBattery(IEnumerable<BatteryComponent> components, bool exhaustive) : IBatteryFeature
{
    public IReadOnlySet<BatteryComponent> Components { get; } = components.ToHashSet();

    public bool ComponentsAreExhaustive { get; } = exhaustive;

    public CapabilitySource Source => CapabilitySource.RuntimeQuery;

    public Verification Verification => Verification.Unverified;

    public BatteryState Current { get; set; } = BatteryState.Empty;

    public event EventHandler<BatteryState>? Changed;

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Changed?.Invoke(this, Current);
        return Task.CompletedTask;
    }
}

internal sealed class FakeDevice(
    IBatteryFeature? battery,
    ConnectionState connection = ConnectionState.Connected,
    IEnumerable<IFeature>? extraFeatures = null) : IEarbudDevice
{
    public DeviceIdentity Identity { get; set; } = new("fake", "Fake Buds");

    public ConnectionState Connection { get; set; } = connection;

    public IReadOnlyCollection<IFeature> Features { get; } =
        (battery is null ? Enumerable.Empty<IFeature>() : [battery]).Concat(extraFeatures ?? []).ToList();

    public event EventHandler? ConnectionChanged;

    public void RaiseConnectionChanged() => ConnectionChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class FakeAudioPipe(FakeAudioBackend owner) : IAudioPipe
{
    public int DelayMs { get; set; }

    public bool Disposed { get; private set; }

    /// <summary>Raise Stopped from inside Dispose, like a real pipe tearing down.</summary>
    public bool RaiseStoppedOnDispose { get; set; }

    public event EventHandler<Exception?>? Stopped;

    public void RaiseStopped(Exception? ex = null) => Stopped?.Invoke(this, ex);

    public void Dispose()
    {
        if (Disposed)
        {
            return;
        }

        Disposed = true;
        owner.PipeDisposed();
        if (RaiseStoppedOnDispose)
        {
            Stopped?.Invoke(this, null);
        }
    }
}

internal sealed class FakeAudioBackend : IAudioBackend
{
    private int _open;

    public List<AudioOutput> Outputs { get; set; } = [];

    public List<(string Source, string Target, int DelayMs)> Opens { get; } = [];

    public List<FakeAudioPipe> Pipes { get; } = [];

    public Exception? OpenThrows { get; set; }

    /// <summary>When set, Open blocks until it completes.</summary>
    public TaskCompletionSource? OpenGate { get; set; }

    public volatile bool OpenEntered;

    public bool RaiseStoppedOnDispose { get; set; }

    public bool Disposed { get; private set; }

    public int OpenPipes => Volatile.Read(ref _open);

    public event EventHandler? OutputsChanged;

    public IReadOnlyList<AudioOutput> GetOutputs() => Outputs.ToList();

    public IAudioPipe Open(string sourceId, string targetId, int delayMs)
    {
        if (OpenGate is not null)
        {
            OpenEntered = true;
            OpenGate.Task.Wait();
        }

        if (OpenThrows is not null)
        {
            throw OpenThrows;
        }

        var pipe = new FakeAudioPipe(this) { DelayMs = delayMs, RaiseStoppedOnDispose = RaiseStoppedOnDispose };
        lock (Pipes)
        {
            Opens.Add((sourceId, targetId, delayMs));
            Pipes.Add(pipe);
        }

        Interlocked.Increment(ref _open);
        return pipe;
    }

    public void PipeDisposed() => Interlocked.Decrement(ref _open);

    public void RaiseOutputsChanged() => OutputsChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose() => Disposed = true;
}

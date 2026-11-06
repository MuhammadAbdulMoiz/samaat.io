using Samaat.Core.Devices;

namespace Samaat.Core.Drivers;

/// <summary>When a device whose model declares its own driver should be rebuilt so the driver gets another try.</summary>
public static class DriverRetry
{
    /// <summary>
    /// A declared model driver gave nothing (tier below Full). Protocol-fallback probes are guesses and are not retried.
    /// </summary>
    public static bool NeedsDriverRetry(IEarbudDevice device) =>
        device.GetFeature<IDeviceDiagnostics>()?.Resolution is { Kind: DriverResolutionKind.Driver, ProtocolFallback: false }
        && device.Identity.Tier != SupportTier.Full;

    /// <summary>The model's own driver gave nothing because the buds aren't connected; connecting will retry it.</summary>
    public static bool WaitsForConnection(IEarbudDevice device) =>
        device.Connection != ConnectionState.Connected && NeedsDriverRetry(device);

    public static bool CanReuse(IEarbudDevice device) =>
        device.GetFeature<Battery.IBatteryFeature>() is not null && !NeedsDriverRetry(device);

    /// <summary>Calls <paramref name="retry"/> once per transition into Connected. Dispose to stop.</summary>
    public static IDisposable WatchForReconnect(IEarbudDevice device, Action retry)
    {
        var last = device.Connection;
        void OnChanged(object? sender, EventArgs e)
        {
            var now = device.Connection;
            var wasConnected = last == ConnectionState.Connected;
            last = now;
            if (now == ConnectionState.Connected && !wasConnected)
            {
                retry();
            }
        }

        device.ConnectionChanged += OnChanged;
        return new Watch(() => device.ConnectionChanged -= OnChanged);
    }

    private sealed class Watch(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}

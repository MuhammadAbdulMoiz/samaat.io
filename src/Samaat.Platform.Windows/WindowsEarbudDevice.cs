using Samaat.Core.Battery;
using Samaat.Core.Devices;
using Windows.Devices.Bluetooth;
using Samaat.Core.Diagnostics;

namespace Samaat.Platform.Windows;

internal sealed class WindowsEarbudDevice : IEarbudDevice, IAsyncDisposable
{
    private readonly BluetoothDevice _bluetooth;
    private readonly IBatteryFeature? _battery;
    private readonly WinRtGattClient? _gatt;

    public WindowsEarbudDevice(DeviceIdentity identity, BluetoothDevice bluetooth, IReadOnlyList<IFeature> features, WinRtGattClient? gatt)
    {
        Identity = identity;
        _bluetooth = bluetooth;
        _battery = features.OfType<IBatteryFeature>().FirstOrDefault();
        _gatt = gatt;
        Features = features;
        _bluetooth.ConnectionStatusChanged += OnConnectionStatusChanged;
    }

    public DeviceIdentity Identity { get; }

    /// <summary>The driver's session died with the connection; the next discovery rebuilds this device. Lives on the device so it can never mark its replacement.</summary>
    internal volatile bool SessionStale;

    public ConnectionState Connection => _bluetooth.ConnectionStatus == BluetoothConnectionStatus.Connected
        ? ConnectionState.Connected
        : ConnectionState.Disconnected;

    public IReadOnlyCollection<IFeature> Features { get; }

    public event EventHandler? ConnectionChanged;

    // A failed refresh leaves the previous (now stale) reading; the UI shows its age, and the log says why.
    private void OnConnectionStatusChanged(BluetoothDevice sender, object args) =>
        _ = SamaatLog.Guard("Windows Bluetooth", $"Refreshing the battery of {Identity.Name} after it connected", async () =>
        {
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
            if (Connection == ConnectionState.Connected && _battery is not null)
            {
                await _battery.RefreshAsync();
            }
        });

    public async ValueTask DisposeAsync()
    {
        _bluetooth.ConnectionStatusChanged -= OnConnectionStatusChanged;
        foreach (var feature in Features)
        {
            await DisposeQuietlyAsync(feature);
        }

        _gatt?.Dispose();
        _bluetooth.Dispose();
    }

    /// <summary>Disposes whichever dispose interface the resource has; a failure is logged, never thrown.</summary>
    internal static async ValueTask DisposeQuietlyAsync(object? resource)
    {
        try
        {
            switch (resource)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
        catch (Exception ex)
        {
            SamaatLog.Warn("Windows Bluetooth", $"Couldn't release {resource?.GetType().Name}", ex);
        }
    }
}

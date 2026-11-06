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
            switch (feature)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }

        _gatt?.Dispose();
        _bluetooth.Dispose();
    }
}

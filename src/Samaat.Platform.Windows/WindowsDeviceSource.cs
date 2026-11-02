using Samaat.Core.Battery;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Protocols.Standard;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;

namespace Samaat.Platform.Windows;

/// <summary>
/// Paired Bluetooth Classic audio devices, enriched with the BLE side of the same physical device
/// (matched by container ID) when it exposes the standard Battery Service.
/// </summary>
public sealed class WindowsDeviceSource : IDeviceSource
{
    private const string ContainerIdProperty = "System.Devices.Aep.ContainerId";
    private const string ManufacturerProperty = "System.Devices.Aep.Manufacturer";
    private const string ModelNameProperty = "System.Devices.Aep.ModelName";

    private static readonly string[] AepProperties = [ContainerIdProperty, ManufacturerProperty, ModelNameProperty];

    private readonly TimeProvider _time;
    private readonly bool _includeNonAudio;
    private readonly DriverRegistry _registry;
    private readonly DeviceWatcher _pairedWatcher;
    private readonly Dictionary<string, WindowsEarbudDevice> _devices = [];
    private readonly SemaphoreSlim _discoverLock = new(1, 1);
    private bool _enumerationCompleted;

    /// <param name="includeNonAudio">Also list paired devices that aren't audio devices (for diagnostics).</param>
    /// <param name="registry">Catalog and drivers used to recognise models. Defaults to none (generic only).</param>
    public WindowsDeviceSource(TimeProvider? time = null, bool includeNonAudio = false, DriverRegistry? registry = null)
    {
        _time = time ?? TimeProvider.System;
        _includeNonAudio = includeNonAudio;
        _registry = registry ?? new DriverRegistry(DeviceCatalog.Empty);

        _pairedWatcher = DeviceInformation.CreateWatcher(
            BluetoothDevice.GetDeviceSelectorFromPairingState(true), AepProperties, DeviceInformationKind.AssociationEndpoint);
        _pairedWatcher.Added += (_, _) => RaiseIfEnumerated();
        _pairedWatcher.Removed += (_, _) => RaiseIfEnumerated();
        _pairedWatcher.Updated += (_, _) => { };
        _pairedWatcher.EnumerationCompleted += (_, _) => _enumerationCompleted = true;
        _pairedWatcher.Start();
    }

    public string Name => "Windows Bluetooth";

    public event EventHandler? DevicesChanged;

    public async Task<IReadOnlyList<IEarbudDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        await _discoverLock.WaitAsync(cancellationToken);
        try
        {
            var bleByContainer = await FindPairedLowEnergyAsync(cancellationToken);
            var classic = await DeviceInformation.FindAllAsync(
                BluetoothDevice.GetDeviceSelectorFromPairingState(true), AepProperties, DeviceInformationKind.AssociationEndpoint)
                .AsTask(cancellationToken);

            var seen = new HashSet<string>();
            foreach (var info in classic)
            {
                if (_devices.TryGetValue(info.Id, out var existing))
                {
                    if (existing.Features.Count > 0)
                    {
                        seen.Add(info.Id);
                        continue;
                    }

                    // Built before Windows had a battery level for it; rebuild so a Refresh can pick one up.
                    await existing.DisposeAsync();
                    _devices.Remove(info.Id);
                }

                var device = await CreateDeviceAsync(info, bleByContainer, cancellationToken);
                if (device is not null)
                {
                    _devices[info.Id] = device;
                    seen.Add(info.Id);
                }
            }

            foreach (var gone in _devices.Keys.Except(seen).ToList())
            {
                await _devices[gone].DisposeAsync();
                _devices.Remove(gone);
            }

            return _devices.Values.ToList<IEarbudDevice>();
        }
        finally
        {
            _discoverLock.Release();
        }
    }

    private async Task<WindowsEarbudDevice?> CreateDeviceAsync(
        DeviceInformation info,
        IReadOnlyDictionary<Guid, string> bleByContainer,
        CancellationToken cancellationToken)
    {
        var bluetooth = await BluetoothDevice.FromIdAsync(info.Id).AsTask(cancellationToken);
        if (bluetooth is null)
        {
            return null;
        }

        if (!_includeNonAudio && bluetooth.ClassOfDevice.MajorClass != BluetoothMajorClass.AudioVideo)
        {
            bluetooth.Dispose();
            return null;
        }

        var containerId = info.Properties.TryGetValue(ContainerIdProperty, out var value) && value is Guid id ? id : (Guid?)null;

        IBatteryFeature? battery = null;
        WinRtGattClient? gatt = null;
        if (containerId is { } container && bleByContainer.TryGetValue(container, out var bleId))
        {
            var le = await BluetoothLEDevice.FromIdAsync(bleId).AsTask(cancellationToken);
            if (le is not null)
            {
                gatt = new WinRtGattClient(le);
                battery = await GattBatteryFeature.CreateAsync(gatt, _time, cancellationToken);
            }
        }

        var name = string.IsNullOrWhiteSpace(info.Name) ? bluetooth.Name : info.Name;
        var signals = new DeviceSignals(
            name,
            bluetooth.ClassOfDevice.RawValue,
            Manufacturer: info.Properties.TryGetValue(ManufacturerProperty, out var manufacturer) ? manufacturer as string : null,
            ServiceUuids: await ReadServiceUuidsAsync(bluetooth, cancellationToken));
        var resolution = _registry.Resolve(signals);

        var features = new List<IFeature> { new DeviceDiagnostics(signals, resolution) };
        var tier = battery is null ? SupportTier.Basic : SupportTier.Standard;
        if (resolution is { Kind: DriverResolutionKind.Driver, Driver: { } driver, Entry: { } entry })
        {
            // A driver supersedes the generic battery sources.
            var opened = await driver.OpenAsync(new DriverContext(signals, entry, new WinRtDeviceTransports(bluetooth, gatt), _time), cancellationToken);
            features.AddRange(opened);
            tier = SupportTier.Full;
            if (opened.Any(f => f is IBatteryFeature) && battery is IAsyncDisposable superseded)
            {
                await superseded.DisposeAsync();
                battery = null;
            }
        }

        if (battery is null && !features.Any(f => f is IBatteryFeature) && containerId is { } c)
        {
            battery = await OsBatteryFeature.CreateAsync(c, _time, cancellationToken);
        }

        if (battery is not null)
        {
            features.Insert(0, battery);
        }

        var identity = new DeviceIdentity(
            info.Id,
            name,
            resolution.Entry?.Manufacturer ?? signals.Manufacturer,
            resolution.Entry?.DisplayName ?? (info.Properties.TryGetValue(ModelNameProperty, out var model) ? model as string : null),
            FirmwareVersion: null,
            tier);

        return new WindowsEarbudDevice(identity, bluetooth, features, gatt);
    }

    /// <summary>RFCOMM service UUIDs from the cached SDP record: vendor protocols announce themselves here.</summary>
    private static async Task<IReadOnlySet<Guid>> ReadServiceUuidsAsync(BluetoothDevice device, CancellationToken cancellationToken)
    {
        try
        {
            var result = await device.GetRfcommServicesAsync(BluetoothCacheMode.Cached).AsTask(cancellationToken);
            return result.Error == BluetoothError.Success
                ? result.Services.Select(s => s.ServiceId.Uuid).ToHashSet()
                : new HashSet<Guid>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new HashSet<Guid>();
        }
    }

    private static async Task<IReadOnlyDictionary<Guid, string>> FindPairedLowEnergyAsync(CancellationToken cancellationToken)
    {
        var devices = await DeviceInformation.FindAllAsync(
            BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), [ContainerIdProperty], DeviceInformationKind.AssociationEndpoint)
            .AsTask(cancellationToken);

        var result = new Dictionary<Guid, string>();
        foreach (var info in devices)
        {
            if (info.Properties.TryGetValue(ContainerIdProperty, out var value) && value is Guid containerId)
            {
                result.TryAdd(containerId, info.Id);
            }
        }

        return result;
    }

    private void RaiseIfEnumerated()
    {
        if (_enumerationCompleted)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_pairedWatcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            _pairedWatcher.Stop();
        }

        foreach (var device in _devices.Values)
        {
            await device.DisposeAsync();
        }

        _devices.Clear();
        _discoverLock.Dispose();
    }
}

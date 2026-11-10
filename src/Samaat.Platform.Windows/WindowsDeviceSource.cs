using Samaat.Core.Battery;
using Samaat.Core.Bluetooth;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Protocols.Standard;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Samaat.Core.Diagnostics;

namespace Samaat.Platform.Windows;

/// <summary>
/// Paired Bluetooth Classic audio devices, enriched with the BLE side of the same physical device
/// (matched by container ID) when it exposes the standard Battery Service.
/// </summary>
public sealed class WindowsDeviceSource : IDeviceSource
{
    private const string ContainerIdProperty = WindowsSelectors.ContainerIdProperty;
    private const string ManufacturerProperty = WindowsSelectors.ManufacturerProperty;
    private const string ModelNameProperty = WindowsSelectors.ModelNameProperty;
    private const string HardwareIdsProperty = WindowsSelectors.HardwareIdsProperty;

    // One budget for the by-address BLE connection plus the driver session (auth and first push).
    private static readonly TimeSpan DriverBudget = TimeSpan.FromSeconds(8);

    // shortcut: a 3 s cap keeps driverless models quick to appear when the probe fails; raise it if real fallback buds need longer to answer
    private static readonly TimeSpan FallbackProbeBudget = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LateBatteryDelay = TimeSpan.FromSeconds(10);
    private readonly CancellationTokenSource _disposeCts = new();

    private static readonly string[] AepProperties = [ContainerIdProperty, ManufacturerProperty, ModelNameProperty];

    private readonly TimeProvider _time;
    private readonly bool _includeNonAudio;
    private readonly DriverRegistry _registry;
    private readonly DeviceWatcher _pairedWatcher;
    private readonly Dictionary<string, WindowsEarbudDevice> _devices = [];
    private readonly Dictionary<string, IDisposable> _reconnectWatches = [];
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
                try
                {
                    await RefreshDeviceAsync(info, bleByContainer, seen, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    // One failing device must not hide the others; it is simply absent this round.
                    SamaatLog.Error("Windows Bluetooth", $"Couldn't read {info.Name}", ex);
                    await RemoveAsync(info.Id);
                }
            }

            foreach (var gone in _devices.Keys.Except(seen).ToList())
            {
                await RemoveAsync(gone);
            }

            return _devices.Values.ToList<IEarbudDevice>();
        }
        finally
        {
            _discoverLock.Release();
        }
    }

    private async Task RefreshDeviceAsync(
        DeviceInformation info,
        IReadOnlyDictionary<Guid, string> bleByContainer,
        HashSet<string> seen,
        CancellationToken cancellationToken)
    {
        if (_devices.TryGetValue(info.Id, out var existing))
        {
            if (DriverRetry.CanReuse(existing) && !existing.SessionStale)
            {
                seen.Add(info.Id);
                return;
            }

            // No battery yet, or its vendor driver failed: rebuild so a Refresh can pick them up.
            // A session that died with the connection is rebuilt too.
            await RemoveAsync(info.Id);
        }

        var device = await CreateDeviceAsync(info, bleByContainer, cancellationToken);
        if (device is not null)
        {
            _devices[info.Id] = device;
            seen.Add(info.Id);
            if (DriverRetry.NeedsDriverRetry(device))
            {
                _reconnectWatches[info.Id] = DriverRetry.WatchForReconnect(device, RaiseIfEnumerated);
            }
            else if (DriverRetry.NeedsRebuildOnReconnect(device))
            {
                _reconnectWatches[info.Id] = DriverRetry.WatchForReconnect(device, () =>
                {
                    device.SessionStale = true;
                    RaiseIfEnumerated();
                });
            }
            else if (device.GetFeature<IBatteryFeature>() is null)
            {
                _reconnectWatches[info.Id] = DriverRetry.WatchForReconnect(device, RaiseLater);
            }
        }
    }

    // Windows publishes the battery level a few seconds after the link comes up, so look again then, once.
    private void RaiseLater() =>
        _ = SamaatLog.Guard("Windows Bluetooth", "Rechecking the battery after a reconnect", async () =>
        {
            await Task.Delay(LateBatteryDelay, _time, _disposeCts.Token);
            RaiseIfEnumerated();
        });

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

        // Everything built before a failure is released here; on success WindowsEarbudDevice owns it.
        var owned = new List<object> { bluetooth };
        try
        {
            return await BuildDeviceAsync(info, bluetooth, bleByContainer, owned, cancellationToken);
        }
        catch (Exception)
        {
            for (var i = owned.Count - 1; i >= 0; i--)
            {
                await WindowsEarbudDevice.DisposeQuietlyAsync(owned[i]);
            }

            throw;
        }
    }

    private async Task<WindowsEarbudDevice?> BuildDeviceAsync(
        DeviceInformation info,
        BluetoothDevice bluetooth,
        IReadOnlyDictionary<Guid, string> bleByContainer,
        List<object> owned,
        CancellationToken cancellationToken)
    {
        var containerId = WindowsSelectors.TryReadContainerId(info.Properties);

        var ids = await ReadVidPidAsync(containerId, cancellationToken);
        var signals = WindowsSignals.Create(
            info.Name, bluetooth.Name, bluetooth.ClassOfDevice.RawValue, ids, await ReadServiceUuidsAsync(bluetooth, cancellationToken), info.Properties);
        var name = signals.Name;
        var resolution = _registry.Resolve(signals);

        // For a driver-resolved device one deadline covers all BLE work: the standard battery probe,
        // the by-address connection and the whole driver session.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var isDriver = resolution.Kind == DriverResolutionKind.Driver;
        if (isDriver)
        {
            budget.CancelAfter(resolution.ProtocolFallback ? FallbackProbeBudget : DriverBudget);
        }

        var bleToken = isDriver ? budget.Token : cancellationToken;
        IBatteryFeature? battery = null;
        WinRtGattClient? gatt = null;
        if (containerId is { } container && bleByContainer.TryGetValue(container, out var bleId))
        {
            try
            {
                var le = await BluetoothLEDevice.FromIdAsync(bleId).AsTask(bleToken);
                if (le is not null)
                {
                    gatt = new WinRtGattClient(le);
                    owned.Add(gatt);
                    battery = await GattBatteryFeature.CreateAsync(gatt, _time, bleToken);
                    if (battery is not null)
                    {
                        owned.Add(battery);
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The driver budget ran out; the driver below gets an already-cancelled token and returns nothing.
            }
        }

        var features = new List<IFeature> { new DeviceDiagnostics(signals, resolution) };
        var tier = battery is null ? SupportTier.Basic : SupportTier.Standard;
        if (resolution is { Kind: DriverResolutionKind.Driver, Driver: { } driver, Entry: { } entry })
        {
            var fallback = gatt is null ? await ConnectLowEnergyByAddressAsync(bluetooth.BluetoothAddress, budget.Token) : null;
            gatt ??= fallback;
            if (fallback is not null)
            {
                owned.Add(fallback);
            }

            // A driver supersedes the generic battery sources.
            var opened = await driver.OpenAsync(new DriverContext(signals, entry, new WinRtDeviceTransports(bluetooth, gatt), _time), budget.Token);
            features.AddRange(opened);
            owned.AddRange(opened);
            if (opened.Count > 0)
            {
                tier = SupportTier.Full;
            }
            else if (fallback is not null)
            {
                fallback.Dispose();
                gatt = null;
            }

            if (opened.Any(f => f is IBatteryFeature) && battery is IAsyncDisposable superseded)
            {
                await superseded.DisposeAsync();
                battery = null;
            }
        }

        if (battery is null && !features.Any(f => f is IBatteryFeature) && containerId is { } c)
        {
            battery = await OsBatteryFeature.CreateAsync(c, _time, cancellationToken);
            if (battery is not null)
            {
                owned.Add(battery);
            }
        }

        if (battery is not null)
        {
            features.Insert(0, battery);
        }

        WindowsPcLinkFeature? pcLink = null;
        if (containerId is { } linkContainer && await BluetoothAudioLink.CountEndpointsAsync(linkContainer) > 0)
        {
            pcLink = new WindowsPcLinkFeature(linkContainer, _time);
            features.Add(pcLink);
            owned.Add(pcLink);
        }

        var identity = new DeviceIdentity(
            info.Id,
            name,
            resolution.Entry?.Manufacturer ?? signals.Manufacturer,
            resolution.Entry?.DisplayName ?? (info.Properties.TryGetValue(ModelNameProperty, out var model) ? model as string : null),
            FirmwareVersion: null,
            tier,
            await ReadDetailsAsync(bluetooth, ids, cancellationToken));

        var created = new WindowsEarbudDevice(identity, bluetooth, features, gatt);
        if (pcLink is not null)
        {
            pcLink.Device = created;
        }

        return created;
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
            SamaatLog.Warn("Windows Bluetooth", $"Couldn't read the RFCOMM services of {device.Name}", ex);
            return new HashSet<Guid>();
        }
    }

    /// <summary>Best effort: any part that can't be read stays unknown rather than failing the device.</summary>
    private static async Task<(int Vid, int Pid)?> ReadVidPidAsync(Guid? containerId, CancellationToken cancellationToken)
    {
        if (containerId is not { } container)
        {
            return null;
        }

        try
        {
            var nodes = await DeviceInformation.FindAllAsync(
                WindowsSelectors.ContainerId(container), [HardwareIdsProperty], DeviceInformationKind.Device)
                .AsTask(cancellationToken);
            return HardwareIds.TryParseVidPid(nodes
                .SelectMany(n => n.Properties.TryGetValue(HardwareIdsProperty, out var v) && v is string[] s ? s : []));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            SamaatLog.Warn("Windows Bluetooth", "Couldn't read the vendor and product IDs", ex);
            return null;
        }
    }

    /// <summary>For devices that aren't listed as paired BLE but answer at the same public address. Null on any failure.</summary>
    private static async Task<WinRtGattClient?> ConnectLowEnergyByAddressAsync(ulong address, CancellationToken cancellationToken)
    {
        try
        {
            var le = await BluetoothLEDevice.FromBluetoothAddressAsync(address, BluetoothAddressType.Public).AsTask(cancellationToken);
            return le is null ? null : new WinRtGattClient(le);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SamaatLog.Info("Windows Bluetooth", "No BLE connection at the device's public address", ex);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<DeviceDetails> ReadDetailsAsync(BluetoothDevice bluetooth, (int Vid, int Pid)? ids, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> uuids = [];
        try
        {
            uuids = bluetooth.SdpRecords
                .Select(buffer => SdpRecord.Parse(WinRtGattClient.ToArray(buffer)))
                .OfType<SdpRecord>()
                .SelectMany(record => record.ServiceClasses)
                .Distinct()
                .ToList();
        }
        catch (Exception ex)
        {
            // Unreadable records just mean the services show as not reported.
            SamaatLog.Warn("Windows Bluetooth", $"Couldn't read the SDP records of {bluetooth.Name}", ex);
        }

        return new DeviceDetails
        {
            Address = DeviceDetails.FormatAddress(bluetooth.BluetoothAddress),
            VendorId = ids?.Vid,
            ProductId = ids?.Pid,
            Services = BluetoothServices.Labels(uuids),
            ServiceUuids = uuids,
        };
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

    private async Task RemoveAsync(string id)
    {
        if (_reconnectWatches.Remove(id, out var watch))
        {
            watch.Dispose();
        }

        if (_devices.Remove(id, out var device))
        {
            await WindowsEarbudDevice.DisposeQuietlyAsync(device);
        }
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
        await _disposeCts.CancelAsync();
        if (_pairedWatcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            _pairedWatcher.Stop();
        }

        // Never touch the maps while a refresh may be iterating them; the process is exiting if this times out.
        if (!await _discoverLock.WaitAsync(TimeSpan.FromSeconds(2)))
        {
            SamaatLog.Warn("Windows Bluetooth", "A refresh was still running at shutdown; skipping cleanup");
            return;
        }

        try
        {
            foreach (var watch in _reconnectWatches.Values)
            {
                watch.Dispose();
            }

            _reconnectWatches.Clear();
            foreach (var device in _devices.Values)
            {
                await WindowsEarbudDevice.DisposeQuietlyAsync(device);
            }

            _devices.Clear();
        }
        finally
        {
            _discoverLock.Release();
        }
    }
}

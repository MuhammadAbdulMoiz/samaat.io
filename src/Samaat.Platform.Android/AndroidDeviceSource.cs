using Android.Bluetooth;
using Android.Content;
using Samaat.Core.Battery;
using Samaat.Core.Bluetooth;
using Samaat.Core.Capabilities;
using Samaat.Core.Devices;
using Samaat.Core.Drivers;
using Samaat.Protocols.Standard;
using Samaat.Transport;

namespace Samaat.Platform.Android;

/// <summary>
/// The phone's paired Bluetooth audio devices as Samaat devices. Like the Windows source it resolves a driver from the
/// catalog and opens it over Android's RFCOMM and GATT, so the Jieli and Fast Pair drivers work on the phone too.
/// Android hides vendor and product IDs from apps, so models are matched by name and services. Unverified on hardware.
/// </summary>
public sealed class AndroidDeviceSource : IDeviceSource
{
    private static readonly TimeSpan DriverBudget = TimeSpan.FromSeconds(8);

    private readonly Context _context;
    private readonly AndroidPhoneAudio _audio;
    private readonly DriverRegistry _registry;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, AndroidEarbudDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);

    public AndroidDeviceSource(Context context, AndroidPhoneAudio audio, DriverRegistry registry, TimeProvider? time = null)
    {
        _context = context.ApplicationContext ?? context;
        _audio = audio;
        _registry = registry;
        _time = time ?? TimeProvider.System;
        _audio.Changed += OnAudioChanged;
    }

    public string Name => "Android Bluetooth";

    public event EventHandler? DevicesChanged;

    public async Task<IReadOnlyList<IEarbudDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var adapter = (_context.GetSystemService(Context.BluetoothService) as BluetoothManager)?.Adapter;
            if (adapter is null || _audio.Blocker is not null || adapter.BondedDevices is not { } bonded)
            {
                return [];
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var bluetooth in bonded.Where(AndroidPhoneAudio.IsAudio))
            {
                if (bluetooth.Address is not { } address)
                {
                    continue;
                }

                seen.Add(address);
                if (_devices.TryGetValue(address, out var existing))
                {
                    if (DriverRetry.CanReuse(existing) || existing.Connection != ConnectionState.Connected)
                    {
                        continue;
                    }

                    // Connected now, and its driver never got a chance: build it again.
                    await existing.DisposeAsync();
                    _devices.Remove(address);
                }

                _devices[address] = await CreateAsync(bluetooth, address, cancellationToken);
            }

            foreach (var gone in _devices.Keys.Except(seen).ToList())
            {
                await _devices[gone].DisposeAsync();
                _devices.Remove(gone);
            }

            _known = seen;
            return [.. _devices.Values];
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<AndroidEarbudDevice> CreateAsync(BluetoothDevice bluetooth, string address, CancellationToken cancellationToken)
    {
        var uuids = (bluetooth.GetUuids() ?? [])
            .Select(p => p.Uuid?.ToString())
            .Where(s => s is not null && Guid.TryParse(s, out _))
            .Select(s => Guid.Parse(s!))
            .ToHashSet();
        var name = AndroidPhoneAudio.DisplayName(bluetooth);
        var signals = new DeviceSignals(name, ClassOfDevice: bluetooth.BluetoothClass is { } c ? (uint)c.DeviceClass : null, ServiceUuids: uuids);
        var resolution = _registry.Resolve(signals);
        var features = new List<IFeature> { new DeviceDiagnostics(signals, resolution) };
        var tier = SupportTier.Basic;
        AndroidGattClient? gatt = null;

        if (resolution is { Kind: DriverResolutionKind.Driver, Driver: { } driver, Entry: { } entry } && _audio.IsConnected(address))
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(DriverBudget);
            gatt = new AndroidGattClient(_context, bluetooth);
            try
            {
                var opened = await driver.OpenAsync(new DriverContext(signals, entry, new AndroidDeviceTransports(bluetooth, gatt), _time), budget.Token);
                features.AddRange(opened);
                if (opened.Count > 0)
                {
                    tier = SupportTier.Full;
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or Java.Lang.Exception or IOException)
            {
                // The driver gave nothing in time; the device stays basic and is retried when it reconnects.
            }

            if (tier != SupportTier.Full)
            {
                gatt.Dispose();
                gatt = null;
            }
        }

        // The desktop's fallbacks, in its order: a driver's battery, then the standard BLE Battery Service, then the OS level.
        if (!features.Any(f => f is IBatteryFeature) && _audio.IsConnected(address))
        {
            var bleGatt = gatt ?? new AndroidGattClient(_context, bluetooth);
            IBatteryFeature? standard = null;
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                budget.CancelAfter(TimeSpan.FromSeconds(3));
                standard = await GattBatteryFeature.CreateAsync(bleGatt, _time, budget.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or Java.Lang.Exception or IOException)
            {
                // No BLE side, or it didn't answer in time.
            }

            if (standard is not null)
            {
                features.Add(standard);
                tier = tier == SupportTier.Basic ? SupportTier.Standard : tier;
                gatt ??= bleGatt;
            }
            else if (!ReferenceEquals(bleGatt, gatt))
            {
                bleGatt.Dispose();
            }
        }

        if (!features.Any(f => f is IBatteryFeature))
        {
            features.Add(new AndroidBatteryFeature(bluetooth, _time));
        }

        var identity = new DeviceIdentity(address, name, Tier: tier, Details: new DeviceDetails
        {
            Address = address.ToUpperInvariant(),
            ServiceUuids = [.. uuids],
            Services = BluetoothServices.Labels(uuids),
        });
        return new AndroidEarbudDevice(identity, address, _audio, features, gatt);
    }

    private void OnAudioChanged(object? sender, EventArgs e)
    {
        // Pairing changes and permission grants change the list; connection changes are per device.
        var adapter = (_context.GetSystemService(Context.BluetoothService) as BluetoothManager)?.Adapter;
        var now = adapter?.BondedDevices?.Where(AndroidPhoneAudio.IsAudio).Select(d => d.Address ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        if (!now.SetEquals(_known))
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _audio.Changed -= OnAudioChanged;
        foreach (var device in _devices.Values)
        {
            await device.DisposeAsync();
        }

        _devices.Clear();
    }
}

internal sealed class AndroidEarbudDevice : IEarbudDevice, IAsyncDisposable
{
    private readonly string _address;
    private readonly AndroidPhoneAudio _audio;
    private readonly AndroidGattClient? _gatt;
    private ConnectionState _last;

    public AndroidEarbudDevice(DeviceIdentity identity, string address, AndroidPhoneAudio audio, IReadOnlyList<IFeature> features, AndroidGattClient? gatt)
    {
        Identity = identity;
        _address = address;
        _audio = audio;
        _gatt = gatt;
        Features = features;
        _last = Connection;
        _audio.Changed += OnAudioChanged;
    }

    public DeviceIdentity Identity { get; }

    public ConnectionState Connection => _audio.IsConnected(_address) ? ConnectionState.Connected : ConnectionState.Disconnected;

    public IReadOnlyCollection<IFeature> Features { get; }

    public event EventHandler? ConnectionChanged;

    private void OnAudioChanged(object? sender, EventArgs e)
    {
        var now = Connection;
        if (now == _last)
        {
            return;
        }

        _last = now;
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        if (now == ConnectionState.Connected && Features.OfType<IBatteryFeature>().FirstOrDefault() is { } battery)
        {
            _ = battery.RefreshAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _audio.Changed -= OnAudioChanged;
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
    }
}

/// <summary>
/// The one battery level Android itself keeps for a headset (from HFP/AVRCP). It's behind a hidden method, read through
/// reflection; on phones that refuse it the device simply reports no battery.
/// </summary>
internal sealed class AndroidBatteryFeature(BluetoothDevice device, TimeProvider time) : IBatteryFeature
{
    public IReadOnlySet<BatteryComponent> Components { get; } = new HashSet<BatteryComponent> { BatteryComponent.Combined };

    public bool ComponentsAreExhaustive => false;

    public CapabilitySource Source => CapabilitySource.OperatingSystem;

    public Verification Verification => Verification.Unverified;

    public BatteryState Current { get; private set; } = BatteryState.Empty;

    public event EventHandler<BatteryState>? Changed;

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var method = device.Class.GetMethod("getBatteryLevel");
            if (method.Invoke(device) is Java.Lang.Integer level && level.IntValue() is >= 0 and <= 100 and var percent)
            {
                Current = BatteryState.Empty.With(new BatteryReading(BatteryComponent.Combined, percent, time.GetUtcNow()));
                Changed?.Invoke(this, Current);
            }
        }
        catch (Java.Lang.Exception)
        {
            // Hidden method missing or refused on this phone.
        }

        return Task.CompletedTask;
    }
}

/// <summary>RFCOMM and GATT to one device, for drivers.</summary>
internal sealed class AndroidDeviceTransports(BluetoothDevice device, AndroidGattClient gatt) : IDeviceTransports
{
    public IGattClient? Gatt => gatt;

    public Task<IRfcommChannel?> OpenRfcommAsync(Guid serviceUuid, CancellationToken cancellationToken = default) =>
        AndroidRfcommChannel.OpenAsync(device, serviceUuid, cancellationToken);
}

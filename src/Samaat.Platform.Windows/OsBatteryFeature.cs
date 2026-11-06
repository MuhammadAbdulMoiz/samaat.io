using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Windows.Devices.Enumeration;

namespace Samaat.Platform.Windows;

/// <summary>
/// The single battery level Windows itself shows in Settings › Bluetooth. Windows reads it from
/// the headset over HFP (HF battery indicator or Apple's AT+IPHONEACCEV) and publishes it as a
/// property on the hands-free PnP device node, which we find through the device's container ID.
/// </summary>
/// <remarks>Unverified on hardware; see docs/modules/battery.md.</remarks>
internal sealed class OsBatteryFeature : IBatteryFeature, IDisposable
{
    /// <summary>DEVPKEY_Bluetooth_Battery as an AQS/PnP property key.</summary>
    internal const string BatteryProperty = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    private static readonly string[] RequestedProperties = [BatteryProperty];

    private readonly string _selector;
    private readonly TimeProvider _time;
    private readonly DeviceWatcher _watcher;
    private BatteryState _current = BatteryState.Empty;

    private OsBatteryFeature(string selector, int? initialPercent, TimeProvider time)
    {
        _selector = selector;
        _time = time;
        if (initialPercent is { } percent)
        {
            _current = BatteryState.Empty.With(new BatteryReading(BatteryComponent.Combined, percent, _time.GetUtcNow()));
        }

        _watcher = DeviceInformation.CreateWatcher(_selector, RequestedProperties, DeviceInformationKind.Device);
        _watcher.Added += (_, info) => Apply(info.Properties);
        _watcher.Updated += (_, update) => Apply(update.Properties);
        _watcher.Start();
    }

    public IReadOnlySet<BatteryComponent> Components { get; } = new HashSet<BatteryComponent> { BatteryComponent.Combined };

    public bool ComponentsAreExhaustive => false;

    public CapabilitySource Source => CapabilitySource.OperatingSystem;

    public Verification Verification => Verification.Unverified;

    public BatteryState Current => Volatile.Read(ref _current);

    public event EventHandler<BatteryState>? Changed;

    /// <summary>Returns null when Windows has never reported a battery level for this container.</summary>
    public static async Task<OsBatteryFeature?> CreateAsync(Guid containerId, TimeProvider time, CancellationToken cancellationToken)
    {
        var selector = WindowsSelectors.ContainerId(containerId);
        var percent = await QueryAsync(selector, cancellationToken);
        return percent is null ? null : new OsBatteryFeature(selector, percent, time);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (await QueryAsync(_selector, cancellationToken) is { } percent)
        {
            Publish(percent);
        }
    }

    private static async Task<int?> QueryAsync(string selector, CancellationToken cancellationToken)
    {
        var nodes = await DeviceInformation.FindAllAsync(selector, RequestedProperties, DeviceInformationKind.Device).AsTask(cancellationToken);
        return nodes.Select(n => ReadPercent(n.Properties)).FirstOrDefault(p => p is not null);
    }

    /// <summary>The level Windows reports, clamped to 0–100; null when it reports none.</summary>
    internal static int? ReadPercent(IReadOnlyDictionary<string, object> properties) =>
        properties.TryGetValue(BatteryProperty, out var value) && value is not null
            ? Math.Clamp(Convert.ToInt32(value), 0, 100)
            : null;

    private void Apply(IReadOnlyDictionary<string, object> properties)
    {
        if (ReadPercent(properties) is { } percent)
        {
            Publish(percent);
        }
    }

    private void Publish(int percent)
    {
        var state = BatteryState.Empty.With(new BatteryReading(BatteryComponent.Combined, percent, _time.GetUtcNow()));
        Volatile.Write(ref _current, state);
        Changed?.Invoke(this, state);
    }

    public void Dispose()
    {
        if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            _watcher.Stop();
        }
    }
}

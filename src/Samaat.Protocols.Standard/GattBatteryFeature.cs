using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Transport;

namespace Samaat.Protocols.Standard;

/// <summary>
/// Battery over the standard BLE Battery Service (0x180F). A device may expose several Battery
/// Level characteristics; a Presentation Format descriptor naming "left"/"right" maps them to
/// buds. A lone unlabelled level is treated as the combined level.
/// </summary>
public sealed class GattBatteryFeature : IBatteryFeature, IAsyncDisposable
{
    private readonly IReadOnlyDictionary<BatteryComponent, IGattCharacteristic> _characteristics;
    private readonly TimeProvider _time;
    private readonly List<IAsyncDisposable> _subscriptions = [];
    private readonly Lock _gate = new();
    private BatteryState _current = BatteryState.Empty;

    private GattBatteryFeature(IReadOnlyDictionary<BatteryComponent, IGattCharacteristic> characteristics, TimeProvider time)
    {
        _characteristics = characteristics;
        _time = time;
        Components = characteristics.Keys.ToHashSet();
    }

    public IReadOnlySet<BatteryComponent> Components { get; }

    public bool ComponentsAreExhaustive => false;

    public CapabilitySource Source => CapabilitySource.StandardService;

    public Verification Verification => Verification.Unverified;

    public BatteryState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event EventHandler<BatteryState>? Changed;

    /// <summary>
    /// Discovers the Battery Service, reads initial values and subscribes to notifications.
    /// Returns null when the device has no usable Battery Level characteristic.
    /// </summary>
    public static async Task<GattBatteryFeature?> CreateAsync(
        IGattClient client,
        TimeProvider? time = null,
        CancellationToken cancellationToken = default)
    {
        var labelled = new Dictionary<BatteryComponent, IGattCharacteristic>();
        var unlabelled = new List<IGattCharacteristic>();

        foreach (var service in await client.GetServicesAsync(GattUuids.BatteryService, cancellationToken))
        {
            foreach (var characteristic in await service.GetCharacteristicsAsync(GattUuids.BatteryLevel, cancellationToken))
            {
                var descriptor = await characteristic.ReadDescriptorAsync(GattUuids.CharacteristicPresentationFormat, cancellationToken);
                var position = descriptor is null ? null : PresentationFormat.Parse(descriptor)?.Position;
                if (position is { } component)
                {
                    labelled.TryAdd(component, characteristic);
                }
                else
                {
                    unlabelled.Add(characteristic);
                }
            }
        }

        // Several unlabelled levels can't be told apart; only the first is trusted as "combined".
        if (unlabelled.Count > 0 && labelled.Count == 0)
        {
            labelled[BatteryComponent.Combined] = unlabelled[0];
        }

        if (labelled.Count == 0)
        {
            return null;
        }

        var feature = new GattBatteryFeature(labelled, time ?? TimeProvider.System);
        await feature.SubscribeAsync(cancellationToken);
        await feature.RefreshAsync(cancellationToken);
        return feature;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        foreach (var (component, characteristic) in _characteristics)
        {
            if (characteristic.CanRead && await characteristic.ReadAsync(cancellationToken) is { } value)
            {
                Apply(component, value);
            }
        }
    }

    private async Task SubscribeAsync(CancellationToken cancellationToken)
    {
        foreach (var (component, characteristic) in _characteristics)
        {
            if (characteristic.CanNotify)
            {
                _subscriptions.Add(await characteristic.SubscribeAsync(value => Apply(component, value), cancellationToken));
            }
        }
    }

    private void Apply(BatteryComponent component, byte[] value)
    {
        if (!BatteryLevelParser.TryParse(value, out var percent))
        {
            return;
        }

        BatteryState updated;
        lock (_gate)
        {
            _current = _current.With(new BatteryReading(component, percent, _time.GetUtcNow()));
            updated = _current;
        }

        Changed?.Invoke(this, updated);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var subscription in _subscriptions)
        {
            await subscription.DisposeAsync();
        }

        _subscriptions.Clear();
    }
}

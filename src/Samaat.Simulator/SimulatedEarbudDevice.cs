using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Devices;

namespace Samaat.Simulator;

/// <summary>The three battery-reporting shapes real devices come in (docs/PLAN.md, Module 1).</summary>
public enum SimulatedBatteryLayout
{
    /// <summary>Left, right and case, like most true-wireless earbuds with a vendor protocol.</summary>
    BudsAndCase,

    /// <summary>Left and right but no case level.</summary>
    BudsOnly,

    /// <summary>One combined level, like a headset or generic buds.</summary>
    CombinedOnly,
}

/// <summary>
/// A fake device whose battery evolves deterministically on each <see cref="Tick"/>: buds drain
/// while worn, go into the case when low, charge from the case, then come back out.
/// </summary>
public sealed class SimulatedEarbudDevice : IEarbudDevice
{
    private const int DrainPerTick = 1;
    private const int ChargePerTick = 4;
    private const int CaseCostPerTick = 1;
    private const int DockAtPercent = 8;
    private const int HeadsetRechargeAtPercent = 15;

    private readonly TimeProvider _time;
    private readonly SimulatedBatteryFeature _battery;
    private readonly Lock _gate = new();
    private readonly Dictionary<BatteryComponent, Cell> _cells;
    private ConnectionState _connection = ConnectionState.Connected;

    private readonly IReadOnlyList<IFeature> _features;

    public SimulatedEarbudDevice(
        DeviceIdentity identity,
        SimulatedBatteryLayout layout,
        TimeProvider? time = null,
        IEnumerable<IFeature>? extraFeatures = null)
    {
        Identity = identity;
        Layout = layout;
        _time = time ?? TimeProvider.System;
        _cells = layout switch
        {
            SimulatedBatteryLayout.BudsAndCase => new()
            {
                [BatteryComponent.Left] = new(93, BudStatus.Connected),
                [BatteryComponent.Right] = new(81, BudStatus.Connected),
                [BatteryComponent.Case] = new(64, BudStatus.Unknown),
            },
            SimulatedBatteryLayout.BudsOnly => new()
            {
                [BatteryComponent.Left] = new(42, BudStatus.Connected),
                [BatteryComponent.Right] = new(17, BudStatus.Connected),
            },
            _ => new() { [BatteryComponent.Combined] = new(55, BudStatus.Unknown) },
        };
        _battery = new SimulatedBatteryFeature(_cells.Keys.ToHashSet());
        _features = [_battery, .. extraFeatures ?? []];
        _battery.Publish(Snapshot());
    }

    public DeviceIdentity Identity { get; }

    public SimulatedBatteryLayout Layout { get; }

    public ConnectionState Connection
    {
        get
        {
            lock (_gate)
            {
                return _connection;
            }
        }
    }

    public IReadOnlyCollection<IFeature> Features => _features;

    public event EventHandler? ConnectionChanged;

    public void SetConnected(bool connected)
    {
        lock (_gate)
        {
            _connection = connected ? ConnectionState.Connected : ConnectionState.Disconnected;
        }

        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Advances the simulation one step. No-op while disconnected (no updates arrive).</summary>
    public void Tick()
    {
        BatteryState snapshot;
        lock (_gate)
        {
            if (_connection != ConnectionState.Connected)
            {
                return;
            }

            switch (Layout)
            {
                case SimulatedBatteryLayout.BudsAndCase:
                    TickBud(_cells[BatteryComponent.Left], _cells[BatteryComponent.Case]);
                    TickBud(_cells[BatteryComponent.Right], _cells[BatteryComponent.Case]);
                    break;
                case SimulatedBatteryLayout.BudsOnly:
                    TickBud(_cells[BatteryComponent.Left], null);
                    TickBud(_cells[BatteryComponent.Right], null);
                    break;
                default:
                    TickHeadset(_cells[BatteryComponent.Combined]);
                    break;
            }

            snapshot = Snapshot();
        }

        _battery.Publish(snapshot);
    }

    private static void TickBud(Cell bud, Cell? caseCell)
    {
        if (bud.Status == BudStatus.Connected)
        {
            bud.Percent = Math.Max(0, bud.Percent - DrainPerTick);
            bud.Charging = ChargingState.NotCharging;
            if (bud.Percent <= DockAtPercent)
            {
                // Without a modelled case, "docking" stands for being put on a charger.
                bud.Status = caseCell is null ? BudStatus.Disconnected : BudStatus.InCase;
            }

            return;
        }

        if (bud.Percent >= 100)
        {
            bud.Status = BudStatus.Connected;
            bud.Charging = ChargingState.Full;
            return;
        }

        if (caseCell is { Percent: <= 0 })
        {
            bud.Charging = ChargingState.NotCharging;
            return;
        }

        bud.Percent = Math.Min(100, bud.Percent + ChargePerTick);
        bud.Charging = bud.Percent >= 100 ? ChargingState.Full : ChargingState.Charging;
        if (caseCell is not null)
        {
            caseCell.Percent = Math.Max(0, caseCell.Percent - CaseCostPerTick);
        }
    }

    private static void TickHeadset(Cell cell)
    {
        if (cell.Charging == ChargingState.Charging)
        {
            cell.Percent = Math.Min(100, cell.Percent + ChargePerTick);
            if (cell.Percent >= 100)
            {
                cell.Charging = ChargingState.Full;
            }
        }
        else
        {
            cell.Percent = Math.Max(0, cell.Percent - DrainPerTick);
            cell.Charging = cell.Percent <= HeadsetRechargeAtPercent ? ChargingState.Charging : ChargingState.NotCharging;
        }
    }

    private BatteryState Snapshot()
    {
        var now = _time.GetUtcNow();
        return BatteryState.Empty.With(_cells.Select(kv => new BatteryReading(
            kv.Key,
            kv.Value.Percent,
            now,
            kv.Value.Charging,
            kv.Key is BatteryComponent.Left or BatteryComponent.Right ? kv.Value.Status : BudStatus.Unknown)));
    }

    private sealed class Cell(int percent, BudStatus status)
    {
        public int Percent { get; set; } = percent;

        public BudStatus Status { get; set; } = status;

        public ChargingState Charging { get; set; } = ChargingState.NotCharging;
    }

    private sealed class SimulatedBatteryFeature(IReadOnlySet<BatteryComponent> components) : IBatteryFeature
    {
        private BatteryState _current = BatteryState.Empty;

        public IReadOnlySet<BatteryComponent> Components { get; } = components;

        public bool ComponentsAreExhaustive => true;

        public CapabilitySource Source => CapabilitySource.Simulator;

        public Verification Verification => Verification.Verified;

        public BatteryState Current => Volatile.Read(ref _current);

        public event EventHandler<BatteryState>? Changed;

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Publish(BatteryState state)
        {
            Volatile.Write(ref _current, state);
            Changed?.Invoke(this, state);
        }
    }
}

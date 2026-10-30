// samaat-cli: prints what Samaat detects for each device. Used to check real hardware (Phase 0)
// without the UI.
//
//   samaat-cli [--watch] [--simulate] [--all-devices]

using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Devices;
using Samaat.Simulator;

var watch = args.Contains("--watch");
var simulate = args.Contains("--simulate");
var includeNonAudio = args.Contains("--all-devices");

var sources = new List<IDeviceSource>();
#if SAMAAT_WINDOWS
sources.Add(new Samaat.Platform.Windows.WindowsDeviceSource(includeNonAudio: includeNonAudio));
#else
if (!simulate)
{
    Console.Error.WriteLine("Real Bluetooth is only implemented on Windows so far; using the simulator.");
    simulate = true;
}
#endif
SimulatorDeviceSource? simulator = null;
if (simulate)
{
    simulator = new SimulatorDeviceSource(TimeSpan.FromSeconds(1));
    sources.Add(simulator);
}

await using var registry = new DeviceRegistry(sources);
registry.SourceFailed += (_, failure) => Console.Error.WriteLine($"[{failure.Source.Name}] {failure.Error.Message}");
await registry.RefreshAsync();

if (registry.Devices.Count == 0)
{
    Console.WriteLine("No paired Bluetooth audio devices found.");
    return;
}

foreach (var device in registry.Devices)
{
    Print(device);
}

if (!watch)
{
    return;
}

Console.WriteLine("Watching for battery changes. Press Ctrl+C to stop.");
foreach (var device in registry.Devices)
{
    if (device.GetFeature<IBatteryFeature>() is { } battery)
    {
        battery.Changed += (_, state) => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {device.Identity.Name}: {Summarize(state)}");
    }

    device.ConnectionChanged += (_, _) => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {device.Identity.Name}: {device.Connection}");
}

var done = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    done.TrySetResult();
};
await done.Task;

static void Print(IEarbudDevice device)
{
    var id = device.Identity;
    Console.WriteLine($"{id.Name}  [{device.Connection}, {id.Tier} support]");
    if (id.Manufacturer is not null || id.Model is not null)
    {
        Console.WriteLine($"  {id.Manufacturer} {id.Model}".TrimEnd());
    }

    var battery = device.GetFeature<IBatteryFeature>();
    Console.WriteLine(battery is null
        ? "  Battery: not reported"
        : $"  Battery ({battery.Source}): {Summarize(battery.Current)}");

    foreach (var capability in BatteryCapabilities.For(device))
    {
        var status = capability.UiState == CapabilityUiState.Enabled ? "available" : capability.DisabledReason;
        Console.WriteLine($"    {capability.Feature,-18} {status}");
    }

    Console.WriteLine();
}

static string Summarize(BatteryState state) => state.Readings.Count == 0
    ? "no reading yet"
    : string.Join("  ", state.Readings
        .OrderBy(r => r.Component)
        .Select(r => $"{r.Component}={(r.Percent is { } p ? $"{p}%" : "?")}{(r.IsCharging ? "⚡" : "")}"));

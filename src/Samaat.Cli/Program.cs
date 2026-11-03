// samaat-cli: prints what Samaat detects for each device. Used to check real hardware (Phase 0)
// without the UI.
//
//   samaat-cli [--watch] [--simulate] [--all-devices]

using Samaat.Core.Battery;
using Samaat.Core.Bluetooth;
using Samaat.Core.Capabilities;
using Samaat.Core.Connections;
using Samaat.Core.Devices;
using Samaat.Core.NoiseControl;
using Samaat.Core.Profiles;
using Samaat.Simulator;

var watch = args.Contains("--watch");
var simulate = args.Contains("--simulate");
var includeNonAudio = args.Contains("--all-devices");

var sources = new List<IDeviceSource>();
#if SAMAAT_WINDOWS
var driverRegistry = new Samaat.Core.Drivers.DriverRegistry(
    Samaat.Core.Drivers.DeviceCatalog.FromDirectory(Path.Combine(AppContext.BaseDirectory, "devices")));
driverRegistry.Register(new Samaat.Protocols.Jieli.JieliDriver());
sources.Add(new Samaat.Platform.Windows.WindowsDeviceSource(includeNonAudio: includeNonAudio, registry: driverRegistry));
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

var profiles = DeviceProfileDb.Load(Path.Combine(AppContext.BaseDirectory, "devices"));
foreach (var error in profiles.Errors)
{
    Console.Error.WriteLine($"[device profiles] {error}");
}

foreach (var device in registry.Devices)
{
    Print(device, profiles);
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

static void Print(IEarbudDevice device, DeviceProfileDb profiles)
{
    var id = device.Identity;
    Console.WriteLine($"{id.Name}  [{device.Connection}, {id.Tier} support]");
    if (id.Manufacturer is not null || id.Model is not null)
    {
        Console.WriteLine($"  {id.Manufacturer} {id.Model}".TrimEnd());
    }

    if (device.GetFeature<Samaat.Core.Drivers.IDeviceDiagnostics>() is { } diagnostics)
    {
        Console.WriteLine($"  Detection: {diagnostics.Resolution.Description}");
        if (diagnostics.Signals.Services.Count > 0)
        {
            Console.WriteLine($"  Services: {string.Join(", ", diagnostics.Signals.Services)}");
        }
    }

    if (profiles.Match(id) is { } profile)
    {
        Console.WriteLine($"  Identified as {profile.DisplayName} ({profile.Id})");
    }

    if (id.Details is { } details)
    {
        Console.WriteLine($"  Address: {details.Address ?? "not reported"}");
        Console.WriteLine($"  VID/PID: {(details.VendorId is { } v && details.ProductId is { } p ? $"0x{v:X4}/0x{p:X4} ({BluetoothVendors.Name(v) ?? "unknown vendor"})" : "not reported")}");
        Console.WriteLine($"  Bluetooth services: {(details.Services.Count > 0 ? string.Join(", ", details.Services) : "not reported")}");
    }

    var battery = device.GetFeature<IBatteryFeature>();
    Console.WriteLine(battery is null
        ? "  Battery: not reported"
        : $"  Battery ({battery.Source}): {Summarize(battery.Current)}");

    if (device.GetFeature<INoiseControlFeature>() is { } noise)
    {
        Console.WriteLine($"  Noise control: {NoiseControlPresentation.Label(noise.Current.Mode)}");
    }

    if (device.GetFeature<IMultipointFeature>() is { } multipoint)
    {
        Console.WriteLine($"  Hosts: {string.Join(", ", multipoint.Current.Hosts.Select(h => $"{h.Name} ({h.State})"))}");
    }

    var capabilities = DeviceCapabilities.For(device);
    foreach (var capability in capabilities)
    {
        var status = capability.UiState == CapabilityUiState.Enabled ? "available" : capability.DisabledReason;
        Console.WriteLine($"    {capability.Feature,-24} {status}");
    }

    Console.WriteLine();
}

static string Summarize(BatteryState state) => state.Readings.Count == 0
    ? "no reading yet"
    : string.Join("  ", state.Readings
        .OrderBy(r => r.Component)
        .Select(r => $"{r.Component}={(r.Percent is { } p ? $"{p}%" : "?")}{(r.IsCharging ? "⚡" : "")}"));

using Samaat.Core.Battery;
using Samaat.Core.Connections;
using Samaat.Core.Devices;
using Samaat.Core.Equalizer;
using Samaat.Core.NoiseControl;

namespace Samaat.Simulator;

/// <summary>
/// The worst realistic data the UI will meet, one failure per device so they show up side by side. Every value is
/// something real users produce: Bluetooth names are user-editable and unbounded in practice (Windows accepts up to
/// 248 bytes), vendor apps name earbuds after their owner, hosts arrive from the earbuds with whatever name each
/// device has, and many earbuds list a case they never report.
/// </summary>
public static class WorstCaseDevices
{
    public static IReadOnlyList<SimulatedEarbudDevice> Create(TimeProvider time, TimeSpan latency)
    {
        // 1. A long owner-named model (how Samsung's app names earbuds), every field filled, many linked hosts,
        //    one bud full, the other at 1 %, and a case that's listed but never reported.
        const string longName = "Aleksandra Wiśniewska-Kowalczyk's Galaxy Buds2 Pro";
        var noise = new SimulatedNoiseControl(
            new NoiseControlOptions([NoiseMode.Off, NoiseMode.NoiseCancelling, NoiseMode.Transparency, NoiseMode.Adaptive],
                AncLevel: new LevelRange(0, 10), AmbientLevel: new LevelRange(0, 10), WindReduction: true, ConversationAwareness: true),
            latency, time);
        var multipoint = new SimulatedMultipoint(
            new MultipointOptions(MaxConnections: 2, CanSwitch: true, CanDisconnect: true, CanToggleMultipoint: true),
            new MultipointState(true,
            [
                new HostLink("this-pc", "DESKTOP-7Q2M4KD", HostKind.Computer, HostLinkState.Active, IsThisHost: true),
                new HostLink("mbp", "Bartholomew Fitzgerald's MacBook Pro (14-inch, 2023)", HostKind.Computer, HostLinkState.Connected, IsThisHost: false),
                new HostLink("jo", "Jo", HostKind.Phone, HostLinkState.Paired, IsThisHost: false),
                new HostLink("tv", "Living-room Android TV (Sony Bravia XR-65A80L)", HostKind.Unknown, HostLinkState.Unavailable, IsThisHost: false),
                new HostLink("ipad", "iPad", HostKind.Tablet, HostLinkState.Paired, IsThisHost: false),
                new HostLink("s24", "Galaxy S24 Ultra", HostKind.Phone, HostLinkState.Paired, IsThisHost: false),
                new HostLink("work", "NORTHWIND-LT-00417", HostKind.Computer, HostLinkState.Unavailable, IsThisHost: false),
            ]),
            latency, time);
        var eq = new SimulatedEqualizer(
            [new EqPreset(0, "Normal"), new EqPreset(1, "Bass boost"), new EqPreset(2, "Soft"), new EqPreset(3, "Dynamic"),
             new EqPreset(4, "Clear"), new EqPreset(5, "Treble boost"), new EqPreset(6, "Custom: Podcasts and long voice calls")],
            latency, time);
        var full = new SimulatedEarbudDevice(
            new DeviceIdentity("worst-long", longName, "Samsung Electronics Co., Ltd.", "Galaxy Buds2 Pro (SM-R510)", "R510XXUAAWH1",
                SupportTier.Full,
                new DeviceDetails { Address = "F2:28:99:5A:7E:79", VendorId = 0x0075, ProductId = 0xA013,
                    Services = ["A2DP", "AVRCP", "HFP", "HSP", "Vendor (Samsung)", "Fast Pair", "LE Audio", "Battery Service"] }),
            SimulatedBatteryLayout.BudsAndCase, time,
            [noise, eq, multipoint, SimulatorDeviceSource.Diagnostics(longName)], latency,
            levels: new Dictionary<BatteryComponent, int> { [BatteryComponent.Left] = 100, [BatteryComponent.Right] = 1 },
            unreported: new HashSet<BatteryComponent> { BatteryComponent.Case });

        // 2. A two-letter name with no model or firmware, one bud flat at 0 % and the other never reported.
        var jo = new SimulatedEarbudDevice(
            new DeviceIdentity("worst-short", "Jo", "Jieli", null, null, SupportTier.Standard),
            SimulatedBatteryLayout.BudsOnly, time, [SimulatorDeviceSource.Diagnostics("Jo")], latency,
            levels: new Dictionary<BatteryComponent, int> { [BatteryComponent.Left] = 0 },
            unreported: new HashSet<BatteryComponent> { BatteryComponent.Right });

        // 3. A right-to-left name (Urdu, "Abdul Moiz's earbuds") at a full single level.
        const string urdu = "عبدالمعیز کے ایئربڈز";
        var rtl = new SimulatedEarbudDevice(
            new DeviceIdentity("worst-rtl", urdu, null, null, null, SupportTier.Basic),
            SimulatedBatteryLayout.CombinedOnly, time, [SimulatorDeviceSource.Diagnostics(urdu)], latency,
            levels: new Dictionary<BatteryComponent, int> { [BatteryComponent.Combined] = 100 });

        // 4. A Japanese name with an emoji, nothing known about it, disconnected, and no level ever reported.
        const string emoji = "ソニー WF-1000XM5 🎧";
        var away = new SimulatedEarbudDevice(
            new DeviceIdentity("worst-away", emoji, null, null, null, SupportTier.Basic),
            SimulatedBatteryLayout.CombinedOnly, time, [SimulatorDeviceSource.Diagnostics(emoji)], latency,
            unreported: new HashSet<BatteryComponent> { BatteryComponent.Combined });
        away.SetConnected(false);

        return [full, jo, rtl, away];
    }
}

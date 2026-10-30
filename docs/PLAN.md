# Samaat: Project Plan (Draft v0.1)

> **Samaat** (سماعت, "hearing") is one app to control Bluetooth earbuds and headphones from any brand.
> It identifies the connected device, works out what it can do, and shows only the controls that actually work.

Status: **draft for iteration**. It merges the two earlier plans ("Universal Bluetooth Earbuds Control Center" and "Universal Earbuds Hub Blueprint"), fixes the gaps found while reviewing them, and lists the decisions still open (§13).

---

## 1. Review of the two source plans

Both plans agree on the core idea: one app, a capability-driven UI, plugin adapters for each vendor, and never faking a control. That idea stays. These are the things the merged plan keeps, changes or adds.

### Kept
- A UI driven by capabilities. Unsupported features are shown greyed out with a reason, not hidden.
- A plugin adapter for each vendor, behind a common interface.
- Clear notes on limits for multipoint, gestures and EQ.
- Separating the app's own audio processing from the EQ built into the device (Plan 1).
- Splitting the solution into Core, Bluetooth, Protocols, Data, Platform and App projects (Plan 2).
- C# / .NET with Avalonia, Windows first (Plan 2's recommendation, re-checked in §6).

### Changed or fixed

| # | Issue in the source plans | Fix in this plan |
|---|---|---|
| 1 | **Too much weight on BLE/GATT.** Most current earbud control protocols (Samsung Galaxy Buds, Sony, Nothing, Anker Soundcore, Huawei and others) run over **Bluetooth Classic RFCOMM/SPP** using a vendor service UUID, not over GATT. | The transport layer treats **RFCOMM as the main control channel**. GATT is secondary, and L2CAP is reserved for special cases. |
| 2 | **"Capability detection" was presented as generic probing.** Proprietary features can't be found by scanning. You have to know the protocol first. | Detection is really: **identify, then load a profile, then confirm at runtime** (§4). Unknown devices get the generic tier only. |
| 3 | Plan 1 has 3 capability states. Plan 2 has 4, then falls back to plain `bool`s in its own code sample. | One richer model separates *device support*, *Samaat support*, *runtime availability* and *verification* (§4.2). |
| 4 | The adapter interface is one large interface (`getGestures`, `setNoiseControl` and so on), so every adapter has to stub out what it doesn't support. | Adapters **opt into small feature interfaces** (`IBatteryFeature`, `INoiseControlFeature` and so on). The registered features *are* the capability set (§5). |
| 5 | Everything is request/response polling. | Driven by events. Earbuds push battery, mode and wear changes, and the UI subscribes to them. |
| 6 | Both roadmaps start with UI and leave the riskiest unknown (can we talk to real buds from Windows at all?) until weeks 4–8. | A **Phase 0 feasibility spike** comes first, using real hardware and no UI (§9). |
| 7 | Free generic battery reading was missed. Many earbuds already report battery to the OS over **HFP** (HF indicators, or Apple's `AT+IPHONEACCEV`), and Windows shows it. | The generic adapter reads the OS-reported HFP battery and the BLE Battery Service (0x180F/0x2A19), so even unknown devices get *some* battery value. |
| 8 | The device identification signals left out the strongest fingerprints for Classic devices. | Added the **Device ID profile (VID/PID/version)**, **SDP vendor service UUIDs** and Class of Device (§4.1). |
| 9 | No plan for developing without hardware, and no plan for reproducible tests. | A **device simulator** plus **record/replay transport traces** become part of the core tooling (§10). |
| 10 | Legal and licensing risk wasn't covered. Many reference implementations are GPL/AGPL. | Protocol work is done **clean-room**, with rules for using references and trademarks (§11). |
| 11 | Timelines were given in weeks with no exit criteria. | Milestones with **definitions of done** (§9). |
| 12 | Platform limits were understated. iOS can't open RFCOMM to non-MFi accessories, so most vendor protocols are impossible there. | A platform matrix with an explicit "iOS: BLE-only, low priority" (§6). |

---

## 2. Vision and product principles

**Vision:** One consistent dashboard for every earbud you own, so you don't need a separate app for each brand.

**Principles**
1. **Honest compatibility.** A control is enabled only when Samaat can actually carry it out on *this* device, firmware and connection.
2. **Graceful tiers.** Any Bluetooth audio device gets *something* (connection info, OS battery). Known devices get full control.
3. **Adapters are cheap to add.** Adding a new model should mostly mean data (a profile JSON). A new protocol family means one adapter project, with no changes to Core or the UI.
4. **Safe by default.** Never write unknown bytes to a device. Firmware updates stay out of scope until a vendor-sanctioned path exists.
5. **Local-first and private.** No account, and no telemetry unless the user opts in.

### Support tiers (shown to the user)

| Tier | Meaning | Example |
|---|---|---|
| **Basic** | Any Bluetooth audio device: connection state, profiles, codec where the OS exposes it, OS-reported battery | Unknown no-name buds |
| **Standard** | Uses standard services: BLE Battery, Device Information, LE Audio VCP/HAP where present | Some newer LE Audio buds |
| **Full** | A vendor adapter is implemented and verified for this model: L/R/case battery, ANC, EQ, gestures and so on | First target device (§13) |

---

## 3. Feature scope

| Module | MVP (v0.1) | v0.2 | Later | Notes |
|---|---|---|---|---|
| Device discovery and dashboard | ✅ | | | Paired devices, connection state, identification result |
| Battery: OS/HFP generic | ✅ | | | Single combined value |
| Battery: L / R / case + charging | ✅ (first adapter) | | | Vendor protocol only |
| Noise control (ANC / Transparency / Off) | ✅ (first adapter) | | | UI built from the modes the device reports |
| ANC level, adaptive, wind, conversation awareness | | ✅ | | Per-device options |
| Device EQ (presets + custom bands) | | ✅ | | The band count and range come from the profile |
| Gesture remapping | | ✅ | | Vendor protocol only |
| Wear detection / auto-pause toggles | | ✅ | | |
| Firmware *info* | ✅ | | | Read-only |
| Multipoint: host-side connect/disconnect | | ✅ | | OS APIs, this host only |
| Multipoint: earbud-side switching / list of hosts | | | ✅ | Only where the vendor protocol exposes it |
| App-side EQ (system audio processing) | | | ✅ | Separate feature, labelled "Samaat EQ", not "Device EQ" |
| Find my earbuds (play tone) | | | ✅ | |
| Fit test, hearing features | | | ✅ | Regulatory review needed for hearing-health claims |
| Firmware *updates* | | | ❌ for now | Only with a vendor-sanctioned path |
| Diagnostics / "Lab" view | ✅ (dev build) | ✅ (opt-in) | | SDP/GATT explorer, packet log export, used to add new devices |

---

## 4. Capability engine (the core of the app)

### 4.1 Identification pipeline

```
Paired/connected device
  → collect signals: name, Class of Device, Device ID (VID/PID/ver),
                     SDP service UUIDs, BLE adv manufacturer data, GATT services
  → match against device profile DB  (most specific match wins)
  → pick adapter (protocol family) + model profile
  → adapter handshake: open channel, query model/firmware/feature report
  → build CapabilitySet (profile defaults ⊕ runtime-confirmed facts)
  → UI renders from CapabilitySet; adapter streams state events
```

Fallbacks: no profile match → **Generic adapter** (Basic/Standard tier). The profile matches but the handshake fails → capabilities are marked *Unavailable (connection)*, not *Unsupported*.

### 4.2 Capability model

Every feature is described on four separate axes, so the UI can always explain *why* a control is disabled.

```csharp
public sealed record Capability(
    FeatureId Feature,              // e.g. NoiseControl, BatteryCase, Eq
    DeviceSupport Device,           // Supported | Unsupported | Unknown
    SamaatSupport Implementation,   // Implemented | NotImplemented
    Availability Runtime,           // Available | Unavailable(reason)  e.g. "case closed", "channel busy"
    Verification Verified,          // Verified (tested on this fw) | Unverified
    Access Access,                  // ReadOnly | ReadWrite
    CapabilitySource Source,        // Profile | RuntimeQuery | StandardService | Os
    IReadOnlyDictionary<string, object>? Options // e.g. modes=[Off,Anc,Ambient], eqBands=5, ancLevels=0..10
);
```

**UI mapping**

| Device | Samaat | Runtime | UI |
|---|---|---|---|
| Supported | Implemented | Available | Enabled (with an "unverified" badge if not verified) |
| Supported | Implemented | Unavailable | Disabled: "Put buds in your ears / reconnect" |
| Supported | NotImplemented | – | Disabled: "Supported by your device, not yet by Samaat" |
| Unsupported | – | – | Greyed out: "Not supported by this model" |
| Unknown | – | – | Greyed out: "Couldn't detect" + a link to the Lab view |

### 4.3 Device profile DB

JSON files in the repo (`devices/<vendor>/<model>.json`), loaded when the app starts. Community contributions go through PRs.

```json
{
  "id": "vendor.model-x",
  "displayName": "Vendor Model X",
  "match": { "deviceId": { "vid": "0x0075", "pid": "0x0101" }, "sdpUuids": ["<vendor-uuid>"], "namePattern": "^Model X" },
  "adapter": "vendor-family-a",
  "features": {
    "battery.left": {}, "battery.right": {}, "battery.case": {},
    "noiseControl": { "modes": ["off", "anc", "ambient"] },
    "eq": { "bands": 5, "rangeDb": [-6, 6], "presets": ["normal", "bass", "voice"] },
    "gestures": { "slots": ["tap2", "tap3", "hold"], "actions": ["playPause", "next", "prev", "anc", "assistant", "none"] }
  },
  "verifiedFirmware": ["R510XXU0AWA1"]
}
```

---

## 5. Architecture

```
┌──────────────────────────────── Samaat.App (Avalonia, MVVM) ─────────────────────────────────┐
│ Dashboard · Sound · Controls · Connections · Info · Lab · Settings  (render from CapabilitySet) │
└───────────────────────────────────────────────┬───────────────────────────────────────────────┘
┌──────────────────────────────── Samaat.Core ─────────────────────────────────────────────────┐
│ DeviceManager · Identifier · ProfileRegistry · AdapterRegistry · CapabilitySet · StateStore    │
│ CommandDispatcher (queue, timeouts, retries, optimistic UI + rollback)                         │
└───────────────┬───────────────────────────────┬──────────────────────────────┬────────────────┘
   Samaat.Protocols.Generic        Samaat.Protocols.<VendorFamily> …     Samaat.Protocols.Standard
   (OS battery, codec, conn)       (framing, CRC, message codecs)        (BAS, DIS, LE Audio VCP/HAP)
                └───────────────────────────────┬──────────────────────────────┘
┌──────────────────────────────── Transport abstraction ───────────────────────────────────────┐
│ IRfcommChannel · IGattClient · IL2capChannel · IHostBluetooth (paired list, connect, DI/SDP)   │
└───────────────┬───────────────────────────────┬──────────────────────────────┬────────────────┘
   Samaat.Platform.Windows (WinRT)    Samaat.Platform.Android (later)   Samaat.Platform.Linux (BlueZ, later)
                                      Samaat.Simulator (fake devices + trace replay)
```

### Adapter contract (feature-based)

```csharp
public interface IEarbudAdapter : IAsyncDisposable
{
    string Family { get; }
    Task<DeviceIdentity> HandshakeAsync(ITransportSet t, CancellationToken ct);
    IReadOnlyCollection<IFeature> Features { get; }   // what this device + adapter can do
}

public interface IBatteryFeature : IFeature
{
    IObservable<BatteryState> State { get; }        // L, R, case, charging flags, timestamp
}

public interface INoiseControlFeature : IFeature
{
    IReadOnlyList<NoiseMode> Modes { get; }
    IObservable<NoiseControlState> State { get; }
    Task<CommandResult> SetModeAsync(NoiseMode mode, CancellationToken ct);
}
// IEqFeature, IGestureFeature, IWearDetectionFeature, IMultipointFeature, IFirmwareInfoFeature ...
```

Rules:
- **Protocol code is pure.** It turns bytes into messages and back, with no OS calls, so it can be unit-tested against recorded traces.
- **One channel owner.** Only the adapter writes to the vendor channel, and the dispatcher serializes commands.
- **Writes are confirmed.** The UI waits for the device's echo or status event before treating a write as committed, and rolls back on timeout.

---

## 6. Platforms and stack

| Platform | RFCOMM vendor protocols | BLE/GATT | OS battery | Verdict |
|---|---|---|---|---|
| **Windows 10/11** | ✅ WinRT `RfcommDeviceService` by vendor UUID | ✅ | ✅ (HFP battery shown by the OS) | **First.** Good for development and debugging, and users exist here |
| **Android** | ✅ public RFCOMM socket API (proven by Gadgetbridge and vendor apps) | ✅ | ✅ | **Second, and the biggest audience.** Strong case for making it first (§13) |
| Linux | ✅ BlueZ + RFCOMM sockets (needs P/Invoke from .NET) | ✅ BlueZ D-Bus | ✅ (BlueZ battery provider) | Third |
| macOS | ⚠️ IOBluetooth (native interop, Apple tightening) | ✅ | ⚠️ | Fourth |
| iOS | ❌ no RFCOMM without MFi | ✅ | ❌ | BLE-only devices; low priority |

**Stack:** .NET 10, Avalonia 11 (desktop now, Android later from the same UI code), CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection/Hosting, System.Reactive (state streams), System.Text.Json, SQLite (preferences and history only; profiles stay JSON), Serilog, xUnit + Verify (snapshot tests for decoded traces).

Alternative if we go Android-first: Kotlin + Jetpack Compose, with Kotlin Multiplatform for the protocol core and Compose Desktop for Windows. The catch is that Windows Bluetooth access from the JVM needs a native shim. Decide in §13.

---

## 7. UI

Navigation: **Dashboard · Sound · Controls · Connections · Info · Settings** (+ **Lab** in dev/opt-in builds).

- **Dashboard:** device card (name, model, tier badge, connection), battery tiles for L/R/case with charging state and "updated X s ago", a quick noise-mode switch, and a compact capability list.
- **Unsupported features:** shown greyed out with a reason (from the §4.2 table), grouped under a collapsible "Not available on this device" so they don't crowd the screen.
- **Multiple devices:** a device switcher at the top. Each device keeps its own state.
- **System tray (Windows):** battery at a glance, noise-mode toggle, low-battery notifications. Users want this more than a full window.
- Dark and light themes, accessible contrast, keyboard navigable.

---

## 8. Multipoint, gestures and EQ: limits we commit to stating in the UI

- **Multipoint:** Samaat can connect and disconnect *this* host. It can list or switch *other* hosts only when the vendor protocol exposes it. A cross-device companion is a "later" idea, not a promise.
- **Gestures:** remapping needs the vendor protocol. Standard AVRCP only *receives* the result of a gesture.
- **EQ:** "Device EQ" (stored on the buds, applies everywhere) and "Samaat EQ" (applies only to this PC's audio) are separate controls with separate labels.

---

## 9. Roadmap (milestones with exit criteria)

**Phase 0: Feasibility spike (≈1 week, no UI).** Console app on Windows:
- Lists paired devices with Device ID, SDP UUIDs and OS battery.
- Opens the vendor RFCOMM channel on **our first target earbuds** and logs incoming frames.
- Decodes battery and sends one ANC mode change, confirmed by the device.
- *Done when:* a trace file is committed and the ANC toggle works from the CLI. If this fails, we revisit the platform before building anything else.

**Phase 1: Core and skeleton (1–2 weeks).** Solution structure, transport abstraction, Windows transport, profile DB loader, capability model, simulator with 2 fake devices, CI (build + tests).
- *Done when:* the app shows a simulated device with a correctly greyed-out capability set.

**Phase 2: Generic tier (1 week).** Real paired devices, connection state, OS/HFP battery, BLE BAS/DIS, identification + "Unknown device" path.
- *Done when:* any paired headset shows as Basic tier with battery where the OS has it.

**Phase 3: First vendor adapter, MVP (2–3 weeks).** L/R/case battery, noise modes, firmware info, reconnect handling, tray icon.
- *Done when:* daily use works for a week without a crash or state drift. **Ship v0.1 (Windows).**

**Phase 4: Depth (2–4 weeks).** EQ, gestures, wear detection, ANC levels for the first adapter. Lab view for contributors.
- *Done when:* the first device is feature-equivalent to its vendor app for the top 5 features.

**Phase 5: Breadth (ongoing).** Second and third adapter families, host-side multipoint, Android port.
- *Done when:* 3 protocol families are verified on real hardware. Only then do we say "universal".

---

## 10. Quality and testing

- **Trace-driven tests.** Every adapter ships recorded byte traces with expected decoded states (snapshot tests). Bug reports attach a Lab trace.
- **Simulator.** Implements the transport interfaces with scripted devices (battery drain, case open/close, mode echo, disconnects) for UI work and CI.
- **Contract tests.** A shared suite every adapter must pass: timeouts, reconnects, unknown frames ignored, no writes before the handshake.
- **Hardware matrix.** A table in `docs/SupportedDevices.md` of model × firmware × platform × verified features.
- **Telemetry:** none by default. Crash logs are kept locally and exported by the user.

---

## 11. Legal, licensing and safety

- **Clean-room protocol work.** We learn protocols from our *own* captures (Android HCI snoop log + Wireshark, Windows BT ETW traces). Open-source projects such as Gadgetbridge (AGPL) and GalaxyBudsClient (GPL) may be used as **behavioural references only**. Copying their code would bind Samaat to their license. Decide our license first (§13).
- **Interoperability.** Reverse engineering for interoperability is broadly permitted in many places (e.g. EU Software Directive Art. 6, US DMCA §1201(f)), but we don't bypass encryption or authentication and we don't redistribute vendor firmware. Get proper legal advice before any commercial launch.
- **Trademarks.** Use "works with Sony WF-…" style wording. No vendor logos and no implied endorsement.
- **Device safety.** Allow-list only known commands for each profile. No fuzzing on users' devices. No firmware writes.

---

## 12. Repository layout

```
samaat.io/
├── src/
│   ├── Samaat.App/                  # Avalonia UI, views, view models, tray
│   ├── Samaat.Core/                 # capability model, managers, dispatcher, registries
│   ├── Samaat.Transport/            # IRfcommChannel, IGattClient, IHostBluetooth abstractions
│   ├── Samaat.Platform.Windows/     # WinRT implementations
│   ├── Samaat.Protocols.Generic/
│   ├── Samaat.Protocols.Standard/   # BAS, DIS, LE Audio
│   ├── Samaat.Protocols.<Family>/   # one per vendor protocol family
│   ├── Samaat.Simulator/
│   └── Samaat.Spike/                # Phase 0 console app
├── devices/                         # profile JSONs, by vendor
├── traces/                          # recorded byte traces for tests
├── tests/
│   ├── Samaat.Core.Tests/
│   ├── Samaat.Protocols.Tests/      # trace snapshot + contract tests
│   └── Samaat.App.Tests/
└── docs/
    ├── PLAN.md                      # this file
    ├── Architecture.md
    ├── AddingADevice.md
    └── SupportedDevices.md
```

---

## 13. Open decisions (to settle in the next iteration)

1. **Which earbuds do you physically own?** This picks the first adapter and is the most important input. Phase 0 can't start without real hardware.
2. **Windows-first or Android-first?** Windows is easier to develop and debug on. Android is where most earbud users are. This also decides .NET/Avalonia versus Kotlin.
3. **Target market and brands.** Global flagships (Samsung, Sony, Nothing, Soundcore) or budget brands popular in a specific region (e.g. QCY, Xiaomi/Redmi, realme, Lenovo, local brands)? This sets the order of adapters.
4. **License and business model.** Open source (GPL-compatible, community-driven device support) or closed/commercial? This affects §11 and whether contributors can help.
5. **Lab/diagnostics:** ship to end users as opt-in, or keep it to developer builds?
6. **Name and domain:** "Samaat" for the product, samaat.io for the site. Do we need a landing page in this repo as well?

# Architecture

Samaat is one app that adapts to whatever earbuds are connected. Its central problem is "many vendors, one UI". It borrows proven patterns from [OpenMouse](https://github.com/OpenMouse-Project/openmouse) and its [mouse-protocol](https://github.com/OpenMouse-Project/mouse-protocol) library, which solve the same problem for mice. OpenMouse is AGPL-3.0, so we reuse **ideas, not code**.

Three principles shape every layer:

1. **Features, not models.** A driver turns bytes into feature modules (`IBatteryFeature`, `INoiseControlFeature`, …). The UI renders whatever features a device has, so a new vendor never needs UI changes.
2. **Honest capabilities.** Every feature is described as *device support × Samaat support × available now × verified on hardware* (`Samaat.Core/Capabilities`). A disabled control always says why, and "unknown" is never shown as "unsupported".
3. **Confirmed writes.** A change is applied, then confirmed by the device, and rolled back if it isn't confirmed (`CommandRunner`, `CommandResult`). There is no "pending changes" bar.

## Projects and layers

Dependencies point inwards. `tests/Samaat.Core.Tests/ArchitectureTests.cs` reads every `.csproj` and fails the build on a reference this table doesn't allow. A new project has to be added there on purpose.

```
                    ┌────────────────────────── composition roots ──────────────────────────┐
                    │  Samaat.App (Avalonia desktop)            Samaat.Cli (hardware check)   │
                    └────┬──────────────┬───────────────┬───────────────┬──────────────┬─────┘
                         │              │               │               │              │
                    Samaat.UI     Samaat.Drivers   Samaat.Simulator  Samaat.Handoff  Samaat.Platform.Windows
                 (design system)  (shipped list)   (fake devices)    (phone link)    (WinRT, WASAPI, DPAPI)
                                        │               │               │              │   │
                     ┌──────────────────┼───────────────┘               │              │   └─ Protocols.Standard
                     │   Samaat.Protocols.Jieli · FastPair · Bose       │              │
                     │   (codec + IEarbudDriver per vendor)             │              │
                     └──────────────────┬───────────────────────────────┴──────────────┘
                                ┌───────┴────────┐
                                │  Samaat.Core   │  features, capabilities, commands, drivers' contracts,
                                └───────┬────────┘  catalog, device registry, settings, platform interfaces
                                ┌───────┴────────┐
                                │Samaat.Transport│  IGattClient · IRfcommChannel · IDeviceTransports
                                └────────────────┘
```

| Project | Role | May reference |
|---|---|---|
| `Samaat.Transport` | Byte transports: GATT, RFCOMM, a receive buffer | nothing |
| `Samaat.UI` | Design system shared with the phone: tokens, metrics, icons, fonts, artwork, battery ring | nothing (Avalonia only) |
| `Samaat.Core` | The domain: feature interfaces and models, capability model, commands, driver contracts, device catalog and registry, settings, platform interfaces (`INotifier`, `IStartupRegistration`, …) | Transport |
| `Samaat.Protocols.*` | One vendor or standard each: framing codec (bytes ↔ messages) and an `IEarbudDriver` | Core, Transport |
| `Samaat.Handoff` | Phone link: discovery, pairing, encrypted sessions, moving earbuds with rollback | Core |
| `Samaat.Simulator` | Simulated earbuds with realistic latency and injectable failures | Core |
| `Samaat.Drivers` | **The one list of shipped drivers** (`BuiltInDrivers`) | Core, Transport, vendor protocols |
| `Samaat.Platform.Windows` | Implements Core, Transport and Handoff contracts on Windows | Core, Transport, Handoff, Protocols.Standard |
| `Samaat.App` | Avalonia desktop app and the only place everything is wired together | Core, UI, Handoff, Drivers, Simulator, Platform.Windows |
| `Samaat.Cli` | Prints what Samaat detects, for checking real hardware without the UI | Core, Drivers, Simulator, Platform.Windows |

Rules the table encodes:

- **Core knows no vendor, platform or UI.** It stays testable on any OS without Avalonia; only `Samaat.UI` and `Samaat.App` may use Avalonia.
- **A protocol never references another protocol or a platform.** Codecs are tested on bytes alone.
- **Platforms implement contracts.** Windows implements `IGattClient`, `IRfcommChannel`, `IDeviceSource`, `INotifier`, Handoff's `ISecretProtector` and more. Android does the same in samaat-mobile's `Samaat.Platform.Android`.
- **Apps choose drivers only through `Samaat.Drivers`**, so registering a new vendor is one line in one place.

## How a device becomes a dashboard

1. The **platform** enumerates paired devices (`IDeviceSource`) and collects `DeviceSignals`: name, class of device, vendor and product ID, SDP service UUIDs and manufacturer.
2. `DriverRegistry.Resolve(signals)` matches the **catalog** (`devices/**/*.json`, loaded by `DeviceCatalog`). A model entry always beats a protocol-level `generic` entry: any Fast Pair Message Stream device gets `fast-pair`, and any Jieli vendor ID gets `jieli-rcsp`. Otherwise the most specific match wins ([scoring](../devices/README.md#matching)). The result is one of:
   - **Generic** (not in the catalog): the Windows battery level and the BLE Battery Service only.
   - **Recognised** (catalog entry, no driver): the catalog's features show as "supported by your device, not yet by Samaat".
   - **Driver**: `IEarbudDriver.OpenAsync(context)` gets the transports and returns the feature modules the earbuds actually answer for. A generic match whose earbuds don't answer falls back to the Windows battery within the 8 s driver budget (`DriverRetry`).
3. The device carries an `IDeviceDiagnostics` feature that records what was seen and how it resolved. Diagnostics and "Copy report" show it.
4. `DeviceRegistry` merges every source's devices. **View models** read features and `DeviceCapabilities.For(device)` and render only what exists.

## How a change reaches the earbuds

```
 view model ── optimistic UI ──► CommandRunner.RunAsync(command, 3 s) ──► feature.SetXAsync(ct) ──► driver ──► transport
      ▲                                │
      └── CommandResult ◄──────────────┘  Confirmed · Rejected · Unsupported · TimedOut · Failed
```

- Every write returns a `CommandResult`; nothing throws to the UI. The UI commits on `Confirmed` and rolls back otherwise, showing `UserMessage`.
- On timeout, `CommandRunner` **cancels the token the command received**, so a driver stops waiting and frees its channel for the next command. Only the caller's own cancellation propagates.
- `LatestValueSender` coalesces fast slider drags into the latest value.
- `HandoffCoordinator` uses the same results to roll back a phone move when its second half fails.

## The phone link

`Samaat.Handoff` sits beside Core and depends only on it. The computer runs a `PhoneLinkHost` and the phone a `PhoneLinkClient`. They pair by comparing a 6-digit code and then talk over an encrypted TCP session (AES-GCM). Paired phones are stored by `PeerStore`, encrypted through `ISecretProtector` (DPAPI on Windows). The computer's `HandoffCoordinator` runs every move using `IPcLinkFeature` on its side and an `IPhoneEndpoint` (the phone over the link) on the other.

The phone app lives in its own repo, [samaat-mobile](https://github.com/MuhammadAbdulMoiz/samaat-mobile). It builds `Samaat.Core`, `Samaat.Handoff`, `Samaat.UI` and the drivers from this repo through a submodule, so both apps share one copy. See [modules/phone-handoff.md](modules/phone-handoff.md).

```
 Samaat.App ── PhoneLinkViewModel ── PhoneLinkHost ──TCP/AES-GCM── PhoneLinkClient ── Samaat.Mobile ── Samaat.Android
                                       │ HandoffCoordinator                │ PhoneAgent
                                       ▼                                   ▼
                                 IPcLinkFeature (Windows)          IPhoneAudioLink (Android)
```

## The desktop app

`Samaat.App` is organised by feature, with MVVM (CommunityToolkit.Mvvm) throughout:

| Folder | Contents |
|---|---|
| `Shell/` | `MainWindow`, the sidebar and device list, toasts, and the three layouts (Studio, Focus, Glance in `Shell/Views`) |
| `Features/` | One folder per page, each with a view and its view model: `Overview`, `Sound` (noise control, EQ, audio sharing), `Connections` (Links), `Details`, `Info` (Diagnostics), `Phone`, `Settings` |
| `Shared/` | Converters, capability wording, layout controls (`SplitPanel`, `AdaptiveColumns`) and shared parts |
| `Styles/` | Component styles (`Base`, `Controls`, `Shell`, `Gauges`) built only from `Samaat.UI` tokens |
| `Tray/` | Tray icon, summary and close behaviour |
| `AppOptions.cs` | Composition: picks the simulator or Windows sources, platform services and the driver registry |

**Design system.** Theme colours live in `Samaat.UI/Styles/Tokens.axaml`, and radii, the type ramp, hit targets and shadows in `Metrics.axaml`. Views use classes and tokens, never raw values; `DesignSystemTests` enforces that, and `ContrastTests` checks every text and background pair against WCAG AA in both themes. Details and history: [design/ui-audit.md](design/ui-audit.md).

## Adding a vendor

1. Add `src/Samaat.Protocols.<Vendor>/` (references Core and Transport only) with a framing codec, tested against recorded byte traces.
2. Implement `IEarbudDriver`: open the vendor's `IRfcommChannel` or GATT service, then return feature modules.
3. Add the driver to `BuiltInDrivers.Create()` in `Samaat.Drivers`, and the project to that project's references and to `ArchitectureTests.Allowed`.
4. Add `devices/<vendor>/<model>.json` with `"driver": "<id>"`. `BuiltInDriversTests` fails if the id doesn't match a registered driver.
5. No UI changes are needed. A model is only `"verified": true` with hardware evidence ([CONTRIBUTING.md](../CONTRIBUTING.md)).

## Quality gates

| Gate | What it protects |
|---|---|
| `ArchitectureTests` | The layering above, Avalonia only in the UI layer, every project in the solution |
| `BuiltInDriversTests`, `ShippedDeviceTests` | Every shipped model resolves to its own entry and names a real driver |
| Protocol tests with recorded traces | Codecs stay byte-exact |
| `DesignSystemTests`, `ContrastTests` | Tokens are the only source of colour, type and shape; text stays readable |
| Headless UI tests | Every page renders in both themes; every button is at least 32 px and named |
| CI (Ubuntu and Windows) | `dotnet build` with warnings as errors, then `dotnet test` |

## Known debt

- `DeviceCatalog` (driver matching) and `DeviceProfileDb` (artwork and identity) both parse `devices/**/*.json`, each with its own DTO. Merging them into one parsed model would remove the duplicated parsing rules.
- `AppOptions` chooses platform services with `#if SAMAAT_WINDOWS` in each factory method. A single platform-services object per OS would keep that choice in one place once a second desktop platform arrives.
- The largest classes, `DeviceViewModel` (615 lines), `PhoneLinkHost` (628) and `PhoneLinkClient` (408), are the first candidates to split.

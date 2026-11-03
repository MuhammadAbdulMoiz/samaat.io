# Architecture

Samaat is one app that adapts to whatever earbuds are connected. The structure borrows proven patterns from [OpenMouse](https://github.com/OpenMouse-Project/openmouse) and its [mouse-protocol](https://github.com/OpenMouse-Project/mouse-protocol) library, a multi-vendor control panel for mice with the same "many vendors, one UI" problem. OpenMouse is AGPL-3.0: we reuse **ideas, not code**.

| OpenMouse pattern | Samaat |
|---|---|
| Protocol library separate from the app; codecs independent of transport | `Samaat.Protocols.*` work on bytes; `Samaat.Transport` defines `IRfcommChannel` / `IGattClient`; platforms implement them |
| Driver registry; the UI discovers supported clients, so new vendors need no UI changes | `DriverRegistry` + `IEarbudDriver`; drivers return feature modules and the UI renders from features |
| Device catalogs with a `verified` flag (recognised ≠ hardware-tested) | `devices/**/*.json` → `DeviceCatalog`; `verified` feeds the capability model's *Verification* axis |
| Controls appear only when the device advertises the feature | The capability model: supported / not implemented / unavailable now / unknown, each with a reason |
| Feature-organised app: shell, features, shared, per-area styles | `Samaat.App/Shell`, `Features/{Overview,Sound,Connections,Info}`, `Shared`, `Styles/*.axaml` |
| Per-device Advanced panel with diagnostics | **Info** page: identity, detection signals, resolution, capability ledger, event log, "Copy report" |
| Simulated-transport tests; hardware evidence rules for contributors | Simulator and fakes in tests; `CONTRIBUTING.md` hardware-evidence checklist |

We deliberately did **not** adopt OpenMouse's staged "pending changes" bar. Earbud settings such as ANC are expected to apply immediately, so Samaat applies each change, waits for the device to confirm it, and rolls back if it doesn't (`CommandRunner`).

## Layers

```
┌──────────── Samaat.App (Avalonia) ─────────────────────────────────────────────┐
│ Shell/        MainWindow, nav rail, device switcher, toasts                    │
│ Features/     Overview · Sound · Connections · Info   (page + view model each)  │
│ Shared/       converters, capability wording, controls (BatteryRing, waves)     │
│ Styles/       Tokens, Icons, Base, Shell, Gauges, Controls                       │
└───────────────────────────────┬────────────────────────────────────────────────┘
┌──────────── Samaat.Core ──────┴────────────────────────────────────────────────┐
│ Capabilities/  Capability, CapabilityFactory, DeviceCapabilities (all modules)  │
│ Battery/ NoiseControl/ Connections/   feature interfaces, models, rules         │
│ Commands/      CommandResult, CommandRunner (timeout), LatestValueSender        │
│ Drivers/       DeviceSignals, DeviceMatch, DeviceCatalog, DriverRegistry        │
│ Devices/       IEarbudDevice, IDeviceSource, DeviceRegistry                     │
└───────┬───────────────────────┬───────────────────────────────┬────────────────┘
  Samaat.Protocols.Standard   Samaat.Protocols.<Vendor> (next)   Samaat.Simulator
  (BLE Battery Service)       codec + IEarbudDriver               (fake devices)
        └───────────────────────┬───────────────────────────────┘
┌──────────── Samaat.Transport ─┴─────── IRfcommChannel · IDeviceTransports · IGattClient ┐
└───────┬──────────────────────────────────────────────────────────────────────────┘
  Samaat.Platform.Windows: discovery, signals (CoD, SDP UUIDs), OS battery, WinRT GATT + RFCOMM
```

## How a device becomes a dashboard

1. The **platform** enumerates paired devices and collects `DeviceSignals`: name, class of device, vendor and product ID, SDP service UUIDs and manufacturer.
2. `DriverRegistry.Resolve(signals)` matches the **catalog** and returns one of the cases below. A model entry always beats a protocol-level `generic` entry (`devices/generic/`: any Fast Pair Message Stream device gets `fast-pair`, any Jieli vendor ID gets `jieli-rcsp`); otherwise the most specific match wins ([scoring](../devices/README.md#matching)). The driver then returns only the features the buds actually answer for, so a generic match without them falls back to the Windows battery within the 8 s driver budget.
   - **Generic** (not in the catalog): OS battery and the BLE Battery Service only.
   - **Recognised** (catalog entry, driver not built yet): the catalog's features show as "supported by your device, not yet by Samaat".
   - **Driver**: `IEarbudDriver.OpenAsync(context)` gets the transports and returns feature modules (`IBatteryFeature`, `INoiseControlFeature`, `IMultipointFeature`, …).
3. The device carries an `IDeviceDiagnostics` feature that records what was seen and how it resolved. The Info page and the report show it.
4. **View models** read features and `DeviceCapabilities.For(device)`. Writes go through `CommandRunner`: optimistic, confirmed, rolled back on failure.

## Adding the first vendor

1. Add `src/Samaat.Protocols.<Vendor>/` with a framing codec (bytes ↔ messages), tested with recorded traces.
2. Implement `IEarbudDriver`: open `IRfcommChannel` for the vendor UUID, then expose features.
3. Register the driver in `AppOptions.CreateRegistry()` and add `devices/<vendor>/<model>.json`.
4. No UI changes are needed.

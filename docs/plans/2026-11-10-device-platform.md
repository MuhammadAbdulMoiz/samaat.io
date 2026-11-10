# Device compatibility platform: design proposal (Phase 3B to 3E)

**Status:** proposal for the owner's review. Nothing in this document is implemented. · **Date:** 2026-11-10

## The short version

The goal is that the tenth brand costs much less than the fifth. After reading the driver code, the catalog, the Windows device source and the diagnostics, my recommendation is:

- **Don't build a plugin system or a new driver framework.** The parts the roadmap asks for mostly exist: one driver contract, standard feature interfaces, a capability model that explains every missing feature, a data-driven catalog, and a diagnostics report.
- **Fix the four places where a new brand costs more than it should.** Each is a contained change, in this order:

| # | Change | What it buys | Size | Needs hardware? |
|---|---|---|---|---|
| 3B-1 | Move the device lifecycle out of the Windows layer into Core | Reconnect, retry and rebuild become testable with fakes; Android gets the same behaviour for free | Medium | To confirm, not to build |
| 3B-2 | One shared RFCOMM session core for the four near-identical sessions | A new RFCOMM brand writes framing and commands only, not timeouts, reply matching and cleanup | Medium | No (replay tests cover it) |
| 3C | One catalog loader and schema, with a support status and a generated compatibility table | One place says what works for each model; docs can't drift from data | Medium | No |
| 3D | A driver open trace in the diagnostics report, and export to a file with redaction | "My earbuds don't work" reports say why the driver gave nothing | Small | No |
| 3E | A driver conformance test kit, a capture file format, issue templates and a CI check for catalog files | A contributor knows when a driver is good enough, and CI checks it | Small to medium | No |

Five decisions are yours; they are listed at the end.

## What exists today

| The roadmap asks for | What the code has | Gap |
|---|---|---|
| A consistent driver lifecycle | `IEarbudDriver.OpenAsync` returns feature modules. Retry, the 8 s budget, reuse, rebuild on reconnect and the reconnect watches live in `WindowsDeviceSource` (about 470 lines of WinRT code) | The lifecycle is platform code, so none of it has a test, and Android has to repeat it |
| Device identification | `DeviceSignals` (name, class, vendor and product ID, service UUIDs) matched by `DeviceCatalog` with scores; protocol-level `generic` entries for unknown models | Solid. Two loaders read the same files (below) |
| Capability detection and reporting | `DeviceCapabilities.For(device)`: device support × Samaat support × available now × verified, with a reason for everything missing | Solid |
| Protocol isolation | One project per vendor, referencing only Core and Transport; `ArchitectureTests` enforces it; `BuiltInDrivers` is the one list | Solid |
| Standard battery, noise and EQ interfaces | `IBatteryFeature`, `INoiseControlFeature`, `IEqualizerFeature`, `IMultipointFeature`, `IDeviceInfoFeature` | Solid |
| Initialisation and recovery | Each driver hand-writes open → probe → build features → clean up on failure. Recovery is two styles: Jieli and Fast Pair revive themselves, the rest are rebuilt (`ReconnectsItself`) | Repeated code; the rebuild path is untested |
| Handling unsupported features | Never hidden, always with a reason; catalog `features` show as "supported by your device, not yet by Samaat" | Solid |
| A compatibility registry | `devices/**/*.json`: driver, match rule, `verified`, `verifiedFirmware`, `features`, `notes` | One boolean can't tell "ported, never run" from "recognised only"; limitations live in free text; the README table is written by hand |
| Diagnostics | Diagnostics tab and **Copy report**: signals, resolution, capabilities, event log, the last 40 log lines | No record of what the driver tried and where it stopped; no file export; no redaction |

## What makes a new brand expensive today

1. **The lifecycle can't be tested.** Everything that decides when a device is reused, rebuilt or retried is inside WinRT code. The reconnect bug fixed on 2026-11-10 lived there, and its fix is build-verified only for the same reason.
2. **Four sessions are the same class written four times.** `NothingSession` (189 lines), `SonySession` (189), `SoundcoreSession` (212) and `SamsungSession` (163) each implement attach, a write lock, reply matching with a timeout, a closed signal and disposal. They already differ by accident: until Phase 3A, three of them waited out the full timeout on a closed channel while the fourth failed at once.
3. **Channel ownership is a convention.** Drivers pass "the first feature built owns the channel" (`features.Count == 0`). Get it wrong and the channel leaks or is closed twice.
4. **Model knowledge is in three places.** A model's features are in its JSON file, again in a code table (`SonyModels`, `NothingModels`, `SoundcoreModels`, `SamsungModels`), and again in the README table.
5. **Two loaders, duplicated fields.** `DeviceCatalog` and `DeviceProfileDb` parse the same files with different field names, so an entry repeats itself: `vendorId` and `deviceId.vid`, `serviceUuid` and `sdpUuids`, `driver` and `adapter`.

## 3B-1: lifecycle in Core

Add one class to `Samaat.Core/Drivers`, working name `DeviceSessions`. The platform gives it three things per paired device: the signals, a way to open transports, and connection events. It owns everything else:

- resolve the catalog entry and driver;
- open the driver inside the time budget, and fall back to the standard battery when it gives nothing;
- decide reuse or rebuild on each discovery (`CanReuse`, `NeedsDriverRetry`, `NeedsRebuildOnReconnect` move in and become private);
- watch for reconnects and trigger the rebuild;
- dispose features, clients and transports exactly once, in the right order.

`WindowsDeviceSource` shrinks to enumeration, signals and transports. The Android source in samaat-mobile does the same.

- **Tests it makes possible:** a fake device that drops and returns 100 times; a driver that fails every third open; a dispose that races a rebuild; the budget expiring mid-open. These are the reconnect stress tests the roadmap asks for.
- **Risk:** this moves the code path that the ParadoX-E, the T200 Lite and the CMF Buds were checked against. The move must be behaviour-preserving, done in two steps (extract with the Windows source still calling the same logic, then switch), and followed by a hardware pass.
- **Not included:** a state machine with public states. The UI needs "connected, and what works"; it already has both.

## 3B-2: one RFCOMM session core

A small base in `Samaat.Transport` or Core, working name `RequestSession<TMessage>`: attach, serialised writes, "send and wait for the reply that matches", timeout, closed signal, idempotent dispose, and explicit ownership of the channel (the session owns it; features hold the session and never dispose the channel). Each vendor keeps its own framing, commands and parsing.

- **What a new RFCOMM brand writes afterwards:** a frame codec, a command list, and the features. Roughly half of today's session-plus-driver code.
- **Driver open boilerplate** (open channel → build session → probe → clean up quietly on any failure → never throw except on cancellation) becomes one helper that takes the probe as a function.
- **Risk:** three of the four sessions are ported files with provenance headers. The headers stay, and the "Changes:" line records the refactor. Their recorded byte traces are the safety net.
- **Not included:** Jieli (BLE, with authentication) and Fast Pair (reopen per refresh) keep their own sessions. Forcing them into the base would complicate it for no reuse.

## 3C: one catalog, with a support status

- **One loader.** `DeviceProfileDb` becomes a view over `DeviceCatalog`'s parsed entries. One set of field names; the old duplicates are still read for one release so samaat-mobile and outside forks don't break on the day.
- **Support status instead of one boolean.** Proposed values, each with a fixed meaning and UI wording:

  | Status | Meaning | Evidence required |
  |---|---|---|
  | `verified` | The full hardware checklist in CONTRIBUTING passed | A hardware test note, listed firmware |
  | `tested` | Ran on real hardware, checklist incomplete | A hardware test note |
  | `ported` | Protocol ported and replayed against another project's vectors; never run on hardware | The decision record |
  | `recognised` | Identified only; no driver | A device report |

  `verified: true` keeps working and maps to `verified`.
- **Structured limitations.** A `limitations` list per entry (for example "read-only", "no case battery") instead of prose in `notes`, shown on the Details page.
- **Features from one place.** The driver's per-model code tables stay the source of what the driver does; a test fails when a JSON entry's `features` disagree with its driver's table.
- **Generated compatibility table.** A test renders `docs/COMPATIBILITY.md` from the catalog and fails when the committed file differs. The README links to it instead of carrying its own table.
- **A JSON Schema** for `devices/*.json`, so editors validate entries as they are written and CI rejects a bad one with a readable message.

## 3D: diagnostics that say why

- **Driver open trace.** Each device keeps the last open attempt as a short list of steps: which transport was asked for and whether it opened, which probes were answered, which features were built or dropped and why, how long it took, how it ended (opened, nothing, timed out, failed). Drivers already log most of these lines globally; the change routes them to the device. Shown in the Diagnostics tab and included in the report.
- **Session state.** Open, closed (and when), rebuilt (how many times) per device.
- **Catalog load errors** in the report (they are collected today and shown nowhere obvious).
- **Export to a file,** next to Copy report.
- **Redaction, on by default.** Bluetooth addresses shortened to the vendor half, the computer name and user-profile paths removed from log lines, and the paired-phone store never read. A "include full addresses" switch for when a developer asks for them. Pairing keys can't appear: they never reach the log.

## 3E: ready for outside drivers

The safe contribution path is the one that exists: a driver is a project in this repository, reviewed and compiled in. This phase makes that path clear and checkable.

- **Conformance kit.** An abstract test class every driver's tests inherit, fed a fake channel and the driver's recorded frames. It checks the contract each driver must keep: never throws except on cancellation; honours cancellation; a closed channel fails commands at once; dispose is safe twice; open after close works; nothing is sent when writes are off. The reconnect tests from Phase 3A become its first members.
- **Capture files.** Recorded frames move from C# constants to text files under `tests/captures/<vendor>/<model>/`, one frame per line with direction and a comment, so a contributor can add evidence without writing C#.
- **Templates.** A "device report" issue form (model, firmware, the exported report) and a pull-request checklist that mirrors CONTRIBUTING's evidence rules.
- **CI.** Schema validation for catalog files, the conformance kit, the provenance check that already exists, and the generated compatibility table.
- **A driver author's guide:** one page, from "my earbuds aren't supported" to a merged driver.

**Not proposed: loading drivers at runtime.** A plugin would run unreviewed code with Bluetooth access inside the app, the Microsoft Store build couldn't ship it, and it would freeze `Samaat.Core`'s interfaces as a public API before they have settled. The trigger to revisit is concrete: several outside authors maintaining drivers who are slowed down by the review queue. Until then a JSON entry covers new models of a known protocol, and a pull request covers new protocols.

## Order and dependencies

1. **3D first, with 3C's status field.** Both are small, need no hardware, and make every later hardware session more useful.
2. **3B-2 next.** It is protected by the recorded traces.
3. **3B-1 after a hardware pass is possible,** because it moves the verified path.
4. **3E last.** The conformance kit is written against the session core from 3B-2.

Each phase is its own branch and pull request, with its own plan review and implementation review.

## Decisions for the owner

1. **Lifecycle extraction (3B-1):** go ahead knowing it needs a hardware pass afterwards, or hold it until the reconnect fix from 2026-11-10 has been checked on the CMF Buds?
2. **Catalog schema (3C):** accept reading both old and new field names for one release, then dropping the old ones?
3. **Support status (3C):** are `verified`, `tested`, `ported` and `recognised` the right four, and the right words to show users?
4. **Redaction (3D):** on by default with a switch, as proposed?
5. **Session core (3B-2):** now, protected by replay tests, or only after real captures exist for Sony, Soundcore and Samsung?

## What was inspected

`Samaat.Core/Drivers` (registry, catalog, signals, retry), `Samaat.Core/Profiles`, `Samaat.Core/Devices`, `Samaat.Transport`, `Samaat.Drivers`, the Nothing and Sony drivers in full and the four RFCOMM sessions' public shape, `WindowsDeviceSource`, `devices/README.md` and three catalog entries, `DeviceInfoViewModel.BuildReport`, and the driver and shipped-device tests. Not inspected: samaat-mobile's Android device source, so "Android repeats the lifecycle" is an inference from its layering, not a reading of its code.

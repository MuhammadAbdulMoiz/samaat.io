# Possible new features

A menu of what Samaat could do next, grounded in what the code already supports. Each idea says where it would live in the [architecture](ARCHITECTURE.md), what it depends on, and roughly how big it is. Nothing here is committed; the roadmap and open decisions are in [PLAN.md](PLAN.md) and [STATUS.md](STATUS.md).

**Source** marks where an idea comes from: **Roadmap** (already in PLAN or STATUS), **Half-built** (Core defines the feature or contract but no driver or UI uses it yet), or **New**.
**Size** is a rough guide: S (days), M (one to two weeks), L (several weeks or needs hardware research).

## At a glance

| # | Feature | Source | Size | Needs hardware research |
|---|---|---|---|---|
| 1 | Reconnect handling | Roadmap | M | Yes |
| 2 | Adaptive ANC, ANC level, wind and conversation awareness on real earbuds | Half-built | M per vendor | Yes |
| 3 | Custom EQ bands (device EQ) | Roadmap | M | Yes |
| 4 | Battery history and time-left estimate | New | M | No |
| 5 | Global hotkeys and tray quick actions | New | S | No |
| 6 | Scenes: switch noise mode automatically | New | M | No |
| 7 | Find my earbuds | Roadmap | M | Yes |
| 8 | Wear detection and auto-pause | Roadmap | M | Yes |
| 9 | Gesture remapping (Controls page) | Roadmap | L | Yes |
| 10 | Samaat EQ for this PC's audio | Roadmap | L | No |
| 11 | Lab view: SDP/GATT explorer and trace recorder | Roadmap | M | No |
| 12 | "Add my earbuds" draft from Copy report | New | S | No |
| 13 | Codec and link details | New | M | Partly |
| 14 | CLI JSON output and a local status API | New | S | No |
| 15 | Localisation | New | M | No |
| 16 | Linux (BlueZ) platform | Roadmap | L | No |
| 17 | Signed catalog updates without an app update | New | M | No |
| 18 | Fit test | Roadmap | L | Yes, plus a regulatory review |

Suggested order: **1, 4, 5 and 12** first. They make every existing device better and need no new protocol work. Then **2 and 3** for the drivers that already exist, then **11**, which makes every later vendor faster to add.

---

## Make what exists work better

### 1. Reconnect handling
**Source:** Roadmap (STATUS, next step 5). **Size:** M.
When earbuds drop and come back, the vendor session isn't re-established, so per-bud battery and controls can go stale until the next Refresh. Each driver gets a re-handshake path. The device source notices the reconnect (`IEarbudDevice.ConnectionChanged`) and reopens the driver within the existing `DriverRetry` budget.
**Lives in:** `Samaat.Core/Drivers` (a reopen contract), each `Samaat.Protocols.*` driver, `Samaat.Platform.Windows/WindowsDeviceSource`.
**Done when:** a case-lid test under `samaat-cli --watch` shows per-bud battery coming back without a Refresh, on each driver with hardware notes.

### 2. The rest of noise control on real earbuds
**Source:** Half-built. **Size:** M per vendor.
`FeatureId` already defines `noise.adaptive`, `noise.ancLevel`, `noise.ambientLevel`, `noise.wind` and `noise.conversation`, and the Sound page already renders them for the simulator. No real driver exposes them yet. The Bose driver only reads the current mode today; switching modes would follow the write commands described in the external notes that [bose-bmap.md](protocols/bose-bmap.md) cites, confirmed on the headset first.
**Lives in:** the vendor protocol projects only. The capability model and UI need no changes.
**Depends on:** captures from each model ([CONTRIBUTING.md](../CONTRIBUTING.md#protocol-work-and-licences)).

### 3. Custom EQ bands
**Source:** Roadmap (STATUS step 3; PLAN §3, v0.2). **Size:** M.
Presets work on the ParadoX-E. Custom bands would add an `IEqualizerFeature` extension with the band count and range from the device, sliders sent through `LatestValueSender`, and the same confirm-and-roll-back path as every write. Labelled "Device EQ" (stored on the earbuds, applies everywhere), never mixed up with Samaat EQ (10).
**Lives in:** `Samaat.Core/Equalizer`, `Samaat.Protocols.Jieli`, `Features/Sound/EqualizerViewModel`.

### 4. Battery history and a time-left estimate
**Source:** New. **Size:** M.
Record each bud's level over time and show a small sparkline on the Overview, plus "about 2 h left" once there's enough data from the current listening session. Later it could warn about a bud that drains much faster than the other, an early sign of a worn battery.
**Lives in:** `Samaat.Core/Battery` (a pure, testable estimator fed by `IBatteryFeature.Changed`), a small local store next to `settings.json`, and an Overview tile.
**Honesty rules:** show nothing until the estimate is meaningful, label it as an estimate, and never estimate from the single combined Windows level as if it were per bud.

### 5. Global hotkeys and tray quick actions
**Source:** New. **Size:** S.
A configurable shortcut to cycle noise modes (off, noise cancelling, transparency) and one to connect or disconnect, with a short toast confirming what the earbuds actually did. The tray menu gets the same actions.
**Lives in:** `Samaat.App/Tray`, a hotkey contract in `Samaat.Core/Platform` with the Windows implementation in `Samaat.Platform.Windows`, and Settings rows. It reuses `NoiseControlViewModel`'s command path, so a refused or timed-out change is reported, not assumed.

### 6. Scenes: switch noise mode automatically
**Source:** New. **Size:** M.
Simple rules such as "noise cancelling when Teams or Zoom starts a call", "transparency when I lock the PC" or "off below 15 % battery". Each rule shows in Settings with what it will do and the last time it ran.
**Lives in:** a rules engine in Core (pure and table-tested), with triggers from platform contracts (process start, session lock, battery) implemented per OS.
**Watch out:** never fight the user. A manual change pauses the rule until its trigger fires again.

---

## New modules

### 7. Find my earbuds
**Source:** Roadmap (PLAN §3, Later; STATUS "Later modules"). **Size:** M.
Play a tone on one bud or both, with an explicit warning to take the earbuds out first. Only where the vendor protocol supports it. Google's Fast Pair specification includes a ring action, so Fast Pair earbuds are the natural first target to test.
**Lives in:** a new `IFindFeature` in Core, plus the Fast Pair driver.

### 8. Wear detection and auto-pause
**Source:** Roadmap (PLAN §3, v0.2). **Size:** M.
Show which buds are in the ear, and offer "pause when I take an earbud out" where the earbuds report it. Pausing uses the system media session.
**Lives in:** an `IWearFeature` in Core, the vendor drivers, and a Windows media-session contract.

### 9. Gesture remapping (Controls page)
**Source:** Roadmap. The Controls page is already planned as "Soon". **Size:** L.
Remap tap and hold actions per bud. This needs the vendor protocol: standard AVRCP only reports the result of a gesture (PLAN §8), and the UI must say so on earbuds without it.
**Lives in:** an `IGestureFeature` in Core, the drivers, and a new `Features/Controls` page.

### 10. Samaat EQ for this PC's audio
**Source:** Roadmap (PLAN §3, Later). **Size:** L.
A system-wide equaliser for whatever Windows plays, for earbuds with no device EQ. The audio-sharing pipeline (`Samaat.Core/Audio`, WASAPI) already handles PCM, so a filter stage is feasible there; a true system-wide effect would need an audio processing object, which is a much bigger step.
**Rule:** always labelled "Samaat EQ, this PC only", separate from Device EQ.

---

## For contributors and power users

### 11. Lab view: SDP/GATT explorer and trace recorder
**Source:** Roadmap (PLAN §3, "Diagnostics / Lab view"; open decision §13 Q5). **Size:** M.
List a device's SDP records and GATT services, open a channel, and record raw frames as the hex constants the protocol tests replay (see CONTRIBUTING, "Adding or changing device support"). This turns "add a new vendor" from a day of Wireshark into an in-app workflow.
**Lives in:** `Features/Info` (Diagnostics) behind an opt-in Settings switch, using the existing `IGattClient` and `IRfcommChannel`.
**Decision needed:** ship it to end users as opt-in, or keep it to developer builds (PLAN §13 Q5).

### 12. "Add my earbuds" draft from Copy report
**Source:** New. **Size:** S.
For an unrecognised device, Diagnostics offers "Create a catalog entry". It fills a `devices/<vendor>/<model>.json` draft from the observed signals (name pattern, VID/PID, SDP UUIDs) with `"verified": false`, ready to attach to an issue.
**Lives in:** `Samaat.Core/Drivers` (a pure signals-to-entry function, tested against `DeviceCatalog.Parse` so the draft always loads) and a Diagnostics button.

### 13. Codec and link details
**Source:** New. **Size:** M. **Research:** partly.
Show the active audio codec (SBC, AAC, aptX, LDAC) and, where available, link quality. Windows exposes little of this publicly, so research what's readable first. Anything unknown shows as unknown, in line with the capability rules.
**Lives in:** `Samaat.Platform.Windows`, surfaced in `Features/Details`.

### 14. CLI JSON output and a local status API
**Source:** New. **Size:** S.
`samaat-cli --json` prints devices, battery and capabilities as JSON for scripts and status bars (Rainmeter, a Stream Deck plugin). A later step could be an opt-in, localhost-only endpoint for the same data.
**Lives in:** `Samaat.Cli`. Core models are records, so serialising them is straightforward.

---

## Reach

### 15. Localisation
**Source:** New. **Size:** M.
All UI text is English and inline. Move strings to resource files, starting with Urdu and Arabic. Both need right-to-left layout checks, which the headless screenshot tests can render.
**Lives in:** `Samaat.App` resources, plus `CommandResult.UserMessage` and the capability wording in `Shared/CapabilityText.cs`.

### 16. Linux (BlueZ) platform
**Source:** Roadmap (STATUS "Later modules"). **Size:** L.
Core, the drivers, the UI and the simulator already build and run on Linux. A `Samaat.Platform.Linux` project would implement `IDeviceSource`, `IGattClient` and `IRfcommChannel` over BlueZ D-Bus.
**Fits the architecture as is:** add the project, give it the same allowed references as `Samaat.Platform.Windows` in `ArchitectureTests`, and wire it in `AppOptions`.

### 17. Signed catalog updates without an app update
**Source:** New. **Size:** M.
New models are data (`devices/*.json`), so a signed catalog bundle could be fetched on request (never silently) to recognise new earbuds between releases. Drivers stay compiled in; only catalog data updates.
**Watch out:** this would be the app's first network call outside the phone link. It needs a signature check, a visible setting, and a privacy note.

### 18. Fit test
**Source:** Roadmap (PLAN §3, Later). **Size:** L.
An eartip fit test where the earbuds support it. PLAN flags that hearing-health claims need a regulatory review first, so the wording would describe a fit check only.

---

## Out of scope for now

- **Firmware updates:** only through a vendor-sanctioned path (PLAN §3).
- **Bypassing pairing, encryption or authentication** on any earbuds (CONTRIBUTING).
- **Cloud accounts or sync:** the no-account, no-server design is a feature.

# Samaat: status, remaining work and getting started

Last updated: 2026-10-31

## TL;DR

- **Done:** project foundation, the capability engine, and the first three modules (battery, noise control, multipoint), each with a UI and tests. All of it runs today against **simulated devices**.
- **Not done:** controlling any **real** earbuds. Real Bluetooth on Windows reads paired devices and battery (written but **not yet run on hardware**). ANC and multipoint for real devices need the first **vendor adapter**, which needs a decision on which earbuds to target.
- **Tests:** 123 automated tests, green on Linux, and CI runs them on Linux and Windows. The UI tests render dark and light screenshots of every page.

| Overview | Sound | Connections |
|---|---|---|
| ![](images/buds-pro.png) | ![](images/buds-pro-sound.png) | ![](images/buds-pro-connections.png) |

---

## 1. What's done

### Foundation
- .NET 10 solution, central package versions, warnings as errors, CI on Ubuntu and Windows (`.github/workflows/ci.yml`).
- **Capability model**: every feature is described as *device support × Samaat support × available now × verified*, so every disabled control can say why. "Unknown" is never shown as "unsupported". (`Samaat.Core/Capabilities`)
- **Device registry**: merges devices from every source (Windows Bluetooth, simulator). (`Samaat.Core/Devices`)
- **Command layer**: every write returns a `CommandResult` (confirmed / rejected / unsupported / timed out / failed). `CommandRunner` adds a 3 s timeout, and `LatestValueSender` coalesces slider drags. (`Samaat.Core/Commands`)
- **Simulator**: three devices with different shapes (Buds Pro has everything, Buds Lite is basic, the Headset is minimal), realistic command latency, and injectable failures. (`Samaat.Simulator`)
- **UI**: Avalonia desktop app with a nav rail, a device switcher, dark and light themes, and a design system (`docs/design/ui-audit.md`).

### Module 1: Battery ([details](modules/battery.md))
Left / right / case or a single level, charging state, where each bud is, data age and staleness, low and critical alerts with hysteresis. Sources: simulator, the BLE Battery Service, and the level Windows itself reports.

### Module 2: Noise control ([details](modules/noise-control.md))
Noise cancelling, adaptive, transparency and off. ANC strength and ambient level sliders, wind reduction, conversation awareness. The UI layout follows from what the device reports. Changes are optimistic, confirmed by the device, and rolled back on failure.

### Module 3: Multipoint and connected devices ([details](modules/connections.md))
Host list with states, switch / connect / disconnect, a multipoint switch, and a connection limit. Without device support, it honestly shows only this computer and explains why.

### Tools
- `samaat-cli`: prints what Samaat detects for each device, with `--watch` for live changes. Used for hardware checks.

---

## 2. What's remaining

### Blocking: needs a decision from you
1. **Which earbuds to target first** (PLAN §13 Q1). Every real ANC, multipoint and L/R/case battery feature depends on it.
2. **Windows-first or Android-first** (§13 Q2). The core and the UI are portable; the platform layer isn't.
3. **License / open source** (§13 Q4). This affects which references we can learn from (§11).

### Next engineering steps
| # | Item | Notes |
|---|---|---|
| 1 | **Run on real Windows hardware** | `samaat-cli --watch` checklist in `modules/battery.md`. Confirms device discovery and the Windows battery property. |
| 2 | **RFCOMM transport** | `IRfcommChannel` plus a WinRT implementation (open a vendor service UUID on a paired device). |
| 3 | **First vendor adapter** | Framing and messages for the chosen earbuds, implementing `IBatteryFeature`, `INoiseControlFeature` and (if supported) `IMultipointFeature`. Recorded traces become tests. |
| 4 | **Device profile database** | `devices/<vendor>/<model>.json` matching (PLAN §4.3) to pick adapters automatically. |
| 5 | **System tray and notifications** | Battery at a glance and OS notifications for low battery. |
| 6 | **Reconnect handling** | Re-handshake after the earbuds drop and return. |

### Later modules (on the nav rail as "soon")
- **Controls**: touch gesture remapping (vendor protocol only).
- **Equalizer**: device EQ and Samaat EQ, kept separate.
- **Info**: firmware, codec, hardware details.
- **Settings**: theme, alert thresholds, start with Windows.
- Find my earbuds, wear detection, Android port, Linux (BlueZ).

### Known limitations today
- Nothing has been run on real Bluetooth hardware yet.
- Windows gives a single combined battery level only. L/R/case needs a vendor adapter.
- This computer can't be connected or disconnected from Samaat (no public WinRT API for classic audio).
- A device that starts reporting battery after launch appears on the next **Refresh**.

---

## 3. How to start the app

### Prerequisites
- **.NET 10 SDK**: <https://dotnet.microsoft.com/download> (on Ubuntu 24.04: `sudo apt install dotnet-sdk-10.0`)
- Windows 10 2004+ or Windows 11 for real Bluetooth. Any OS works for the simulator.

### Get the code
```bash
git clone https://github.com/MuhammadAbdulMoiz/samaat.io.git
cd samaat.io
```

### Run the desktop app
```bash
# Any OS: simulated devices
dotnet run --project src/Samaat.App -f net10.0

# Windows: your real paired earbuds
dotnet run --project src/Samaat.App -f net10.0-windows10.0.19041.0

# Windows: real devices plus the simulated ones
dotnet run --project src/Samaat.App -f net10.0-windows10.0.19041.0 -- --simulate
```
Options: `--simulate` adds simulated devices; `--all-devices` also lists paired non-audio devices.

In the simulator, try:
- **Overview**: batteries drain and recharge live, and the right bud of *Buds Lite* triggers a low-battery alert.
- **Sound** on *Buds Pro*: switch modes, then drag the strength slider. On *Buds Lite* there's a single switch. On *Headset* the card explains why it's unavailable.
- **Links** on *Buds Pro*: switch audio to the phone, connect the laptop (watch the phone drop to standby), turn multipoint off.

### Run the CLI
```bash
dotnet run --project src/Samaat.Cli -f net10.0 -- --simulate --watch                    # any OS
dotnet run --project src/Samaat.Cli -f net10.0-windows10.0.19041.0 -- --watch           # Windows, real devices
```

### Build a standalone Windows executable
```bash
dotnet publish src/Samaat.App -f net10.0-windows10.0.19041.0 -c Release -r win-x64 --self-contained
# output: src/Samaat.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/Samaat.exe
```

---

## 4. Working on Samaat (TDD)

We work test-first: **red → green → refactor**.

1. **Red:** write the test for the behaviour in the matching test project and watch it fail (a compile error counts).
2. **Green:** write the smallest code that passes.
3. **Refactor** with the tests green. Run everything before committing.

```bash
dotnet test Samaat.slnx                                   # all tests
dotnet test tests/Samaat.Core.Tests                       # one project
dotnet test Samaat.slnx --filter "FullyQualifiedName~NoiseControl"
SAMAAT_SCREENSHOTS=artifacts/screenshots dotnet test tests/Samaat.App.Tests   # also save UI screenshots
```

| Test project | What goes there |
|---|---|
| `Samaat.Core.Tests` | Capability rules, models, commands, simulator behaviour. Pure and fast; uses `FakeTimeProvider` instead of real time. |
| `Samaat.Protocols.Tests` | Byte parsing and protocol features against fake transports. Future vendor adapters add recorded traces here. |
| `Samaat.App.Tests` | View models (with a synchronous dispatcher) and headless UI tests that render the real window. |

Conventions:
- New device capabilities go in as a feature interface in Core first (`IXxxFeature`), then the simulator, then the UI. That keeps a vendor adapter "just another implementation".
- Every write is a `CommandResult`, so the UI must handle refused and timed-out commands.
- Never hide an unsupported feature. Give it a reason.

## Repository map

```
src/
  Samaat.Core/                 capabilities, battery, noise control, connections, commands, device registry
  Samaat.Transport/            byte-level transport abstractions (GATT now; RFCOMM next)
  Samaat.Protocols.Standard/   BLE Battery Service
  Samaat.Platform.Windows/     WinRT discovery, Windows-reported battery, GATT client
  Samaat.Simulator/            simulated devices for development and tests
  Samaat.App/                  Avalonia desktop app (Views/, ViewModels/, Controls/, Assets/)
  Samaat.Cli/                  hardware check tool
tests/                         Core, Protocols and App test projects
docs/                          PLAN, STATUS (this file), design audit, per-module docs, screenshots
```

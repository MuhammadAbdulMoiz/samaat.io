# Plan: Settings, Info, device profile DB, tray + notifications

Branch `feature/remaining-modules` (from `test/hardware-quantum-2026-11-02`, so it carries the WindowsDeviceSource fix).
Context: Samaat (.NET 10, Avalonia 11.3, CommunityToolkit.Mvvm, xUnit + Avalonia.Headless). TDD is the convention (docs/STATUS.md §4).
Warnings are errors. Core is portable (net10.0); Windows-only code sits behind `SAMAAT_WINDOWS` or in `Samaat.Platform.Windows`.
Real test device: Zero Lifestyle Quantum Zbuds — Basic tier, one combined battery level, VID 0x05D6 (Jieli) / PID 0x000A, no vendor protocol (docs/hardware-tests/2026-11-02-quantum.md).

Out of scope: vendor adapters, Controls/EQ/find/wear (need a vendor protocol), reconnect handshakes, Android/Linux.

## 1. Settings

Core (`Samaat.Core/Settings/`):
- `AppSettings` record: `Theme` (System|Light|Dark), `LowThreshold`=20, `CriticalThreshold`=10, `IncludeCase`=false, `SystemNotifications`=true, `CloseToTray`=true, `StartWithWindows`=false. `Normalize()`: clamp low 5–50, critical 1–(low−1).
- `SettingsStore(string path)`: `Load()` returns defaults when the file is missing or unreadable (corrupt file is moved to `settings.json.bad`, overwriting an older `.bad`, never silently overwritten); `Save()` writes a temp file then `File.Move(overwrite: true)`. **No method throws**: every IO/JSON/rename/save failure is caught; `Load` returns defaults, `Save` returns false and keeps the in-memory settings; the last failure is exposed as `LastError` (shown as a one-line note on the Settings page). Tests: missing, corrupt, `.bad` already exists, unreadable path (directory in place of file), save to an unwritable location. System.Text.Json, camelCase, enums as strings. Default path `%APPDATA%/Samaat/settings.json` (`Environment.SpecialFolder.ApplicationData`, works cross-platform).
- `LowBatteryDetector.Policy` becomes settable so a threshold change applies without rebuilding device view models (existing alert state is kept).

App:
- `SettingsViewModel`: observable properties bound to the page; every change normalizes, saves, and applies (theme → `Application.RequestedThemeVariant`; thresholds/IncludeCase → detector policy; StartWithWindows → `IStartupRegistration`).
- `IStartupRegistration` { `IsSupported`, `IsEnabled`, `SetEnabled(bool)` }: Windows impl writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value `Samaat` = `"<Environment.ProcessPath>" --minimized`; elsewhere a no-op with `IsSupported=false` and the toggle disabled with a reason. `--minimized` added to `AppOptions` (start hidden in tray).
- `SettingsPage.axaml` (Appearance / Battery alerts / Background); Settings nav button enabled → `AppSection.Settings`.

## 2. Info

- `DeviceIdentity` gains optional `DeviceDetails? Details = null` (positional, last → source-compatible). `DeviceDetails` record: `Address` (string, "F2:28:99:5A:7E:79"), `VendorId`/`ProductId` (int?), `Services` (IReadOnlyList<string>, e.g. "A2DP", "AVRCP", "HFP", "SPP", "Vendor (fe010000-…)").
- Core `BluetoothVendors.Name(int vid)`: small table of Bluetooth SIG company IDs seen on earbuds (Apple, Samsung, Sony, Jieli, Qualcomm, Bestechnic, Actions, Realtek, Airoha…). shortcut comment: partial list.
- Windows: fill `Details` in `WindowsDeviceSource.CreateDeviceAsync` — address from `BluetoothDevice.BluetoothAddress`; services from `BluetoothDevice.SdpRecords` (minimal SDP data-element parser in Core, `SdpRecord.Parse(byte[])` → service class UUIDs + name; pure and unit-tested with the Quantum records captured on 2026-11-02); VID/PID from `System.Devices.HardwareIds` of the container's PnP nodes (`VID&0001xxxx_PID&xxxx`), pure regex helper unit-tested.
- Simulator devices get plausible `Details`.
- `InfoViewModel` (per device; rows: name, identified model, maker from VID, VID/PID, address, services, support tier with explanation, battery source, connection) + `InfoPage.axaml`; Info nav button enabled → `AppSection.Info`. Unknown values show "Not reported", never blank.

## 3. Device profile DB (PLAN §4.3, minimal)

- `devices/<vendor>/<model>.json`, copied to output (`<Content … CopyToOutputDirectory>` in App and Cli). Schema exactly as PLAN §4.3: `id`, `displayName`, `match: { deviceId: { vid: "0x05D6", pid: "0x000A" }, sdpUuids: [], namePattern }`, `adapter`, `features`, `verifiedFirmware`; plus one optional extra field `notes` (string). First file: `devices/zero-lifestyle/quantum-zbuds.json` (vid 0x05D6, pid 0x000A, namePattern `^Quantum$`, adapter null, features `{"battery.combined": {}}`, verifiedFirmware [], notes linking the hardware test).
- Core `DeviceProfile` record mirroring that schema (features as raw `JsonElement`) and `DeviceProfileDb`: `Load(directory)` (bad JSON, missing id/displayName, unparsable vid/pid hex, or an invalid `namePattern` regex → that file is skipped and reported in `Errors`; regexes compiled at load; never throws), `Match(DeviceIdentity)`: all specified match fields must agree; most specific profile wins (count of matched fields); null when none. Generic Jieli VID/PID alone must not match the Quantum profile (name required) — test with the buds-sniffer case (same VID/PID, other name).
- Used by: Info page ("Identified as …") and Overview subtitle. Adapter selection is not wired (no adapters exist); `adapter` is read and shown on Info as "Vendor adapter: none yet".

## 4. Tray + notifications

- Avalonia `TrayIcon` set up in `App.OnFrameworkInitializationCompleted` (code, not XAML, so tests don't create it). Icon generated at runtime (accent rounded square + "S" via `RenderTargetBitmap` → `WindowIcon`) to avoid shipping a binary asset; also used as the window icon.
- Tooltip: pure `TraySummary.Text(devices)` → e.g. "Samaat — Quantum 90%" / "Samaat — no devices"; ≤127 chars (Windows tooltip limit). Menu: one disabled line per device ("Quantum · 90% · Connected"), Open Samaat, Refresh, Quit. Rebuilt on device/battery change.
- `CloseToTray`: `ShutdownMode = OnExplicitShutdown`. `MainWindow.Closing`: if quitting → allow; else if `CloseToTray` → cancel + `Hide()`; else → allow and call `desktop.Shutdown()` explicitly (so CloseToTray off still exits and disposes as today). The decision is a pure method `CloseAction Decide(bool quitting, bool closeToTray)` with a test. Quit menu item sets quitting and calls `Shutdown()`.
- `INotifier.Show(title, body)`; `MainWindowViewModel.OnAlert` calls it when `SystemNotifications` is on (in-app toasts stay). Windows impl in Platform.Windows: registers AUMID `Samaat.App` under `HKCU\Software\Classes\AppUserModelId\Samaat.App` (DisplayName, IconUri) once — the same registration the Windows Community Toolkit's `ToastNotificationManagerCompat` performs for unpackaged apps — then `ToastNotificationManager.CreateToastNotifier("Samaat.App").Show(...)`; any exception is swallowed and logged (a notification failure must never crash the app). Non-Windows: no-op. Verified manually on this PC with a test notification (`--test-notification` flag); a clean-machine check is not possible here and is listed as a known gap.

## Composition (Windows isolation)
- Interfaces `IStartupRegistration` and `INotifier` live in `Samaat.Core/Platform/` (portable; both App and Platform.Windows already reference Core, so no reference cycle), with no-op implementations there.
- Toast delivery is documented as **verified on this PC only**; STATUS.md lists the clean-machine check as open, not complete.
- Windows implementations (`WindowsStartupRegistration`, `WindowsToastNotifier`) live in `Samaat.Platform.Windows` (already referenced only by the Windows TFM).
- `AppOptions` gets `CreateStartupRegistration()` / `CreateNotifier()` alongside the existing `CreateSources()`, choosing the Windows types under `#if SAMAAT_WINDOWS` and the no-ops otherwise. `MainWindowViewModel` takes them as optional constructor parameters (tests pass fakes).

## Tests (written first)
- Core: SettingsStore round-trip, missing file, corrupt file (renamed, defaults returned), Normalize edges; detector policy change affects next Evaluate; SdpRecord.Parse on the captured Quantum records; HardwareId VID/PID regex; BluetoothVendors; DeviceProfileDb load (good, bad JSON skipped), Match (Quantum matches, same VID/PID other name doesn't, specificity).
- App (headless): SettingsViewModel persists and applies thresholds/theme (temp dir store, fake startup registration); InfoViewModel rows for a simulated device and for "Not reported"; navigation to Info and Settings renders without binding errors (extend existing MainWindowTests); OnAlert calls fake INotifier only when enabled; TraySummary text cases.
- Not unit-tested (thin WinRT/registry shims): Run-key writer, toast notifier, PnP HardwareIds query, TrayIcon wiring. Checked by running the app on this PC.

## Order
1. Settings + Info + profile DB (one implementer pass; they share MainWindow/nav wiring).
2. Tray + notifications + start-with-Windows (needs Settings).
3. Docs: STATUS.md (done/remaining), module notes `docs/modules/settings.md`, `info.md`, `tray.md` short.

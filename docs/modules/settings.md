# Module: Settings

Status: **implemented (v0.3)**. Works for simulated and real devices.

## What it does

- **Appearance**: System, Light or Dark (default Dark, today's look).
- **Battery alerts**: low threshold (5–50%, default 20) and critical threshold (below low, default 10), and whether a low case raises an alert (off by default). Changes apply immediately to the shared `LowBatteryDetector`; alerts already raised stay raised.
- **Background**: system notifications on/off, keep running in the tray when the window is closed, start with Windows (writes `HKCU\…\CurrentVersion\Run\Samaat` = `"<exe>" --minimized`; unavailable under `dotnet run` and on other OSes, with a reason).
- Saved to `%APPDATA%/Samaat/settings.json` on every change. A missing or corrupt file falls back to defaults (the corrupt file is kept as `settings.json.bad`); a failed save keeps the settings in memory and shows a one-line note. Nothing here throws.

## Code

| Piece | Where |
|---|---|
| `AppSettings`, `SettingsStore` | `src/Samaat.Core/Settings/` |
| `IStartupRegistration` (+ no-op) | `src/Samaat.Core/Platform/` |
| Windows Run-key writer | `src/Samaat.Platform.Windows/WindowsStartupRegistration.cs` |
| View model / page | `src/Samaat.App/Features/Settings/SettingsViewModel.cs`, `SettingsPage.axaml` |

## Tests

`SettingsTests.cs` (Core: normalize, round-trip, missing/corrupt/unwritable files) and `SettingsViewModelTests.cs` (App). The Run-key writer is a thin registry shim and is not unit-tested; toggling it on a real machine is not yet checked.

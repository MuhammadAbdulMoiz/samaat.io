# Module: Tray and notifications

Status: **implemented (v0.3)**. Notifications are **verified on one PC only**; a clean-machine check is still open.

## What it does

- **Tray icon** (runtime-drawn "S", also the window icon). Tooltip shows each device's battery, e.g. "Samaat — Quantum 80%" (≤127 characters). Menu: one line per device, Open Samaat, Refresh devices, Quit. Clicking the icon opens the window.
- **Close to tray** (Settings › Keep running in the tray, on by default): closing the window hides it; Quit in the tray menu exits. With the setting off, closing exits. If the tray icon can't be created, closing always exits.
- `--minimized` starts with only the tray icon (used by Start with Windows).
- **System notifications** for low/critical battery alerts (Settings › System notifications, on by default), in addition to the in-app toasts. On Windows Samaat registers itself as `Samaat.App` under `HKCU\Software\Classes\AppUserModelId` and shows a toast; any failure is logged and ignored. Elsewhere it is a no-op.
- `--test-notification` shows one notification after startup, for checking a machine by hand.

## Code

| Piece | Where |
|---|---|
| `INotifier` (+ no-op) | `src/Samaat.Core/Platform/INotifier.cs` |
| Windows toast | `src/Samaat.Platform.Windows/WindowsToastNotifier.cs` |
| Tray, icon, tooltip text, close decision | `src/Samaat.App/Tray/` |
| Wiring | `src/Samaat.App/App.axaml.cs` |

## Tests

`TrayTests.cs` (tooltip text, menu lines, close decision) and `NotifierTests.cs` (alerts reach the notifier only when enabled). The toast, the tray icon and hiding/restoring the window are checked by hand, not by tests.

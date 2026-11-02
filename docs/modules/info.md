# Module: Info and device profiles

Status: **implemented (v0.3)**. Checked on real hardware (Zero Lifestyle Quantum Zbuds, 2026-11-02) and the simulator.

## What it does

- The **Info** page lists what Samaat knows about the selected device: name, identified model, vendor (from the Bluetooth vendor ID), vendor/product ID, Bluetooth address, advertised services (A2DP, AVRCP, HFP, SPP, vendor UUIDs…), support tier, battery source, connection, and whether a vendor adapter exists. Anything unknown says "Not reported".
- On Windows the details come from the device's SDP records (services), its PnP hardware IDs (`VID&0001xxxx_PID&xxxx`) and `BluetoothAddress`. Each lookup is best-effort; a failure leaves that field unreported.
- **Device profile DB** (PLAN §4.3): `devices/<vendor>/<model>.json`, copied next to the app and CLI and loaded at startup. A profile matches when every field it specifies (`match.deviceId.vid/pid`, `namePattern`, `sdpUuids`) agrees; the most specific match wins. Bad files are skipped and reported, never fatal. Profiles name the model today; `adapter` is read but no adapters exist yet.
- `samaat-cli` prints the same details and the identified profile.

## Code

| Piece | Where |
|---|---|
| `DeviceDetails` | `src/Samaat.Core/Devices/Devices.cs` |
| SDP parser, service labels, hardware-ID parser, vendor names | `src/Samaat.Core/Bluetooth/` |
| `DeviceProfile`, `DeviceProfileDb` | `src/Samaat.Core/Profiles/` |
| Windows lookups | `src/Samaat.Platform.Windows/WindowsDeviceSource.cs` |
| Profiles | `devices/` |
| View model / page | `src/Samaat.App/Features/Details/BluetoothDetailsViewModel.cs`, `BluetoothDetailsPage.axaml` (the "Details" nav entry; master's Info page is the separate diagnostics page) |

## Tests

`BluetoothInfoTests.cs` and `ProfileTests.cs` (Core; the SDP tests use the Quantum's records, hand-encoded from docs/hardware-tests/2026-11-02-quantum.md), `BluetoothDetailsViewModelTests.cs` (App).

Limit: the vendor table holds Bluetooth SIG company IDs only, so a USB-IF vendor ID (e.g. Microsoft's `0x045E` headset) shows as an unknown vendor.

# Plan: Fast Pair Message Stream driver — realme Buds T200 Lite (battery + firmware)

Branch `feature/ui-redesign`. Evidence: docs/hardware-tests/2026-11-03-realme-buds-t200-lite.md. User asked to "read and fix" the T200 Lite and use their artwork. Read-only: nothing is written to the buds.

## Scope
1. **New project `src/Samaat.Protocols.FastPair`** (net10.0; Core + Transport; in Samaat.slnx; tests in `tests/Samaat.Protocols.Tests`):
   - `MessageStreamParser`: reassembles `group(1) code(1) length(2,BE) data` messages from RFCOMM chunks (split/glued), never throws, caps length (e.g. 1024), resyncs on nonsense by dropping the buffer.
   - `FastPairMessages`: decode group 0x03 Device Information — 0x01 model ID (3 bytes), 0x02 BLE address (6), 0x03 battery (L, R, case; low 7 bits %, 0x7F unknown, bit 7 charging), 0x09 firmware (ASCII). Unknown groups/codes ignored.
   - `FastPairBatteryFeature : IBatteryFeature, IAsyncDisposable`: Components {Left, Right, Case}, `ComponentsAreExhaustive=false`, `Source=RuntimeQuery`, `Verification=Unverified`. Updates on every battery message; holds firmware/model for identity. `RefreshAsync` reopens the channel (the buds push device info on connect). Handles `Closed` by marking readings stale (keep last values; don't crash).
   - `FastPairDriver : IEarbudDriver`, `Id="fast-pair"`, `DisplayName="Google Fast Pair"`: opens RFCOMM `df21fe2c-2515-4fdb-8886-f12c4d67927c` via `context.Transports.OpenRfcommAsync`, returns the feature only after the first valid battery message within the caller's token (WindowsDeviceSource already owns the 8 s driver budget), else disposes and returns `[]`.
   - **Read-only**: the driver never writes to the channel. Test with a fake `IRfcommChannel` that records writes and asserts none.
2. **Firmware in the UI**: if cheap, surface the firmware revision as `DeviceIdentity.FirmwareVersion`-equivalent on the Details page (e.g. a `FirmwareVersion` property on the feature that `BluetoothDetailsViewModel` shows when present). Skip if it needs Core identity changes beyond a small optional read.
3. **Registration**: register `FastPairDriver` in `AppOptions.CreateRegistry()` and the CLI next to `JieliDriver`.
4. **Catalog/profile** `devices/realme/buds-t200-lite.json`, valid for both loaders (follow `devices/audionic/paradox-e.json`): `driver: "fast-pair"`, `adapter: "fast-pair"`, match `namePattern "^realme Buds T200 Lite$"` + vendor/product `0x05D6/0x000A` + `sdpUuids ["df21fe2c-2515-4fdb-8886-f12c4d67927c"]`, features `battery.left/right/case`, `image: "buds-t200-lite.png"`, `images: { left: "buds-t200-lite-left.png", right: "buds-t200-lite-right.png", case: "buds-t200-lite-case.png" }` (already in `devices/realme/`). Must out-rank nothing else: ParadoX-E and Quantum keep resolving to their own entries (test).
5. **Docs**: STATUS.md, docs/modules/battery.md (Fast Pair source row), hardware note "Next".

## Review fixes (Codex plan R1)
- **Firmware is required, not optional**: Core gets a small optional `IDeviceInfoFeature : IFeature` (`string? FirmwareVersion`, `event EventHandler? Changed`). `FastPairBatteryFeature` (or a sibling feature returned by the same driver) implements it from message 0x03/0x09. `BluetoothDetailsViewModel` adds a "Firmware" row: the feature's value, else `DeviceIdentity.FirmwareVersion`, else "Not reported", and updates on `Changed`. Tests for the row (value, fallback, update).
- **Greeting must not be lost**: `IRfcommChannel` contract: chunks received before the first `Received` subscriber are buffered and replayed to that subscriber, in order (bounded, e.g. 4 KB). Implemented in `WinRtRfcommChannel`. Test: a fake channel that emits the whole greeting synchronously inside `OpenRfcommAsync`, before the driver subscribes, still yields the battery feature; plus a unit test of the buffer/replay behaviour on a reusable base or helper.
- **Service UUID must constrain resolution**: the profile JSON gets `"serviceUuid": "df21fe2c-2515-4fdb-8886-f12c4d67927c"` in `match` (what `DeviceCatalog` reads) next to `sdpUuids` (what `DeviceProfileDb` reads). Test: T200 Lite signals without that UUID do not resolve to `fast-pair`.
- **Project references**: add the FastPair project reference to `Samaat.App.csproj`, `Samaat.Cli.csproj` and `Samaat.Protocols.Tests.csproj`.
- **Failure and refresh tests**: an opener that throws and a channel whose read loop throws → `[]` and the channel disposed; `RefreshAsync` disposes the old channel, reopens, receives a new push and updates; a failed refresh keeps the last reading and doesn't throw.

## Review fixes (Codex plan R2)
- **Firmware row threading/lifetime**: `BluetoothDetailsViewModel` posts `IDeviceInfoFeature.Changed` rebuilds to the UI thread (same pattern as `DeviceViewModel`'s battery updates; injectable post for tests) and unsubscribes when `DeviceViewModel.Dispose` runs. Tests: update after an off-thread change; no update after disposal.
- **Ordered replay**: the RFCOMM channel serializes delivery with one lock: a chunk either goes to the pre-subscription queue or straight to subscribers, and the first subscription replays the queue under the same lock before any later chunk is delivered. Test: a background reader pushing numbered chunks while the subscriber attaches sees every chunk exactly once, in order, never concurrently.
- **Refresh cancellation** (optional, adopted): `RefreshAsync` propagates caller cancellation but swallows transport failures, matching `JieliBatteryFeature`.

## Tests (first)
- Parser: the captured 45 bytes (two chunks) → 5 messages; split into many chunks; garbage/oversize; truncated waits.
- Decoder: battery `5A 46 FF` → L 90, R 70, case unknown; charging bit; `7F` unknown; firmware "1.0.0"; model `67B851`.
- Feature/driver with fake RFCOMM: happy path returns one battery feature with L/R; no channel → `[]`; channel that sends nothing → `[]` when the token expires; channel closes → no crash; zero writes in every case.
- Catalog: T200 Lite resolves to `fast-pair`; ParadoX-E → `jieli-rcsp`; Quantum → its entry.

## Verification
Build 0 warnings, all tests, then the real buds: CLI shows `realme Buds T200 Lite [Connected, Full support]` with Left/Right from the device protocol; app shows the three-piece artwork with levels.

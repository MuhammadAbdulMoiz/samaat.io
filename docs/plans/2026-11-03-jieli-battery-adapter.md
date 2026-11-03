# Plan: Jieli RCSP adapter — per-bud battery for the Audionic ParadoX-E

Branch `feature/ui-redesign`. Evidence: docs/hardware-tests/2026-11-03-audionic-paradox-e.md (auth handshake and a `0xC2` AdvInfo push captured on the real buds). User approved "battery first"; noise control is out of scope (write commands unconfirmed). Follows docs/ARCHITECTURE.md "Adding the first vendor".

## Scope
1. **Transport**: `IGattCharacteristic` gains `bool CanWrite` and `Task<bool> WriteAsync(ReadOnlyMemory<byte> data, bool withoutResponse, CancellationToken)`. Implemented in `WinRtGattClient`; existing test fakes updated.
2. **New project `src/Samaat.Protocols.Jieli`** (net10.0, references Core + Transport; added to Samaat.slnx; tests in `tests/Samaat.Protocols.Tests`):
   - `JlAuthCipher`: C# port of the MIT-licensed JieLi auth cipher (buds-sniffer `JlAuthCipher.kt` ← ElectronicCats `auth.py` ← hybridherbst `jl_auth_v3.py`). Attribution header in the file and a new root `THIRD_PARTY_NOTICES.md` with the MIT text and the three sources. KAT test.
   - `RcspFrame`: build commands (`FE DC BA | flags | op | len(2,BE) | sn | params | EF`), and `RcspFrameParser` that reassembles frames from notification chunks (split and glued), skips garbage, never throws on bad input, caps payload at 2048.
   - `RcspAuth`: the 6-step raw handshake (no framing) over AE01/AE02; validates the device's cipher reply; times out (3 s per step) → failure, never hangs.
   - `AdvInfo.TryParse(params)`: from the `0xC2` push (params after sn): VID at [0..1], address [5..10], L/R/case at [13..15] of the params-without-sn as captured (`05D6 01D2 004A 22 305F277C717E 02 64 64 00 62` → L=100, R=100, case unknown). Low 7 bits = %, bit 7 = charging, `0x00` = no data. Must be defined against the real capture in a test.
   - `JieliBatteryFeature : IBatteryFeature, IAsyncDisposable`: Components {Left, Right, Case}, `ComponentsAreExhaustive=false`, `Source=RuntimeQuery` (shown as "Device protocol"), `Verification=Unverified`. Subscribes AE02, runs auth, then updates `Current` and raises `Changed` on each `0xC2`. Ignores device commands that expect a response except it must not crash. `RefreshAsync` re-runs the session (resubscribe + auth), which makes the buds push `0xC2` again. Disposing unsubscribes.
   - `JieliDriver : IEarbudDriver`, `Id = "jieli-rcsp"`, `DisplayName = "Jieli RCSP"`. `OpenAsync` finds service `0000ae00-…`, write `ae01`, notify `ae02` via `context.Transports.Gatt`; returns `[JieliBatteryFeature]` once auth succeeds within ~6 s, or `[]` on any failure (missing GATT/service, auth timeout) so the source falls back to the Windows battery.
   - Only these bytes are ever written: the auth steps. No other opcodes (no GetTargetInfo either, since it returned status 0x02).
3. **Windows**: in `WindowsDeviceSource.CreateDeviceAsync`, when the resolution is a Driver and no paired-BLE `gatt` exists, try `BluetoothLEDevice.FromBluetoothAddressAsync(bluetooth.BluetoothAddress, BluetoothAddressType.Public)` and wrap it in `WinRtGattClient` (dispose with the device). Failures leave gatt null.
4. **Registration**: `AppOptions.CreateRegistry()` registers `JieliDriver`; the CLI uses the same registry (check how it builds its source and register there too). App and CLI reference the new project only where needed.
5. **Catalog/profile**: `devices/audionic/paradox-e.json`, valid for both DeviceCatalog and DeviceProfileDb (follow quantum-zbuds.json): `driver: "jieli-rcsp"`, `adapter: "jieli-rcsp"`, match `namePattern "^ParadoX-E$"` + vendor/product `0x05D6/0x000A`, features `battery.left`, `battery.right`, `battery.case`, `image: "paradox-e.png"` (user-supplied photo, background removed — provided separately).
6. **Docs**: STATUS.md (first vendor adapter, ParadoX-E), docs/modules/battery.md (Jieli source), the hardware note's "Next" updated.

## Review fixes (Codex plan R1)
- **VID/PID in signals**: `WindowsDeviceSource` fills `DeviceSignals.VendorId/ProductId` from the same `System.Devices.HardwareIds` lookup used for `DeviceDetails`, before `_registry.Resolve`. Test: signals built the Windows way (name + VID/PID from hardware IDs, CoD, SDP UUIDs) resolve ParadoX-E to `jieli-rcsp` and still resolve the Quantum to its entry.
- **One total deadline**: `JieliDriver.OpenAsync` creates a linked token with a single 8 s budget covering BLE acquisition (the Windows by-address fallback uses the same token), service and characteristic discovery, subscribe, auth and the first `0xC2`. Every await gets that token. Test: a fake GATT that never answers discovery/subscribe returns `[]` once the budget expires (injectable budget for tests).
- **Feature only with real data**: `OpenAsync` returns the feature only after a valid initial `0xC2` battery payload within the budget; otherwise it disposes it and returns `[]`, so the Windows battery stays.
- **Tier**: `WindowsDeviceSource` sets `SupportTier.Full` only when the driver returned at least one feature; otherwise the tier stays Basic/Standard.
- **Write transcript test**: fake-GATT tests record every `WriteAsync` payload across open, refresh, auth failure and timeout, and assert each one is an auth step (first byte 0x00/0x01 with 16 bytes, or `02 70 61 73 73`) and that no `FE DC BA` frame is ever written.
- **Deadline ownership (Codex plan R2)**: `WindowsDeviceSource` resolves signals first; for a Driver resolution it creates the 8 s linked CTS itself, uses it for the by-address BLE fallback, and passes that token to `OpenAsync`. The driver uses the supplied token and starts no budget of its own (tests pass a short-lived token).
- **No battery without data (Codex plan R2)**: driver test where auth succeeds but the notification is absent, malformed or a non-battery frame → `[]` and the feature disposed when the token expires.
- The `paradox-e.png` artwork is already in `devices/audionic/` and is committed with this work.

## Tests (first)
- Cipher KAT; frame build equals `FEDCBAC002000500FFFFFFFFEF`-style bytes for a sample command; parser: the captured `0xC2` frame split into 3 chunks and glued with the `0x09` frame yields both packets; garbage prefix skipped; truncated frame waits; oversize length skipped.
- AdvInfo from the captured bytes → L=100, R=100, case=null; charging bit → IsCharging; `0x00` → no reading.
- Auth against a fake device (fake GATT that answers like the buds using the cipher): succeeds; wrong cipher reply → fails; silence → fails within timeout (use FakeTimeProvider or a short timeout parameter).
- Feature: after a `0xC2` notification, `Current` has Left/Right readings and `Changed` fired; `RefreshAsync` re-authenticates.
- Driver: no GATT → `[]`; no AE00 → `[]`; happy path → one IBatteryFeature.
- Catalog test: shipped devices folder still loads in both loaders without errors (existing test) and ParadoX-E resolves to the jieli-rcsp driver.

## Verification
Build (0 warnings), all tests, then run the app/CLI with the real ParadoX-E: Full support, Left/Right percentages from the device protocol, and fallback to Windows battery if BLE is unreachable.

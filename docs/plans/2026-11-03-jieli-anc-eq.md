# Plan: noise control and EQ presets for the Audionic ParadoX-E (Jieli RCSP)

Branch `claude/anc-eq-521b37`. Protocol and evidence: [docs/protocols/jieli-rcsp.md](../protocols/jieli-rcsp.md). The read-only probe on the real buds returned the noise mode list (Off, Noise cancelling, Transparency), the current mode, the current EQ preset and 7 presets × 10 bands.

## Scope
1. **One RCSP session per device** (`RcspSession`, new). Moves the subscribe/auth/parse code out of `JieliBatteryFeature`. Adds `SendAsync(op, params)`: one command in flight, `sn` increments, flags `0xC0`, waits up to 2 s for the response with the same op and sn, returns `(status, params)` or null. Raises `Packet` for device commands and `Authenticated` after each (re)auth. `RestartAsync` = today's refresh (resubscribe + auth). Dispose is idempotent; each feature disposes the shared session (they are always disposed together by the device).
2. **Codec** (`SysInfo.cs`): `RcspCommands` builds the only frames ever written after auth: `GetSysInfo(mask)` for masks `0x6000`, `0x2000`, `0x1010`, `0x0010`; `SetNoiseMode(VoiceMode)`; `SetEqPreset(mode, gains)`. Parsers: TLV list after `function`, `VoiceMode` (9 bytes BE), EQ attr `0x04` (fixed 10-band and dynamic layouts), presets attr `0x0C`. Malformed input → null, never throws.
3. **`JieliNoiseControlFeature : INoiseControlFeature`**: built only if `GetSysInfo 0x6000` lists at least one non-off mode. `Options.Modes` = the listed modes it knows (0 Off, 1 NoiseCancelling, 2 Transparency; others ignored), `OptionsAreExhaustive = true`, no levels/wind/conversation (setters return Unsupported without writing). `Source = RuntimeQuery`, `Verification = Unverified`. `SetModeAsync`: validate; send `SetNoiseMode` with the VoiceMode bytes exactly as the buds listed that mode; status ≠ 0 → Rejected; no response → TimedOut; then read `0x2000` back: matches → Confirmed and `Changed`, otherwise Rejected and `Current` = what was read. `0x09` pushes with function `0xFF` attr `0x0D` update `Current`. Re-reads after `Authenticated`.
4. **Core `IEqualizerFeature`** (`src/Samaat.Core/Equalizer/Equalizer.cs`, new; Core has no EQ yet): `Presets` (`EqPreset(int Id, string Name)`), `CurrentPresetId` (int?), `Changed`, `SetPresetAsync(id)`, `Source`, `Verification`. `FeatureId.EqPresets = "eq.presets"`, `EqualizerCapabilities.For` added to `DeviceCapabilities`. Bands are not editable in this change.
5. **`JieliEqualizerFeature`**: built only if `GetSysInfo 0x1010` parses. Presets = the 7 reported modes with SDK names (Standard, Rock, Pop, Classical, Jazz, Country, Custom). `SetPresetAsync(id)`: send `SetEqPreset(id, gains the buds reported for it)`, confirm by reading `0x0010` back. `0x09` attr `0x04` pushes update it.
6. **Driver**: `OpenAsync` starts the session, then waits for the battery push and reads noise and EQ within the same caller-owned budget. Returns whichever features got real data; none → `[]` and the session is disposed.
7. **UI**: `EqualizerViewModel` (same optimistic-then-rollback pattern as `NoiseControlViewModel`, via `CommandRunner`); the Sound page's "Equalizer · Soon" card becomes a preset list styled like the noise-mode rows, or an "Equalizer unavailable" note with the reason when the device has no EQ feature. `DeviceViewModel` owns and disposes it. No changes to `MainWindow.axaml` or profile image fields.
8. **Catalog/docs**: `paradox-e.json` features add `noise.anc`, `noise.transparency`, `eq.presets`; notes no longer say read-only. Hardware note, STATUS, THIRD_PARTY_NOTICES (Apache-2.0 SDK as the source of the protocol facts).

## Tests first
- Codec: captured responses decode (modes, current, EQ, presets); builders produce the exact documented bytes; response frames split into status/sn/params; malformed TLVs return null.
- `FakeBuds` answers GetSysInfo with the captured payloads and SetSysInfo by updating its state (status configurable: OK, error, silent).
- Noise feature: options from the buds; SetMode writes the exact frame and confirms; error status → Rejected; silent → TimedOut and state unchanged; unsupported mode/levels → Unsupported with no write; `0x09` push updates `Current`.
- EQ feature: presets and current; SetPreset writes the exact frame and confirms; rejection; push.
- Driver: features returned with battery, noise and EQ; buds that don't answer sys-info still give battery only.
- **Write allowlist** (replaces the auth-only transcript test, guard kept): across all fake modes and every feature operation, every write is an auth step or one of the allowlisted frames above (ops `0x07` with those masks; `0x08` with attr `0x0D` + a listed VoiceMode, or attr `0x04` + mode 0–6 + 10 gains). Asserts no op in `0xD0`–`0xEF` is ever sent.
- App: `EqualizerViewModel` selects, rolls back on failure, shows unavailable text with no feature.

## Hardware gate
Before any write to the real buds: ask the user, listing the exact bytes. Then verify from Samaat: switch Off/ANC/Transparency and two EQ presets, user confirms they hear it.

## Review fixes (Codex plan R1)
- **Readback semantics**: set acknowledged but readback missing or malformed → `TimedOut`, `Current` keeps the last confirmed state. `Rejected` only for a non-zero status or a valid readback showing a different state (then `Current` = the readback).
- **Session owner**: `JieliBatteryFeature` owns the `RcspSession`: its `DisposeAsync` disposes the session; noise and EQ `DisposeAsync` only detach their handlers. Reconnect stays `WindowsEarbudDevice` → battery `RefreshAsync` → `session.RestartAsync` → `Authenticated` → noise and EQ re-read. So the driver still returns no Jieli features without a valid battery push (rejected part of the finding: returning noise/EQ without battery would leave them with no owner and no reconnect hook; the ParadoX-E pushed `0xC2` right after auth in every session so far).
- **Discovery**: after auth the driver issues the noise and EQ reads at once (serialized by the session) while it waits for the battery push concurrently; each feature is built from its own valid data.
- **More tests**: concurrent ANC and EQ sets; a `0x09` push arriving between a command and its response; disposing the noise feature leaves EQ working; battery refresh re-auths and re-reads noise/EQ; buds that push no battery but answer sys-info → driver returns `[]` and the session is disposed.
- **Simulator**: `SimulatedEqualizer` (presets only), given to the simulated Buds Pro; core tests for it.
- **Only evidenced presets**: presets come only from the parsed `0x0C` list; ids outside 0–6 or layouts that don't parse are neither shown nor writable.
- **Finish**: `dotnet build Samaat.slnx` (warnings are errors) and `dotnet test Samaat.slnx` green, hardware gate results recorded, commit.

## Review fixes (Codex plan R2)
- **Diagnostics**: `DeviceInfoViewModel` groups an "Equalizer" module for `eq.` capabilities and `CapabilityText.Name` labels `eq.presets` ("EQ presets"); tests updated.

## Hardware gate result (2026-11-03)
With the user's OK, a throwaway probe sent exactly: noise cancelling, transparency, off, EQ Rock, EQ Classical (frames in docs/protocols/jieli-rcsp.md). Each got status `0x00`, a `0x09` push with the new attribute, and a matching readback; the user heard every change.

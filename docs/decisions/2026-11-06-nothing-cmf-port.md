# Decision: port Nothing / CMF Buds support from BudsLink

**Date:** 2026-11-06 · **Status:** implemented on branch `feat/nothing-cmf-buds`, **run on real CMF Buds** (firmware 1.0.5.050) · **Plan:** [licensing and porting, phase 3](../plans/2026-11-05-licensing-and-porting.md#3-porting-order-one-brand-per-pr-each-with-a-decision-record)

## Source

| | |
|---|---|
| Project | [BudsLink](https://github.com/maniacx/BudsLink) (credits radiance-project/ear-web) |
| Commit | `0eb4d07dcd7eafabf425d98c607e02fc9519c94b` |
| Licence | GPL-3.0. Samaat is GPL-3.0-or-later; while this code is in, the combined program is distributed under GPL-3.0 (as with [OpenSCQ30](2026-11-06-soundcore-port.md)) |
| Upstream files read | `src/lib/devices/nothingBuds/{nothingBudsSocket.js, nothingBudsConfig.js, deviceConfigs/CMFBuds.js}` |

BudsLink is JavaScript; the logic was rewritten in C#. Only command ids, byte layouts and preset ids are taken over; preset names are Samaat's own wording.

## What was ported

`src/Samaat.Protocols.Nothing` (references Core and Transport only), driver id `nothing-spp`, registered once in `BuiltInDrivers`. Frame, commands and bytes are in [the protocol note](../protocols/nothing.md).

- **Battery:** left, right and case with charging, from `4007` and the `E001` push.
- **Noise control:** off, noise cancelling, transparency; set with `F00F`, confirmed by the ack and a `C01E` read-back.
- **EQ presets:** the seven presets, including Custom; set with `F01D`, confirmed by the ack and a `C050` read-back.
- **Model:** only `0xB168` (CMF Buds) is in the model table; any other model id gives no features.
- **Catalog:** `devices/nothing/cmf-buds.json` matches on Nothing's vendor ID `0x02B0`, the RFCOMM service and the name together, so it wins over `generic.fast-pair` and a look-alike name alone matches nothing.

## Corrections to upstream, from our own captures

1. The noise mode is `payload[4]` of the `401E` reply.
2. `01`/`02`/`03` are noise cancelling levels (Samaat remembers the last one and re-sends it); `04` is ignored by this model.

## Writes are on

Unlike the other ports, the shipped driver writes noise mode and EQ presets (`new NothingDriver()` defaults to `allowWrites: true`). The owner's rule is read-only until we have our own captures, and for CMF Buds we do have them ([notes](../hardware-tests/2026-11-06-cmf-buds.md)): each write was seen to take effect on the buds. `new NothingDriver(allowWrites: false)` makes the setters return the read-only result and never sends an `F` command.

## What was tested

- The encoder against every captured request, the reader against every captured reply and push (split chunks, several frames in one chunk, garbage, no-CRC pushes, buffer cap), sequence wrap.
- The driver against fake RFCOMM channels: no service, silent, closed, unknown model, full open with the captured values, a missing reply, writes on and off, level memory, pushes, ownership of the channel.
- Catalog: the shipped entry resolves, and a device named "CMF Buds" with another vendor ID or service does not match it in `DeviceCatalog` or `DeviceProfileDb`.

## Per-port checklist (plan §4)

- [x] `src/Samaat.Protocols.Nothing` references Core and Transport only (`ArchitectureTests` allow-list updated)
- [x] Only the protocol layer ported
- [x] Provenance headers, plus a `THIRD_PARTY_NOTICES.md` entry
- [x] Registered once in `BuiltInDrivers`
- [x] `devices/nothing/cmf-buds.json` with `"verified": true`, firmware 1.0.5.050
- [x] Replay tests from our own captures
- [x] Features map onto existing Core interfaces; no new capability
- [x] This decision record
- [ ] **Bump the phone app's submodule pin** after this merges

## What's left

Other Nothing and CMF models (each needs its model id and byte table), noise cancelling strength as a level control, firmware version parsing, and the buds' gestures.

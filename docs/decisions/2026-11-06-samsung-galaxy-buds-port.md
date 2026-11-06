# Decision: port Samsung Galaxy Buds support from GalaxyBudsClient

**Date:** 2026-11-06 · **Status:** implemented on branch `claude/port-samsung-84ffd5`, read-only, **not run on hardware** · **Plan:** [licensing and porting, phase 1](../plans/2026-11-05-licensing-and-porting.md#3-porting-order-one-brand-per-pr-each-with-a-decision-record)

## Source

| | |
|---|---|
| Project | [GalaxyBudsClient](https://github.com/timschneeb/GalaxyBudsClient) by Tim Schneeberger and contributors |
| Commit | `754b6fdfb1c151b022cece775c8aaad4eb185afa` (2026-10-01) |
| Licence | GPL-3.0 ("GPLv3", no "or later"). Samaat is GPL-3.0-or-later; while this code is in, the combined program is distributed under GPL-3.0 (see [the licence decision](2026-11-05-licence-gpl.md#why)) |
| Upstream files read | `GalaxyBudsClient/Message/SppMessage.cs`, `Utils/Crc16.cs`, `Message/SppMessageEnums.cs`, `Message/Decoder/{ExtendedStatusUpdate,StatusUpdate,NoiseControlUpdate,Acknowledgement}Decoder.cs`, `Message/Encoder/{SetEqualizer,ManagerInfo}Encoder.cs`, `Model/Constants.cs`, `Model/Specifications/*DeviceSpec.cs`, `GalaxyBudsClient.Tests/TestData/ExtendedStatusUpdate/*.bin` |
| Cross-check | None yet. Gadgetbridge (behaviour only, AGPL) wasn't consulted |

Upstream says its message *names* come from decompiling Samsung's app. Samaat copies no vendor code and keeps none of those names: only the numeric message IDs and byte layouts, which are facts of the protocol, taken from GalaxyBudsClient's own GPL-3.0 code.

## What was ported

`src/Samaat.Protocols.Samsung` (references Core and Transport only), driver id `samsung-spp`, registered once in `BuiltInDrivers`:

- **Framing:** SPP frames with SOM `0xFD`, a 2-byte size header, CRC-16 (XMODEM) and EOM `0xDD`; the 2019 Galaxy Buds' legacy `0xFE … 0xEE` frames. Chunk reassembly with resync on a bad frame; fragmented frames are dropped (upstream doesn't handle them either).
- **Battery:** left, right and case (not on the 2019 Buds) plus where each bud is (worn, out, in the case), from the status messages the buds push (`0x60`, `0x61`). Values above 100 are "unknown".
- **Noise control:** the current mode (off, noise cancelling, ambient) for the Buds Pro and every later model, from the extended status and the `0x77` push. Read-only (below).
- **EQ presets:** Normal, Bass boost, Soft, Dynamic, Clear, Treble boost for everything after the 2019 Buds. A custom EQ shows as no preset. Read-only.
- **Model table:** catalog entry → RFCOMM service UUID, framing, where the EQ byte sits, and whether the noise-mode byte exists. 11 models in `devices/samsung/`: Galaxy Buds, Buds+, Buds Live, Buds Pro, Buds2, Buds2 Pro, Buds FE, Buds3, Buds3 Pro, Buds3 FE, Buds Core.

## Read-only for now

The driver can send GalaxyBudsClient's commands (noise mode `0x78`, EQ `0x86`, confirmed by the buds' acknowledgement `0x42`, and the `MANAGER_INFO` reply `0x88` it sends after each status), but `BuiltInDrivers` creates it with writes **off**: it writes nothing to the buds, and the controls say changing them isn't supported yet.

Why: the plan review (Codex, two rounds) objected to shipping write commands learned only from another project's code and test vectors, with no Samaat capture from real buds. CONTRIBUTING says to learn protocols from our own captures, and the Bose driver, also `verified: false`, has real recorded frames. Reading is safe; writing to buds nobody here has tested isn't. **The owner decided (2026-11-06): read-only until we have our own captures**, matching the Bose BMAP precedent. Turning it on is `new SamsungDriver(allowWrites: true)` in `BuiltInDrivers`; the write path is already covered by tests against fake channels.

## What was tested

- Replay tests against upstream's seven `ExtendedStatusUpdate` vectors (Buds rev 3, Buds+ rev 13, Buds Live rev 9, Buds Pro rev 10, Buds2 rev 10, Buds2 Pro rev 13, Buds FE rev 2): battery, placement, case, EQ and noise mode match upstream's expected values. They're labelled as upstream vectors, not Samaat captures.
- Frame encode/decode, the reader (split chunks, several frames per chunk, garbage, bad CRC, fragments, buffer cap), the driver against fake RFCOMM channels (unknown model, silent and closed channels, features per model, no writes in read-only mode), and the write path with writes on (frames, ACK confirmation, time-out, a `MANAGER_INFO` reply to each status). Synthetic frames are labelled.
- Catalog: every `samsung-spp` entry is in the model table and the other way round, and sample names resolve to the right entry.
- **Not tested:** anything on real Galaxy Buds or a real Windows/Android RFCOMM link.

## Per-port checklist (plan §4)

- [x] `src/Samaat.Protocols.Samsung` references Core and Transport only (`ArchitectureTests` allow-list updated)
- [x] Only the protocol layer ported: no UI, framework or dependency from GalaxyBudsClient
- [x] Provenance headers, plus a `THIRD_PARTY_NOTICES.md` entry
- [x] Registered once in `BuiltInDrivers`
- [x] `devices/samsung/*.json` with `"verified": false`, features from upstream's model specs
- [x] Replay tests from upstream's test vectors
- [ ] **Replay tests from our own captures.** Open: no Galaxy Buds available. Needed: an Android HCI snoop log (or Windows ETW trace) of a connect (the `0x61` push), a noise-mode change on the buds (`0x77`), and, before writes are turned on, a mode and EQ change sent from the Samsung or GalaxyBudsClient app with its `0x42` acknowledgement
- [x] Features map onto existing Core interfaces (battery, noise control, EQ presets); no new capability
- [x] This decision record
- [x] No model marked verified
- [ ] **Bump the phone app's submodule pin** after this merges into dev (samaat-mobile `external/samaat.io`)

## What's left

- Hardware: one Buds2/Buds2 Pro/Buds FE capture, then the CONTRIBUTING checklist per model before any `verified: true`.
- Turn writes on once captures exist (the owner's condition).
- Not ported yet: Buds+ ambient sound (`SET_AMBIENT_MODE`), Buds Live noise cancelling (`SET_NOISE_REDUCTION`), the 2019 Buds' EQ and ambient, charging state (its byte moves between models and firmware revisions), firmware version, noise-cancelling and ambient levels, adaptive mode, touch controls, custom EQ, and the Buds4/Buds4 Pro.
- Cross-check against Gadgetbridge's behaviour once hardware shows a difference.

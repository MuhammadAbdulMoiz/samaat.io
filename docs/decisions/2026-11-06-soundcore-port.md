# Decision: port Soundcore support from OpenSCQ30

**Date:** 2026-11-06 · **Status:** implemented on branch `claude/port-sony-soundcore-5cf6ef`, read-only, **not run on hardware** · **Plan:** [licensing and porting, phase 3](../plans/2026-11-05-licensing-and-porting.md#3-porting-order-one-brand-per-pr-each-with-a-decision-record)

## Source

| | |
|---|---|
| Project | [OpenSCQ30](https://github.com/Oppzippy/OpenSCQ30) |
| Commit | `e75a68b70de8eaf2cf0ca364e132085a097557c5` (2026-10-29) |
| Licence | GPL-3.0. Samaat is GPL-3.0-or-later; while this code is in, the combined program is distributed under GPL-3.0 (as with [GalaxyBudsClient](2026-11-06-samsung-galaxy-buds-port.md)) |
| Upstream files read | `lib/src/devices/soundcore.rs`, `lib/src/devices/soundcore/common/{device.rs, packet.rs, packet/checksum.rs, packet/packet_io_controller.rs, packet/outbound/{request_state,set_sound_modes}.rs, packet/inbound/{battery_level,battery_charging,sound_modes}.rs, structures/{battery,sound_modes,equalizer_configuration,basic_hear_id,custom_hear_id,button_configuration,tws_status}.rs, modules/equalizer.rs, modules/button_configuration.rs}`, the state packets of `a3027`, `a3028`, `a3931`, `a3951` and `a3040`, and the model list in `README.md` |
| Cross-check | [PairFree](https://github.com/Omega-Destroyer/PairFree) @ `ebe0dfffec86923505f9c5fa7e2f74cb848964ca` (GPL-3.0), `data/soundcore/devices.json` only: its state layouts for all 8 models give the same offsets as ours (battery, EQ, and sound modes at 35 for the headphones). For the earbuds it puts sound modes at 36 (Life Dot 2 NC, Life A2 NC) and 77 (Liberty Air 2 Pro), matching our derivation, but its table is imported from OpenSCQ30 too, and it has no recordings from these models (`data/soundcore/reports` has none). So that isn't independent evidence, and the earbuds' noise mode stays out. PairFree also carries data read from the Soundcore app's own parsers (`soundcore_app`); none of that was used, since CONTRIBUTING rules out anything learned from vendor apps |

OpenSCQ30 is Rust; the logic was rewritten in C#. Only numeric command ids, byte layouts and preset ids are taken over; preset names are Samaat's own wording.

## What was ported

`src/Samaat.Protocols.Soundcore` (references Core and Transport only), driver id `soundcore-spp`, registered once in `BuiltInDrivers`:

- **Framing:** `08 EE 00 00 00` (to the device) / `09 FF 00 00 01` (from it), a 2-byte command, a little-endian total length and an 8-bit sum checksum. A reader with resync on bad frames and a buffer cap.
- **Service:** the Soundcore vendor RFCOMM UUID (`0cf12d31-fac3-4553-bd80-d6832e7xxxxx`, matched with upstream's mask) when the device lists one, otherwise standard SPP.
- **State:** the driver asks for the state (`01 01`) up to three times, as upstream does, and builds the features from the reply. Later updates come from the pushes for battery level (`01 03`), charging (`01 04`) and sound modes (`06 01`).
- **Battery:** the devices report 0 to 5 steps, shown as 0 to 100 % in steps of 20, plus charging.
- **Noise control:** the current mode (off, noise cancelling, transparency) on the Life headphones.
- **EQ presets:** the 22 built-in presets by id; a custom EQ shows as no preset. Read-only.
- **Models**, 8 in `devices/soundcore/`:

  | Entry | Upstream model | Shown |
  |---|---|---|
  | Life Q30, Life Tune | A3028, A3029 | battery, noise mode, EQ preset |
  | Life Q35, Life Tune Pro | A3027, A3030 | battery, noise mode, EQ preset |
  | Life Dot 2 NC, Life A2 NC | A3931, A3935 | left/right battery, EQ preset |
  | Liberty Air 2 Pro | A3951 | left/right battery, EQ preset |
  | Space Q45 | A3040 | battery |

  The noise mode is left out on the three earbud models: where it sits in their state packet depends on the sizes of several variable structures, and upstream has no real packet from those models to check it against. The Space Q45 uses its own EQ and sound-mode layouts, which aren't ported.

## Read-only for now

The owner's rule for every port: **read-only until we have our own captures.** The shipped driver writes only the state request (`01 01`), which is a query; tests check that nothing else is ever written. Setting the noise mode (`06 81`, confirmed by the device's `06 81` acknowledgement) is ported and tested against fake channels, but only `new SoundcoreDriver(allowWrites: true)` turns it on. Setting an EQ preset isn't ported at all: upstream sends each preset's band values, and those tables aren't ported.

## What was tested

- Replay tests against upstream's real Life Q30 packets (labelled as upstream vectors, not Samaat captures): its example state packet, the packet with a preset EQ, the one with a level of 4, the packets from GitHub issue 141 and Flathub issue 34, its sound-mode push examples, and its battery level and charging packets. Upstream's request-state and set-sound-modes byte examples are checked against the encoder.
- The reader (split chunks, several frames per chunk, garbage, bad checksum, junk), service selection, the driver against fake RFCOMM channels (unknown model, no service, silent channel with three retries, closed channel, features per model, pushes, the read-only write allowlist), the write path with writes on, and disposal. The earbud and Space Q45 state packets are synthetic, built from upstream's layouts, and labelled.
- Catalog: every `soundcore-spp` entry is in the model table and the other way round, and sample names resolve to the right entry.
- **Not tested:** anything on real Soundcore devices or a real RFCOMM link.

## Per-port checklist (plan §4)

- [x] `src/Samaat.Protocols.Soundcore` references Core and Transport only (`ArchitectureTests` allow-list updated)
- [x] Only the protocol layer ported: no UI, framework or dependency from OpenSCQ30
- [x] Provenance headers, plus a `THIRD_PARTY_NOTICES.md` entry
- [x] Registered once in `BuiltInDrivers`
- [x] `devices/soundcore/*.json` with `"verified": false`, names and features from upstream's model list
- [x] Replay tests from upstream's test vectors (Life Q30)
- [ ] **Replay tests from our own captures.** Open: no Soundcore device available. Needed: an Android HCI snoop log of the Soundcore app connecting (the `01 01` state exchange) for each model family, especially one earbud model to confirm the noise-mode offset, then a mode change
- [x] Features map onto existing Core interfaces; no new capability
- [x] This decision record
- [x] No model marked verified
- [ ] **Bump the phone app's submodule pin** after this merges

## What's left

- Captures, then turning the noise-mode write on.
- The noise mode on the earbud models (needs one real state packet each), EQ preset writes (port the band tables), the Space Q45's EQ and modes, the case battery, firmware and serial number, and the other ~25 models OpenSCQ30 supports.

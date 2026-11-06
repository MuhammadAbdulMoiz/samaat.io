# Sony headphones, protocol v1: what Samaat sends and reads

Status: **written from other projects, not from our own captures.** Samaat's `sony-mdr` driver
(`src/Samaat.Protocols.Sony`) reads battery and the current noise mode and shows them read-only. Every model
entry is `verified: false` until someone records a session from real headphones
([decision](../decisions/2026-11-06-sony-port.md)).

## Sources, and how each one is used

| Part | Source | How |
|---|---|---|
| Framing (markers, escaping, checksum, sequence numbers, ACK) and the noise/ambient **set** command | [SonyHeadphonesClient](https://github.com/Plutoberth/SonyHeadphonesClient) @ `5620e8ed`, MIT | Ported, with provenance headers and a `THIRD_PARTY_NOTICES.md` row |
| The init exchange, the battery and noise-mode **reads**, and answering the headphones' own messages with an ACK | [Gadgetbridge](https://codeberg.org/Freeyourgadget/Gadgetbridge) @ `be3fcbf7`, AGPL-3.0 | **Behaviour only** (CONTRIBUTING): read to learn the message formats, written down here in our own words, and implemented from this note. No Gadgetbridge code was copied or translated |

SonyHeadphonesClient can only set the noise mode and virtual sound; it never reads anything back, so the reads
couldn't come from there.

## Transport

Classic RFCOMM, service UUID `96CC203E-5068-46AD-B32D-E316F5E069BA`.

## Frames

A frame on the wire is `3E`, then the escaped content, then `3C`. The content is:

| Bytes | Meaning |
|---|---|
| 1 | Kind: `01` acknowledgement, `0C` command (the only kinds v1 uses here) |
| 1 | Sequence number (0 or 1 in practice) |
| 4 | Payload length, big-endian, counted before escaping |
| n | Payload. For a command, its first byte says what it is |
| 1 | Checksum: the low 8 bits of the sum of every content byte before it |

Escaping: inside the content, `3C`, `3D` and `3E` are each sent as two bytes, `3D` followed by `2C`, `2D` or `2E`
respectively. Unescape before checking the length and the checksum.

## Taking turns

- The host sends one command at a time and waits for the headphones' acknowledgement before the next one.
  The acknowledgement carries the sequence number the host puts on its **next** command.
- When the headphones send a command of their own (a reply or an unprompted update), the host answers with an
  acknowledgement: no payload, and a sequence number of 1 minus the one it received.

Samaat counts these acknowledgements as part of reading, not as writes: without them the headphones stop talking.

## Starting a session

The host sends the command `00 00`. The headphones reply with a command whose first byte is `01`:

- 4 payload bytes (for example `01 00 40 10` on a WH-1000XM3): **protocol v1**, which this note covers.
- 8 bytes: protocol v2 (the WF-1000XM4 and later models). Samaat's driver gives no features for v2.

## Battery

Ask with `10 t`, where `t` picks the battery:

| `t` | Battery | Reply layout after `11 t` |
|---|---|---|
| `00` | the one battery of a headset | level, charging |
| `01` | left and right bud | left level, left charging, right level, right charging |
| `02` | the charging case | level, charging |

The reply starts with `11`. The headphones also send the same layout unprompted when it changes, starting with
`13`. Levels are percent. Charging is `01` while charging, anything else means not charging. For left and right,
a level of 0 means that bud isn't connected, so it has no reading.

## Noise and ambient sound (current mode)

Ask with `66 02`. The reply starts with `67`, and unprompted changes start with `69`. Both are 8 bytes:

| Byte | Meaning |
|---|---|
| 0 | `67` or `69` |
| 1 | `02` |
| 2 | `00` off, `01` on (noise cancelling or ambient sound) |
| 3 | `00` on models without a wind mode; `02` on models with one |
| 4 | When on, and byte 3 is `00`: `00` ambient sound, `01` noise cancelling. When byte 3 is `02`: `00` ambient sound, `01` wind noise reduction, `02` noise cancelling |
| 5 | not used here |
| 6 | Focus on voice: `00` off, `01` on |
| 7 | Ambient sound level, 0 to 20 |

Samaat shows off as Off, ambient sound as Transparency, and both noise cancelling and wind noise reduction as
Noise cancelling (wind noise reduction is a noise-cancelling setting tuned for wind). Anything else is ignored.

## Setting the mode (ported, switched off)

SonyHeadphonesClient sets the mode with the 8-byte command
`68 02 effect 01 dualSingle 01 voice level`, where `effect` is `00` (off) or `11` (on), `dualSingle` is
`02` (level 0), `01` (level 1) or `00` (otherwise), `voice` is `00` or `01`, and `level` is the ambient level
(it sends `FF` when off). The headphones confirm it with an acknowledgement. Samaat's shipped driver never sends it:
writes stay off until we have our own captures.

## Models

| Catalog entry | Battery asked | Noise mode asked | Note |
|---|---|---|---|
| `sony.wh-1000xm3` | headset | yes | v1 per Gadgetbridge's init example |
| `sony.wh-1000xm4` | headset | yes | Assumed v1; if it answers v2 the driver gives nothing |
| `sony.wf-1000xm3` | left/right and case | yes | |
| `sony.mdr-xb950bt` | headset | no (no noise cancelling) | Not in Gadgetbridge; SonyHeadphonesClient reports it works. A lead only |

The driver offers only what got a well-formed reply, so a model that ignores a request just loses that feature.

## Not covered

EQ, virtual sound and sound position reads, speak-to-chat, touch controls, protocol v2 (XM4 buds, XM5 and later),
and anything learned from our own captures (none yet).

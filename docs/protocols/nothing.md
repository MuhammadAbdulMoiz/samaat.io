# Nothing / CMF RFCOMM protocol

Used by the `nothing-spp` driver (`src/Samaat.Protocols.Nothing`) on CMF Buds. Source: BudsLink (GPL-3.0, see [the decision](../decisions/2026-11-06-nothing-cmf-port.md)), checked against Samaat's own captures ([notes](../hardware-tests/2026-11-06-cmf-buds.md)). RFCOMM service `aeac4a03-dff5-498f-843a-34487cf133eb`.

## Frame

```
55 | c0 c1 | cmd u16 LE | len u16 LE | seq u8 | payload | [crc u16 LE]
```

- **To the buds:** `c0 c1` is always `60 01`, and a CRC16/MODBUS (init `0xFFFF`, reflected polynomial `0xA001`) over every earlier byte follows the payload, little-endian.
- **From the buds:** a CRC follows the payload only when `(frame[1] & 0x20) != 0`. Samaat does not check it: acks carry `FF FF`. Pushes have `c0 = 00`, so no CRC.
- **seq:** 1 to 250, then back to 1. Replies (`0x4xxx`) and acks (`0x7xxx`, empty payload) echo the request's seq; pushes use 0. One request is in flight at a time; the timeout is 1 s.
- The reader resyncs by skipping to the next `55`, waits for the whole frame, and drops its buffer above 4 KB.

## Commands

| Purpose | Send | Reply | Payload |
|---|---|---|---|
| Model id | `C01C` | `401C` | `68 B1`: u16 LE `0xB168` is CMF Buds |
| Battery | `C007` | `4007` | `count, [id, level]...`; id `02` left, `03` right, `04` case, `06` single bud; `level & 0x7F` is percent, `0x80` is charging. Push `E001`, same layout |
| ANC read | `C01E` | `401E` | `02 01 00 01 MM 00`: mode is `payload[4]` |
| ANC set | `F00F` `01 MM 00` | ack `700F`, then push `E003` `01 MM 00` | mode is `payload[1]` of the push |
| EQ read | `C050` | `4050` | `PP` |
| EQ set | `F01D` `PP 00` | ack `701D` | |

`C01F` and `F010` address a different register on this model and are not used.

## Bytes

ANC `MM`: `05` off, `07` transparency, `01`/`02`/`03` noise cancelling (high, mid, low). `04` is ignored by CMF Buds and is never sent. Samaat remembers the last of `01`/`02`/`03` the buds reported and sends it when asked for noise cancelling (default `01`).

EQ `PP`: `00` Balanced, `01` Rock, `02` Electronic, `03` Pop, `04` Enhance vocals, `05` Classical, `06` Custom (names are Samaat's own wording). Choosing Custom restores the curve stored on the buds.

## Differences from BudsLink

1. **ANC read offset:** the current mode is `payload[4]` of `401E`, not the offset BudsLink reads.
2. **Noise-cancelling byte:** `01`/`02`/`03` are all echoed back by the buds as noise cancelling levels, so Samaat sends the last one the buds reported instead of a fixed byte. `04` does nothing.

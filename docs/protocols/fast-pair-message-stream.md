# Google Fast Pair Message Stream

What Samaat uses or has checked of the Fast Pair Message Stream, with sources. Driver: `src/Samaat.Protocols.FastPair`.

## Transport and framing

- RFCOMM service `df21fe2c-2515-4fdb-8886-f12c4d67927c` (the realme Buds T200 Lite serves it on channel 10). Only one client can hold it at a time; a second connect may succeed but receive nothing.
- Every message: `group(1) code(1) length(2) data`. Length and data are big-endian ([Message Stream][ms]).
- On every open, when no other client holds the channel, the provider pushes its Device Information (group `0x03`): model ID (`0x01`), BLE address (`0x02`), battery (`0x03`), firmware (`0x09`), session nonce (`0x0A`). Samaat reads only these.

## Smart Audio Source Switching (SASS), group `0x07`

From the [SASS spec][sass]. "MAC" means the message must carry an 8-byte message nonce and an 8-byte HMAC-SHA256 truncated MAC.

| Code | Name | Direction | MAC | Reply |
|---|---|---|---|---|
| `0x10` | Get capability | either | no | `0x11` |
| `0x11` | Notify capability: version (2, `0x0102`; `0x0000` = unsupported), flags (2) | either | seeker only | ACK |
| `0x12` | Set multipoint state (1 = on, 0 = off) | S→P | yes | ACK/NAK |
| `0x20` / `0x21` / `0x22` | Set / get / notify switching preference | S→P, P→S | set only | `0x22` for get |
| `0x30` | Switch active audio source (flags: to this device, resume, reject SCO, disconnect other) | S→P | yes | ACK/NAK (`0x04` = already active) |
| `0x31` | Switch back (`0x01`, `0x02` = and resume) | S→P | yes | ACK/NAK |
| `0x32` | Notify multipoint-switch event: reason, target (1 = this, 2 = other), target name (UTF-8) | P→S | no | none |
| `0x33` | Get connection status | S→P | no | `0x34` |
| `0x34` | Notify connection status: active flag (1 byte, plain) + encrypted state/bitmap + message nonce | P→S | no | none |
| `0x40` | Notify SASS-initiated connection | S→P | yes | ACK/NAK |
| `0x41` | Indicate in-use account key (`"in-use"`) | S→P | yes | ACK/NAK |
| `0x42` | Send custom data | S→P | yes | ACK/NAK |
| `0x43` | Set drop connection target | S→P | yes | ACK/NAK |

Capability flags (octets 6-7 of `0x11`, MSB first): bit 0 SASS on, bit 1 multipoint toggleable, bit 2 multipoint on, bit 3 on-head detection supported, bit 4 on-head detection on.

Handshake: the seeker sends `0x10`; **no reply within 1 s, or version `0x0000`, means SASS is unsupported**. Multipoint-only messages are used only when `0x11` reports multipoint.

ACK is `FF 01 len 07 code [data]`; NAK is `FF 02 0003 reason 07 code` ([SASS spec][sass]). NAK reason `0x03` means a wrong MAC ([MAC spec][mac]).

### Why Samaat can't send the write commands

Every state-changing SASS message (`0x12`, `0x30`, `0x31`, `0x40`-`0x43`) needs a MAC:
`first 8 bytes of HMAC-SHA256(K = account key padded with 48 zero bytes, session_nonce || message_nonce || message)` ([MAC spec][mac]). The provider checks it against the account keys it stores, which phones write during Fast Pair key-based pairing (ECDH with the model's anti-spoofing key). Windows doesn't do Fast Pair pairing, so this PC has no account key and the provider would NAK every switch with `0x03`. The `0x34` status bitmap is encrypted with a key derived from the same account key; only its first byte (is this seeker the active one) is readable.

So even on buds that implement SASS, a PC without an account key can read: capability and multipoint on/off (`0x11`), whether this PC is the active source (`0x34` octet 4), and switch events with the other host's name (`0x32`). It cannot switch, toggle multipoint or list hosts.

## Tested on the realme Buds T200 Lite (firmware 1.0.0)

2026-11-03, buds connected to the PC and the user's phone at once, Samaat stopped so the probe held the channel. With the user's OK the probe sent only the three no-data, no-MAC "get" requests:

```
13:50:18.578 connected
13:50:18.777 rx 0301000367B85103020006F3A2C67986C603030003503CFF   model, BLE address, battery 80/60/unknown
13:50:18.778 rx 03090005312E302E30030A00080102030405060708         firmware "1.0.0", session nonce
13:50:20.593 tx 07100000   SASS get capability
13:50:22.601 tx 07330000   SASS get connection status
13:50:24.615 tx 07210000   SASS get switching preference
13:50:26.617 listening 20s
13:50:46.630 done          (nothing more received: no 0x11, no 0x34, no 0x22, no ACK/NAK)
```

An earlier run (13:42, PC only) also got no reply, but its channel turned out to be held by another client, so it proves nothing. Not yet captured: whether the buds push anything unprompted while the phone connects or disconnects.

**Result: the T200 Lite doesn't implement SASS** (no `0x11` within 1 s means unsupported, per the spec). Its dual connection is plain Bluetooth multipoint that the buds don't expose over the Message Stream, so Samaat can't show the phone or switch audio for it. The Links page correctly shows only "This computer".

The BLE address in the greeting rotates (`F7:A0:…` earlier, `F3:A2:…` now), as Fast Pair's resolvable private addresses do; don't key anything on it.

[ms]: https://developers.google.com/nearby/fast-pair/specifications/extensions/messagestream
[sass]: https://developers.google.com/nearby/fast-pair/specifications/extensions/sass
[mac]: https://developers.google.com/nearby/fast-pair/specifications/extensions/mac

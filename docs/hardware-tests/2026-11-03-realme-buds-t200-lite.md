# Hardware test: realme Buds T200 Lite on Windows 11 — 2026-11-03

## Identity

| | |
|---|---|
| Name | `realme Buds T200 Lite` |
| Classic address | `28:04:C6:79:86:C6`, CoD `0x240404` (Audio/Video) |
| PnP VID/PID | `0x05D6` / `0x000A` (Jieli chip, same IDs as the Quantum and the ParadoX-E) |
| SDP | `JL_A2DP`, AVRCP 1.6, `JL_HFP` (ch 4), `JL_SPP` records for SPP `1101` (ch 1), **`df21fe2c-2515-4fdb-8886-f12c4d67927c` (ch 10)** and `0000079a-d102-11e1-9b23-00025b00a5a5` (ch 11) |
| BLE | Separate address `F7:A0:C6:79:86:C6`, name `realme Buds T200 Lite(BLE)`, GATT `1800` and **`FE2C`** (Google Fast Pair: `fe2c1233` model ID, `fe2c1234` key-based pairing, `fe2c1235` passkey, `fe2c1236` account key, `fe2c1237` additional data, `2A26` firmware). No `AE00`. |

## Samaat before any change

Listed as `realme Buds T200 Lite [Connected, Basic support]` with **no battery**: Windows reports no HFP battery level for it, so the dashboard has nothing to show.

## Fast Pair Message Stream (read-only, nothing written)

`df21fe2c-2515-4fdb-8886-f12c4d67927c` is Google Fast Pair's Message Stream RFCOMM service. On connect, the buds pushed (format `group(1) code(1) length(2, BE) data`):

```
03 01 0003 67B851                     Device information: model ID 0x67B851
03 02 0006 F7A0C67986C6               BLE address F7:A0:C6:79:86:C6
03 03 0003 5A 46 FF                   Battery: left 90 %, right 70 %, case unknown
03 09 0005 312E302E30                 Firmware revision "1.0.0"
03 0A 0008 0102030405060708           Session nonce
```

Battery bytes follow the Fast Pair spec: low 7 bits = percent, `0x7F` = unknown, bit 7 = charging (`FF` = unknown).
The `0000079a-…-00025b00a5a5` channel closed as soon as it was opened (unknown purpose).

Side note: the Quantum Zbuds' 24-byte greeting on its channel 10 (`030100030000000302000608050403020103030003FFFFFF`) is the same message format with placeholder values (model `000000`, dummy address, battery unknown), so the Quantum runs a stub of the same stack without real data.

## Meaning

The T200 Lite's battery (left, right, case, charging) and firmware are readable from a documented, standard protocol over Classic RFCOMM, with no pairing or writes. A generic Fast Pair Message Stream driver would also cover other Fast Pair earbuds. ANC and other controls are not part of what was observed; the spec's other message groups (device actions, ANC state in newer revisions) need confirmation before anything is written.

## Next

Done: `Samaat.Protocols.FastPair` reads this stream (read-only, nothing written) and `devices/realme/buds-t200-lite.json` ties it to the buds. The CLI now shows `realme Buds T200 Lite [Connected, Full support]` with Left 80 % and Right 70 % from the device protocol and firmware 1.0.0. Still to do: confirm the case byte with the buds charging in the case, check a second firmware, and decide whether ANC (newer Fast Pair message groups) is worth probing.

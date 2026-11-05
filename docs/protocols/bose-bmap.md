# Bose BMAP: what the QC Headphones answer

Status: **BMAP answers read requests with no handshake** on raw RFCOMM channels 8 and 9. No Samaat driver yet; the
headset stays on the Windows HFP battery level. Raw captures are in
[the hardware note](../hardware-tests/2026-11-05-bose-qc-headphones.md).

## Sources

- [aaronsb/bosectl](https://github.com/aaronsb/bosectl): `NOTES.md` (packet format, operators, AudioModes block) and
  `docs/quietcomfort-headphones-prince.md`, which covers this exact model (codename `prince`, PID `0x4075`, firmware
  `1.0.6-80+f5f219b`). Used to choose which requests to send. Only bytes this headset sent back are recorded as observed.

## Headset

`Bose QC Headphones`, PnP VID `0x009E` / PID `0x4075`, Classic address `68:F2:1F:1D:13:D6`.

SDP records Windows lists (WinRT `GetRfcommServicesAsync`, uncached), with the RFCOMM channel from attribute `0x0004`:

| Service class | Channel | BMAP? |
|---|---|---|
| `1108` Headset | 11 | not tried |
| `111E` Hands-Free | 12 | not tried |
| `00000000-deca-fade-deca-deafdecacaff` (Apple iAP2) | 14 | no: pushes `FF 55 02 00 EE 10` every second, ignores BMAP |
| `9b26d8c0-a8ed-440b-95b0-c4714a518bcc` (name `SRfcomm`) | 26 | no: accepts, never sends a byte |

**BMAP is on channels 8 and 9, which have no SDP record Windows lists.** Reached with a Winsock `AF_BTH` socket
(`SOCKADDR_BTH` with a zero service GUID and `port` = channel). Channel 2 refused ("host was down").
So a Samaat driver can't open it through `IDeviceTransports.OpenRfcommAsync(Guid)`; it needs a by-channel open.

## Frames

`function block(1) function(1) flags(1) payload length(1) payload`. Low nibble of `flags` = operator:
`0x01` GET, `0x02` SETGET, `0x03` STATUS (reply), `0x04` ERROR, `0x05` START, `0x06` RESULT, `0x07` PROCESSING
(per bosectl; `0x01` and `0x03` observed here).

## Observed (GET only, nothing written)

Identical replies on channel 8 and channel 9:

| Request | Reply | Meaning |
|---|---|---|
| `00 05 01 00` firmware | `00 05 03 10` + `312E302E362D38302B66356632313962` | `"1.0.6-80+f5f219b"` |
| `1F 03 01 00` current audio mode | `1F 03 03 01 00` | mode index `0` (Quiet per bosectl; the name wasn't read from the headset) |
| `02 02 01 00` battery | `02 02 03 04 14 FF FF 00` | `0x14` = 20 %, matching Windows HFP at the time; other 3 bytes unknown |

## Not yet observed (bosectl's claims for `prince`, untested here)

- Switch mode: START `1F 03 05 02 <index> <prompt 00|01>`, acked with PROCESSING and applied asynchronously;
  re-read with `1F 03 01 00`.
- Modes 0 Quiet, 1 Aware, 2+ user-configured; names via AudioModes GetAll (`1F 01 05 00`, a START).
- No unauthenticated "ANC off" on this model.

# Bose BMAP: what the QC Headphones answer

Status: **no BMAP reply observed.** Samaat has no Bose driver; the headset stays on the Windows HFP battery level.
Raw captures and timing are in [the hardware note](../hardware-tests/2026-11-05-bose-qc-headphones.md).

## Headset

`Bose QC Headphones`, PnP VID `0x009E` / PID `0x4075`, Classic address `68:F2:1F:1D:13:D6`.
RFCOMM services (WinRT `GetRfcommServicesAsync`, uncached): `1108` (Headset), `111E` (Hands-Free),
`00000000-deca-fade-deca-deafdecacaff`, `9b26d8c0-a8ed-440b-95b0-c4714a518bcc`. No BLE GATT reachable
(`LE-Bose QC Headphones`, disconnected, GATT `Unreachable`).

## What was sent

Only reads. Each request is a BMAP GET as public reverse-engineering describes it for older Bose headsets
(`function block(1) function(1) operator(1) length(1) payload`, operator `0x01` = get, length `0`):
`00 01 01 00`, `00 03 01 00`, `00 05 01 00`, `01 02 01 00`, `01 06 01 00`, `02 02 01 00`, `1F 03 01 00`, `1F 01 01 00`.
These are probes, not confirmed values for this model. No set (`0x02`) or start (`0x05`) operator was ever sent.

## What came back

| Service | Observed |
|---|---|
| `00000000-deca-fade-deca-deafdecacaff` | Pushes `FF 55 02 00 EE 10` once a second from connect on, before and after every request. Nothing else, ever. Later connects to it timed out ("connected party did not properly respond") while the other service still connected. |
| `9b26d8c0-a8ed-440b-95b0-c4714a518bcc` | Connects, then stays silent: no bytes at all, including 6 s of listening before the first request and 3 s after each one. |

Reading of `FF 55 02 00 EE 10` (interpretation, not confirmed): `FF 55` is Apple's iAP sync, `02` a length, `00 EE`
the body and `10` its checksum (`-(02+00+EE) mod 256`). It matches the iAP2 link-detect sequence, so the
`deca-fade` port looks like an iAP2 accessory port waiting for an iAP2 host. BMAP may ride inside an iAP2 session
there, but that needs an iAP2 link layer and identification first; nothing here shows BMAP bytes.

## Conclusion

Neither vendor service answers a plain BMAP GET without some prior handshake. Noise control can't be read, so
switching it isn't attempted. Options, none started: capture the Bose app's traffic (Android HCI snoop log) to see
which service and handshake it uses, or implement an iAP2 host on `deca-fade` if the capture shows BMAP there.

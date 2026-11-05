# Hardware test: Bose QC Headphones on Windows 11 — 2026-11-05

## Identity

| | |
|---|---|
| Name | `Bose QC Headphones` (BLE side: `LE-Bose QC Headphones`) |
| Classic address | `68:F2:1F:1D:13:D6` |
| PnP VID/PID | `0x009E` (Bose) / `0x4075` |
| RFCOMM | `1108`, `111E`, `00000000-deca-fade-deca-deafdecacaff`, `9b26d8c0-a8ed-440b-95b0-c4714a518bcc` |
| BLE | GATT `Unreachable` while the Classic link was up |

## Samaat before any change

`devices/bose/qc-headphones.json` (`driver: none`): Windows HFP battery 20 %, noise control "Couldn't detect".

## Probe (read-only)

A throwaway WinRT console app opened each vendor service with `StreamSocket`, logged every byte received and wrote
the BMAP GET frames listed in [bose-bmap.md](../protocols/bose-bmap.md). Headset connected to the PC the whole time.

Run 1, `deca-fade`, 2 s listen then one GET every 1.2 s:

```
10:49:36.332 RX FF550200EE10
10:49:37.332 RX FF550200EE10
10:49:38.338 TX 00010100
10:49:38.340 RX FF550200EE10
... (RX FF550200EE10 every ~1 s; TX 00030100 00050100 01020100 01060100 02020100 1F030100 1F010100)
10:49:47.360 RX FF550200EE10
```

Run 1, `9b26d8c0`, same requests right after: connected, **no RX at all**.

Run 2, `9b26d8c0`, 6 s listen, then `00010100`, `00050100`, `1F030100` 3 s apart: connected, no RX.

Runs 3–5, `deca-fade` (GET sent with no initial wait): `ConnectAsync` timed out each time ("connected party did not
properly respond"), while `9b26d8c0` still connected and stayed silent in the same run. The headset was not
power-cycled or re-paired between runs.

## Meaning

No BMAP response on either vendor service without a handshake. `deca-fade` behaves like an iAP2 accessory port (its
only output is what looks like the iAP2 detect sequence); `9b26d8c0` takes a connection and says nothing. No Bose
driver was built; the catalog entry stays `driver: none`, `verified: false`.

## Next

- Capture the Bose app talking to the headset (Android Bluetooth HCI snoop log) to learn which service it uses and
  what it sends first.
- Recheck whether `deca-fade` accepts connections again after the headset reconnects.

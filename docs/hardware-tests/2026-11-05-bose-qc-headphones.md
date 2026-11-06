# Hardware test: Bose QC Headphones on Windows 11 — 2026-11-05

## Identity

| | |
|---|---|
| Name | `Bose QC Headphones` (BLE side: `LE-Bose QC Headphones`) |
| Classic address | `68:F2:1F:1D:13:D6` |
| PnP VID/PID | `0x009E` (Bose) / `0x4075` |
| Firmware | `1.0.6-80+f5f219b` (BMAP `[0.5]`) |
| SDP (RFCOMM) | `1108` ch 11, `111E` ch 12, `deca-fade…` ch 14, `9b26d8c0…` (`SRfcomm`) ch 26 |
| BLE | GATT `Unreachable` while the Classic link was up |

## Samaat before any change

`devices/bose/qc-headphones.json` (`driver: none`): Windows HFP battery 20 %, noise control "Couldn't detect".

## Probe 1: the two listed vendor services (read-only)

WinRT `StreamSocket` to each listed vendor service; BMAP GETs `00010100 00030100 00050100 01020100 01060100 02020100
1F030100 1F010100`.

- `deca-fade` (ch 14): pushes `FF550200EE10` about once a second from connect on; no other bytes. Later connects timed
  out ("connected party did not properly respond").
- `9b26d8c0` (ch 26): connects, no bytes at all (6 s listen, 3 s after each GET).

## Probe 2: raw channels (read-only)

After reading bosectl's notes for this model (BMAP over SPP on channel 8, sometimes 9), a Winsock `AF_BTH` socket
connected straight to channel numbers. GETs only:

```
channel 2: connect failed ("destination host was down")
channel 8: connected
  TX 00050100   RX 00050310312E302E362D38302B66356632313962
  TX 1F030100   RX 1F03030100
  TX 02020100   RX 0202030414FFFF00
channel 9: connected, identical replies
```

Nothing pushed on connect; each reply came within 1.5 s.

## Meaning

BMAP reads work with no authentication or handshake on channel 8/9. The current audio mode is index 0; battery 20 %
agrees with Windows. Channels 8/9 have no SDP record Windows lists, so Samaat needs to open RFCOMM by channel number.
Mode switching (START `[31.3]`) wasn't sent.

## Next

Done since: the read-only `bose-bmap` driver (by-channel RFCOMM open via Winsock, noise mode read from channel 8/9),
tested only against the frames above. The headset went back to its owner before the driver could run on it.

- With the headset again: run `samaat-cli` and check `noise.anc` / `noise.transparency` show as available with the
  right mode.
- Send one mode switch and confirm it by read-back, to observe the START reply on this firmware; only then enable
  switching.

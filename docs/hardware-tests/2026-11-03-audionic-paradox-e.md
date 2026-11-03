# Hardware test: Audionic ParadoX-E on Windows 11 — 2026-11-03

Second test device. The owner reports the earbuds have a companion app ("software based").

## Identity

| | |
|---|---|
| Name | `ParadoX-E` |
| Address | `30:5F:27:7C:71:7E` (classic and BLE, public) |
| Class of Device | `0x240404` (Audio/Video) |
| PnP VID/PID | `0x05D6` / `0x000A` (Jieli; same IDs as the Quantum Zbuds, so the name is what tells them apart) |
| SDP | `JL_A2DP` (A2DP 1.4), AVRCP 1.6 controller and target, `JL_HFP` (HFP 1.8, ch 4), `JL_SPP` (SPP, ch 1). No vendor RFCOMM UUID. |
| BLE | GATT at the same address: `1800` (name), **`AE00`** with `AE01` (write without response) and `AE02` (notify): Jieli's RCSP control service. Advertises as `ParadoX-E` (scan response), no manufacturer data. |

## Samaat before any change

Listed by `samaat-cli` and the app as `ParadoX-E [Connected, Basic support]`, battery `Combined=100%` from Windows. Not missing: it shows up, but only with the Windows battery level.

## Read-only RCSP session over BLE (throwaway probe, not in the repo)

Sent only: the Jieli auth handshake and `GetTargetInfo` (op `0x02`, mask `FFFFFFFF`).

```
auth1  tx 00 ‖ 16 random
       rx 01 5FBA44F9AF6E228EA8573E15FAF8E1E8        (matches our JieLi cipher)
auth3  tx 02 70617373 ("\x02pass")
       rx 00 FBFB7830441A48787BC039EB502BE09A        (device challenge)
auth5  tx 01 ‖ encrypt(challenge)
       rx 02 70617373                                 authenticated
GetTargetInfo
       tx FEDCBA C0 02 0005 00 FFFFFFFF EF
       rx FEDCBA 00 02 0002 02 00 EF                  status 0x02, no attributes
                                                      (later found: op 0x02 is GetTargetFeatureMap; see below)
push   rx FEDCBA 80 C2 0013 01 05D6 01D2 004A 22 305F277C717E 02 64 64 00 62 EF
push   rx FEDCBA 80 09 0005 02 00 02 07 01 EF         (6 s later; not decoded)
```

The `0xC2` AdvInfo push decodes with the buds-sniffer layout (VID `05D6`, then the EDR address, then `[13]=02`, L, R, case, `[17]`):
**Left 100 %, Right 100 %, case not reported** (`00`; case lid closed or buds out of the case). Bit 7 of each level is the charging flag. Unlike the Redmi clone in buds-sniffer, it arrived once after auth rather than every 0.5 s; whether it repeats on change is still to be seen.

## Meaning

The ParadoX-E speaks Jieli RCSP over BLE, and per-bud (and probably case) battery is readable today. That makes it the first real target for a vendor adapter (PLAN §13 Q1). Noise control and other settings go through RCSP *write* commands (Jieli `SetSysInfo`); their opcodes and payloads are not confirmed yet, so nothing was written.

## Next

- Adapter: done for battery (`src/Samaat.Protocols.Jieli`, see docs/plans/2026-11-03-jieli-battery-adapter.md): BLE, RCSP framing, JieLi auth, battery from `0xC2` (L/R, case when reported, charging), Full tier. Note the AdvInfo bytes are L=[14], R=[15], case=[16] of the params after sn.
- Map the `0x09` push and the case byte by watching while buds go in and out of the case.
- ~~Find the ANC/transparency commands~~ done, see below.

## Noise control and EQ (second session, same day)

Protocol from Jieli's Apache-2.0 SDK; details and sources in [docs/protocols/jieli-rcsp.md](../protocols/jieli-rcsp.md).

### Read-only probe

```
GetTargetInfo (op 0x03)  tx FEDCBA C0 03 0006 01 FFFFFFFF 00 EF
                         rx status 00: protocol 0x20, EDR 305F277C717E, attr 10 (vid/pid) = 01D2 004A,
                            project "jl_sdk_ac697_publish", attr 13 = 0110021C
GetSysInfo 0x6000        tx FEDCBA C0 07 0006 02 FF 00006000 EF
                         rx FEDCBA 00 07 002C 00 02 FF 0A0D 000000000000000000
                                                   1D0E 03 000000000000000000 014000400040004000 024000400040004000 EF
GetSysInfo 0x1010        tx FEDCBA C0 07 0006 03 FF 00001010 EF
                         rx FEDCBA 00 07 0074 00 03 FF 0C04 0300080804000000000202
                                                   630C 0A 0019003A00940168035C05F00BB817701F4035E8 (10 freqs) + 7 × 11 preset bytes EF
GetSysInfo 0xFFF7        also battery 0x64, volume 0x06, phone status 0, and the attributes above
```

So: modes Off / Noise cancelling / Transparency, current Off; EQ preset 3 (Classical) of 7. The `0x09` push from the first session (`00 | 02 07 01`) is Bluetooth-mode attribute 7, music play status, not ANC.

### Writes (approved by the owner, exact frames listed to them first)

```
SET noise cancelling  tx FEDCBA C0 08 000D 05 FF 0A 0D 01 4000 4000 4000 4000 EF
                      rx FEDCBA 00 08 0002 00 05 EF                         status 00
                      rx FEDCBA 80 09 000D .. FF 0A0D 014000400040004000 EF  push of the new mode
                      readback 0x2000 → 01 4000 4000 4000 4000
SET transparency      tx FEDCBA C0 08 000D 07 FF 0A 0D 02 4000 4000 4000 4000 EF → status 00, push, readback 02
SET off               tx FEDCBA C0 08 000D 09 FF 0A 0D 00 0000 0000 0000 0000 EF → status 00, push, readback 00
SET EQ Rock           tx FEDCBA C0 08 000F 0B FF 0C 04 01 FE 00 02 04 FE FE 00 00 04 04 EF → status 00, push, readback mode 01
SET EQ Classical      tx FEDCBA C0 08 000F 0D FF 0C 04 03 00 08 08 04 00 00 00 00 02 02 EF → status 00, push, readback mode 03
```

The owner, wearing the buds with music playing, heard every change. Also seen while listening: `0x09` function `0x00` pushes with the track title and artist (attrs 0 and 1), a `0xC4` device request (flags `0xC0`, params `04`) that Samaat leaves unanswered, and the `0xC2` battery push again (case still `00`; the unknown last byte went from `62` to `66`).

The touch-control capture (ANC toggled on the buds while listening) was skipped; changes made on the buds are expected to arrive as the same `0x09` push, but that is unverified.

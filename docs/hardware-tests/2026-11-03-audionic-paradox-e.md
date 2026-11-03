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
- Find the ANC/transparency commands (Jieli SDK sources or the Audionic app) before any write.

# Jieli RCSP: noise control and EQ

What Samaat sends to Jieli earbuds after the auth handshake, and where each byte comes from. Checked on the Audionic ParadoX-E (AC697N, `jl_sdk_ac697_publish`) on 2026-11-03; raw captures are in [the hardware note](../hardware-tests/2026-11-03-audionic-paradox-e.md).

## Sources

- **Jieli's official Android SDK** ([Jieli-Tech/Android-JL_Bluetooth](https://github.com/Jieli-Tech/Android-JL_Bluetooth), Apache 2.0), v4.2.0: the HTML developer docs (`doc/JieLi_Home_SDK_V4.2.0_html_en`: `function/anc_ctrl`, `function/eq_ctrl`, `constant/func_code`) and the constants and builders in `jl_bluetooth_rcsp_V4.2.0_40250-release.aar` (`constant.Command`, `constant.AttrAndFunCode`, `utils.CommandBuilder`, `bean.base.VoiceMode`, `bean.parameter.GetSysInfoParam`/`SetSysInfoParam`, `bean.base.AttrBean`, `tool.RcspEventHandler`). Behaviour was read from these, not copied; Samaat's code is its own.
- The iOS SDK ([Jieli-Tech/iOS-JL_Bluetooth](https://github.com/Jieli-Tech/iOS-JL_Bluetooth), Apache 2.0) agrees: `JL_AncMode` 0 normal, 1 noise reduction, 2 transparent; `JLModel_ANC` holds mode plus L/R max and current gain.
- [ElectronicCats/jieli-ble-badge-research](https://github.com/ElectronicCats/jieli-ble-badge-research) (MIT) `qix_ble/sysinfo.py`: the same GetTargetInfo/GetSysInfo wire format, verified against a btsnoop capture.

## Frames

`FE DC BA | flags | op | len(2, BE) | body | EF`, `len` counts the body.

- Command (flags bit 7 set): body = `sn | params`. Bit 6 = the sender wants a response. Samaat sends `0xC0`.
- Response (bit 7 clear): body = `status | sn | params`. Status `0x00` = success.

The earlier note read op `0x02` as GetTargetInfo; it is `CMD_GET_TARGET_FEATURE_MAP`, and the `02` in its reply was the status. GetTargetInfo is `0x03`.

## Opcodes used

| Op | SDK name | Params | Samaat use |
|---|---|---|---|
| `0x03` | `CMD_GET_TARGET_INFO` | `mask(4, BE) platform(1)` | Probe only |
| `0x07` | `CMD_GET_SYS_INFO` | `function(1) mask(4, BE)` | Read noise mode, EQ |
| `0x08` | `CMD_SET_SYS_INFO` | `function(1) TLV…` | Set noise mode, EQ preset |
| `0x09` | `CMD_SYS_INFO_AUTO_UPDATE` (device → host) | `function(1) TLV…` | Listen |
| `0xC2` | `CMD_ADV_DEVICE_NOTIFY` (device → host) | AdvInfo | Battery |

Never sent: `0xD0`–`0xEF` (app info, MTU, MD5, and the OTA range `0xE1`–`0xE8`: update, reboot).

TLV = `len(1) type(1) value(len-1)`. Function `0xFF` = public attributes. Mask bit *n* asks for attribute *n*.

## Public attributes (function `0xFF`)

| Attr | Name | Value |
|---|---|---|
| `0x04` | EQ | `mode(1) gains(10, signed dB)`; bit 7 of mode = dynamic layout `mode count gains(count)` |
| `0x0C` | EQ presets | `n(1) freq(2, BE)×n` then 7 × `mode(1) gains(n)` |
| `0x0D` | Current noise mode | VoiceMode (9 bytes) |
| `0x0E` | All noise modes | `count(1)` then VoiceMode × count |

VoiceMode = `mode(1) leftMax(2) rightMax(2) leftCur(2) rightCur(2)`, all BE. Modes: `0` off ("normal"), `1` noise cancelling, `2` transparency. EQ modes: `0` Standard, `1` Rock, `2` Pop, `3` Classical, `4` Jazz, `5` Country, `6` Custom.

## Commands Samaat sends

Reads:

```
GetSysInfo noise current+all   FEDCBA C0 07 0006 sn FF 00006000 EF
GetSysInfo current noise       FEDCBA C0 07 0006 sn FF 00002000 EF
GetSysInfo EQ + presets        FEDCBA C0 07 0006 sn FF 00001010 EF
GetSysInfo EQ                  FEDCBA C0 07 0006 sn FF 00000010 EF
```

Writes (what the SDK's `buildSetCurrentVoiceMode` and `configEqInfo` send):

```
Set noise mode   FEDCBA C0 08 000D sn FF 0A 0D <VoiceMode as the buds listed it> EF
Set EQ preset    FEDCBA C0 08 000F sn FF 0C 04 <mode> <that preset's 10 gains as the buds listed them> EF
```

Each set is confirmed by reading the attribute back.

## ParadoX-E answers

```
modes    0E 03 | 00 0000 0000 0000 0000 | 01 4000 4000 4000 4000 | 02 4000 4000 4000 4000
current  0D    | 00 0000 0000 0000 0000                                   → Off
EQ       04    | 03 | 00 08 08 04 00 00 00 00 02 02                       → Classical
presets  0C    | 0A | 25 58 148 360 860 1520 3000 6000 8000 13800 Hz
                 0 Standard  -8  0  0 -5 -6  1  8 -2  0 -4
                 1 Rock      -2  0  2  4 -2 -2  0  0  4  4
                 2 Pop        3  1  0 -2 -4 -4 -2  0  1  2
                 3 Classical  0  8  8  4  0  0  0  0  2  2
                 4 Jazz       0  0  0  4  4  4  0  2  3  4
                 5 Country   -2  0  0  2  2  0  0  0  4  4
                 6 Custom     0  0  0  0  0  0  0  0  0  0
```

The ANC gains report max = current (`0x4000`), so there is no strength to adjust. The `0x09` push seen after auth, `00 | 02 07 01`, is function `0x00` (Bluetooth mode) attribute 7 (`ID3_PLAY_STATUS`) = 1: music playing, not ANC.

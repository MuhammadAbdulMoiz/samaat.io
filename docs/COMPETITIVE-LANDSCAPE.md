# Competitive landscape

**Checked:** 2026-11-05/06 · **Source plan:** [`docs/plans/2026-11-05-licensing-and-porting.md`](plans/2026-11-05-licensing-and-porting.md) (on `docs/session-2026-11-05`) · **Licence decision:** [`docs/decisions/2026-11-05-licence-gpl.md`](decisions/2026-11-05-licence-gpl.md)

Other open-source earbud apps, what they cover, how Samaat compares, and which of them Samaat may port code from. Samaat is GPL-3.0-or-later, so GPL-3.0, MIT, BSD and Apache-2.0 code may be ported with provenance; AGPL-3.0 and GPL-2.0-only code is behaviour only ([CONTRIBUTING.md](../CONTRIBUTING.md#protocol-work-and-licences)).

**How this was checked.** Each licence was read from the project's own `LICENSE` file, in a shallow clone of its repository at the commit listed. Platforms, brands and claims come from the same commit's README unless a cell says otherwise. Anything not read from the project itself is marked *unverified*. Codeberg and Microsoft's sites were not reachable from the session that wrote this, which is why Gadgetbridge is checked through its GitHub mirror.

## Verified projects

| Project | Licence (from its `LICENSE`) | Checked at | Platforms | Language | Brands | Can Samaat port its code? |
|---|---|---|---|---|---|---|
| [PairFree](https://github.com/Omega-Destroyer/PairFree) | GPL-3.0 text; the F-Droid listing says "v3.0 or later" | `ebe0dff` | Android (iOS and Windows "planned") | Kotlin | 12 brands, 618 models listed: SOUNDPEATS, Soundcore, Galaxy Buds, Sony, Bose, JBL, AirPods/Beats, Nothing/CMF, Pixel Buds, QCY, 1MORE, EarFun | Yes, with provenance |
| [BudsLink](https://github.com/maniacx/BudsLink) | GPL-3.0 text (AUR package: GPL-3.0-or-later) | `0eb4d07` | Linux (Flatpak) | JavaScript (GJS), some C | AirPods/Beats, Sony, Galaxy Buds, Pixel Buds, Nothing/CMF, Bose, Redmi/Xiaomi, Sennheiser, Edifier and others (README disclaimer and press coverage) | Yes, with provenance |
| [QuickBuds](https://github.com/spizganed/QuickBuds) | GPL-3.0 text; README: "v3.0 or later". **Now confirmed** (the plan had it as unconfirmed) | `4bc1798` | Android 8+, Windows, Linux | Kotlin, Rust | OnePlus, OPPO, realme: 140 models from HeyMelody's and realme Link's lists, 43 confirmed | Yes, with provenance |
| [Gadgetbridge](https://codeberg.org/Freeyourgadget/Gadgetbridge) | AGPL-3.0 (GitHub mirror's `LICENSE`; a few files GPL-3.0 or MIT) | mirror `a0948ee` (2024-12-22; Codeberg is upstream and unreachable here) | Android | Java | The broadest list. Earbud drivers in the mirror: Galaxy Buds, Sony, Soundcore, Nothing, OPPO, Huawei, Moondrop, Marstek. Watches and bands too | **Behaviour only** (AGPL) |
| [GalaxyBudsClient](https://github.com/timschneeb/GalaxyBudsClient) | GPL-3.0 text; README: "licensed under GPLv3" (no "or later" stated) | `754b6fd` | Windows, macOS, Linux, Android | C# (.NET) | Samsung Galaxy Buds | Yes, with provenance. Record "GPL-3.0" (not "or later") as the original licence; GPL-3.0-only code can go into GPL-3.0-or-later Samaat, but the combined work can then only be distributed under GPL-3.0 |
| [OpenSCQ30](https://github.com/Oppzippy/OpenSCQ30) | GPL-3.0 text | `e75a68b` | Windows, Linux, Android | Rust (Kotlin for Android) | Soundcore | Yes, with provenance (port the logic; it's Rust) |
| [SonyHeadphonesClient](https://github.com/Plutoberth/SonyHeadphonesClient) | MIT | `5620e8e` | Windows, macOS, Linux (archived page) | C++ | Sony (older models) | Yes, keep the MIT notice. **Archived** ("This project is now archived"): check each fork's licence before using it |
| [LibrePods](https://github.com/kavishdevar/librepods) | GPL-3.0 text | `89356c0` | Android, Linux | Kotlin, Rust, C++/QML | AirPods | Yes, with provenance. **Trademark notice:** "The GPL does not grant any rights to use the LibrePods name, logo, or branding". Don't use the name beyond credit |

### Not found

"Earbridge", "OpenPeats" and "B4S" could not be found as open-source earbud projects. Don't benchmark against them.

## Feature comparison

Samaat's column is what the code and the hardware notes support today ([README](../README.md#supported-earbuds), [`docs/STATUS.md`](STATUS.md)). Other columns only claim what their README states; "–" means not checked, not "missing".

| | **Samaat** | PairFree | BudsLink | QuickBuds | Gadgetbridge | GalaxyBudsClient | OpenSCQ30 |
|---|---|---|---|---|---|---|---|
| Windows desktop | ✅ (main app) | Planned | ❌ | ✅ | ❌ | ✅ | ✅ |
| Android | ✅ ([samaat-mobile](https://github.com/MuhammadAbdulMoiz/samaat-mobile)) | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ |
| Linux / macOS | Simulated devices only | ❌ | Linux | Linux | ❌ | Both | Linux |
| Battery per bud and case | ✅ (Jieli, Fast Pair; Windows overall battery for anything else) | ✅ | ✅ *(press coverage)* | ✅ | – | ✅ ("detailed battery statistics") | – |
| Noise control | ✅ Jieli; Bose read only | ✅ | ✅ *(press coverage)* | ✅ | – | ✅ *(unverified)* | ✅ *(unverified)* |
| EQ | ✅ Jieli presets | ✅ | – | ✅ | – | ✅ *(unverified)* | ✅ *(unverified)* |
| **Move earbuds between phone and PC** | ✅ from either side, with rollback | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Share audio to several outputs** | ✅ with per-output delay | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Any paired headset (no vendor protocol) | ✅ battery, connect, share | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Shows untested models as untested | ✅ `verified` flag; "recognised, not yet verified on hardware" | ✅ per-brand "tried on real earbuds" tables | – | ✅ "confirmed" list | – | – | – |
| Unsupported controls shown with a reason | ✅ capability model | – | – | ✅ "shows only the features each model has" (hidden, not explained) | – | – | – |
| Network use | Local network only, for the phone link; no account | No internet permission (README) | – | Daily install count to a Cloudflare Worker (opt-out on Android) | – | – | – |

The ❌ cells in the handoff and audio-sharing rows mean the README describes nothing like it, not that the feature was searched for in the code.

## Where Samaat stands

**Unique strengths**

- **Phone ↔ PC handoff:** move earbuds between the Android phone and the Windows PC from either device, including earbuds that connect to one device at a time; if the other side can't connect them, they go back. None of the projects above does this.
- **Audio sharing:** play one source on several outputs at once, with a delay per output, and "Switch here".
- **The honest capability model:** every control a model lacks is shown greyed out with the reason, and a model is "verified" only with hardware evidence (CONTRIBUTING checklist). PairFree's README admits "most models have not been tried on real earbuds yet"; Samaat keeps that state visible instead of hiding it.
- **Any headset works a little:** battery, connect and sharing through Windows, before any vendor protocol exists.

**The gap: brands.** Samaat speaks three vendor protocols (Jieli RCSP, Google Fast Pair, Bose BMAP read-only) with five models on record. PairFree lists 12 brands, QuickBuds 140 models, Gadgetbridge many more. Brand coverage is the gap to close, and porting GPL/MIT/Apache protocol code is how.

## Porting order

From the plan §3: one brand per PR, each with a decision record and the per-port checklist (plan §4).

| Phase | Brand | Main source (licence, as checked above) | Cross-check | Notes |
|---|---|---|---|---|
| 1 | Samsung Galaxy Buds | GalaxyBudsClient (GPL-3.0) | Gadgetbridge (behaviour) | Biggest market; C#, so the most direct port |
| 2 | Sony, older (WH-1000XM3/XM4, MDR-XB950BT) | SonyHeadphonesClient (MIT) | Gadgetbridge (behaviour) | Archived upstream; check forks' licences |
| 2b | Sony, newer (XM5, WF-C510, ULT900N) | Gadgetbridge, behaviour only; own captures | SonyHeadphonesClient forks (check licences) | Clean-room |
| 3 | Soundcore | OpenSCQ30 (GPL-3.0, Rust) | PairFree (GPL-3.0) | Many affordable models |
| 4 | Bose, JBL, SOUNDPEATS | Own BMAP notes; bosectl (*licence unverified*); PairFree (GPL-3.0) | PairFree's model tables | Often untested upstream: hardware evidence is essential |
| 5 | Nothing, realme/OPPO, Huawei | Gadgetbridge, behaviour only; **QuickBuds (now confirmed GPL-3.0-or-later) for OnePlus/OPPO/realme** | – | QuickBuds' confirmation makes realme/OPPO portable rather than clean-room |
| 6 | AirPods | LibrePods (GPL-3.0; trademarked name) | – | Feasibility spike first: the channel AirPods use is hard to reach on Windows, and LibrePods needs root on Android |

Another project's model list is a lead, not evidence: a ported model stays `"verified": false` until it passes the hardware checklist.

## Sources

- Repositories, read at the commits in the table: [PairFree](https://github.com/Omega-Destroyer/PairFree), [BudsLink](https://github.com/maniacx/BudsLink), [QuickBuds](https://github.com/spizganed/QuickBuds), [Gadgetbridge GitHub mirror](https://github.com/Freeyourgadget/Gadgetbridge), [GalaxyBudsClient](https://github.com/timschneeb/GalaxyBudsClient), [OpenSCQ30](https://github.com/Oppzippy/OpenSCQ30), [SonyHeadphonesClient](https://github.com/Plutoberth/SonyHeadphonesClient), [LibrePods](https://github.com/kavishdevar/librepods).
- PairFree on F-Droid: [io.github.omegadestroyer.pairfree](https://f-droid.org/packages/io.github.omegadestroyer.pairfree/) (licence "GPL-3.0-or-later", "no internet access").
- BudsLink: [AUR budslink-git](https://aur.archlinux.org/packages/budslink-git) (GPL-3.0-or-later), [LinuxLinks](https://www.linuxlinks.com/budslink-monitor-control-bluetooth-earbuds/), [Linuxiac](https://linuxiac.com/budslink-brings-airpods-and-galaxy-buds-controls-to-linux/) (brand list).
- Gadgetbridge's upstream is [Codeberg](https://codeberg.org/Freeyourgadget/Gadgetbridge); its current device list is at [gadgetbridge.org/gadgets](https://gadgetbridge.org/gadgets/) (*not reached from this session*).

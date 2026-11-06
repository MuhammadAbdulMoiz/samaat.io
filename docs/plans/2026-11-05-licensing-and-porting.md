# Plan: licensing for reuse, and porting other open-source drivers

**Date:** 2026-11-05 · **Status:** direction chosen by the owner, not started · **Affects:** `samaat.io` and `samaat-mobile`

## Goal

Grow brand coverage fast by reusing protocol work from other open-source earbud projects (GPL-3.0, MIT and Apache-2.0 licensed) without legal or architectural damage. Brand coverage is Samaat's biggest gap against PairFree, BudsLink, Gadgetbridge and the brand-specific clients. Samaat's unique strengths (phone ↔ PC handoff, audio sharing, the honest capability model) stay as they are.

## 1. Licence: GPL-3.0-or-later, for both repos

The owner wants to use **GPL, MIT and Apache** code. A combination of all three can only be distributed under **GPL-3.0**:

| Source licence | Can go into a GPL-3.0 Samaat? | Can go into an MIT or Apache Samaat? |
|---|---|---|
| GPL-3.0 / GPL-3.0-or-later | ✅ Yes | ❌ No |
| MIT, BSD | ✅ Yes (keep their notice) | ✅ Yes |
| Apache-2.0 | ✅ Yes (GPL-3.0 accepts it; GPL-2.0-only doesn't) | ✅ Yes |
| AGPL-3.0 (Gadgetbridge) | ⚠️ Combinable, but those parts keep AGPL terms. **Behaviour only**, unless Samaat chooses AGPL | ❌ No |
| GPL-2.0-only | ❌ No: behaviour only | ❌ No |
| Vendor apps, SDKs, firmware, decompiled code | ❌ Never | ❌ Never |

**Decision:** license both `samaat.io` and `samaat-mobile` under **GPL-3.0-or-later**. The phone app builds the desktop's shared code through its submodule, so both repos must match. This resolves audit issue 2, which PR #6 left open.

**Before adding the LICENSE file:**
- **Rights:** confirm nobody but the owner (and Claude sessions working for the owner) has contributed code. If anyone has, get their agreement.
- **Microsoft Store:** check its current policy on copyleft licences for the MSIX listing. GPL-3.0 needs the source offered for each build; linking the GitHub tag in the release notes covers that.
- **F-Droid:** GPL-3.0 makes the phone app eligible.

*(This plan is not legal advice; check anything unusual with someone qualified.)*

## 2. Foundations: one PR in each repo

1. **The licence:** `LICENSE` (GPL-3.0 text); a licence line in the README; `SPDX-License-Identifier: GPL-3.0-or-later` in each project's `Directory.Build.props` or in file headers.
2. **CONTRIBUTING porting rule.** Replace "GPL/AGPL code may be read for behaviour only" with:
   - **May be ported, with provenance:** GPL-3.0, GPL-3.0-or-later, MIT, BSD and Apache-2.0 code.
   - **Behaviour only:** AGPL-3.0 and GPL-2.0-only code.
   - **Never:** vendor apps, SDKs, firmware and decompiled code.
   - **No trademarks or assets:** no names, logos, fonts or icons. Keep "works with <model>" wording.
3. **Provenance header convention** for every ported file:
   ```csharp
   // SPDX-License-Identifier: GPL-3.0-or-later
   // Ported from <repo URL> @ <commit>, <path>, Copyright (c) <year> <authors>, <original licence>.
   // Changes: rewritten onto Samaat.Transport (IRfcommChannel/IGattClient) and Samaat.Core features.
   ```
4. **`THIRD_PARTY_NOTICES.md`:** add a "Ported protocol code" section, one entry per source (project, URL, commit, licence, files).
5. **CI check:** fail the build if a file under `src/Samaat.Protocols.*` lacks an SPDX line, or contains "Ported from" with no matching `THIRD_PARTY_NOTICES.md` entry. Make it a test in `ArchitectureTests` style.
6. **`docs/COMPETITIVE-LANDSCAPE.md`:** the comparison, the verified project facts, the source map below, and the positioning.

## 3. Porting order (one brand per PR, each with a decision record)

| Phase | Brand | Main source (licence) | Cross-check | Notes |
|---|---|---|---|---|
| 1 | **Samsung Galaxy Buds** | GalaxyBudsClient (GPL-3.0) | Gadgetbridge (behaviour) | Biggest market; it's a C# desktop app, so the port is the most direct |
| 2 | **Sony, older** (WH-1000XM3/XM4, MDR-XB950BT) | SonyHeadphonesClient (MIT) | Gadgetbridge | Archived upstream, so check the forks' licences before using them |
| 2b | **Sony, newer** (XM5, WF-C510, ULT900N) | Gadgetbridge, behaviour only; your own captures | Forks of SonyHeadphonesClient (check licences) | Clean-room |
| 3 | **Soundcore** | OpenSCQ30 (GPL-3.0, Rust: port the logic) | PairFree | Many affordable models |
| 4 | **Bose, JBL, SOUNDPEATS** | Your own BMAP notes plus bosectl (check its licence); PairFree (GPL-3.0) | PairFree's model tables | Often untested upstream: hardware evidence is essential |
| 5 | **Nothing, realme/OPPO, Huawei** | Gadgetbridge (AGPL), behaviour only; QuickBuds (check its licence) | — | Clean-room unless Samaat moves to AGPL |
| 6 | **AirPods** | LibrePods (GPL-3.0; trademarked name) | — | Run a feasibility spike first: the Bluetooth channel AirPods use is hard to reach on Windows, and LibrePods needs root on Android |

## 4. Checklist for every port

- [ ] A new `src/Samaat.Protocols.<Brand>` project referencing Core and Transport only (`ArchitectureTests` allow-list updated).
- [ ] Only the protocol layer is ported (framing, commands, parsing). No UI, framework or dependency from the source app.
- [ ] Provenance headers, plus a `THIRD_PARTY_NOTICES.md` entry.
- [ ] Registered once in `BuiltInDrivers` (both apps pick it up through the submodule).
- [ ] `devices/<brand>/<model>.json` entries with `"verified": false` and features from the source's model tables.
- [ ] Replay tests from the source's test vectors (where the licence allows) and from our own captures.
- [ ] Features map onto existing Core interfaces. New capabilities follow CONTRIBUTING: Core interface, then simulator, then UI.
- [ ] A decision record: source, commit, licence, what was ported, what was tested, what's left.
- [ ] A model is marked verified only with hardware evidence (CONTRIBUTING checklist). Another project's list is a lead, not evidence.
- [ ] The phone app's submodule pin is bumped after the desktop PR merges.

## 5. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Code with an incompatible licence slips in (AGPL, GPL-2.0-only) | The porting rule, provenance headers, the CI check, and a decision record per port |
| Claiming models nobody has tested (PairFree's weakness) | Keep the `verified` flag and the capability model's "recognised, not yet verified" state |
| Mixing two sources for one protocol | One main source per brand; others for cross-checking only |
| Protocol changes in vendor firmware | Version-aware drivers; tests per firmware; the hardware-test notes |
| The Store and GPL | Check the policy before the first GPL release; link the source for each tag |
| Upstream projects disappear (SonyHeadphonesClient is archived) | Pin the source commit in provenance; our own tests protect the port |

## 6. Giving back

Contribute fixes and protocol notes upstream (the Bose BMAP findings, Windows-side behaviour), and credit every source in the README. Talk to the maintainers: shared test hardware and documentation help everyone.

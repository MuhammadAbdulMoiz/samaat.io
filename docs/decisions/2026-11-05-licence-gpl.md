# Decision: license Samaat under GPL-3.0-or-later

**Date:** 2026-11-05 (work finished 2026-11-06) · **Status:** accepted by the owner; foundations in branch `claude/licence-gpl` · **Applies to:** `samaat.io` and `samaat-mobile` · **Plan:** `docs/plans/2026-11-05-licensing-and-porting.md` §1–2 (on `docs/session-2026-11-05`)

## What was chosen

Both Samaat repos are licensed under **GPL-3.0-or-later**. The phone app builds the desktop's shared code through its `external/samaat.io` submodule, so the two repos must use the same licence. This closes audit issue 2, which PR #6 left open.

## Why

The owner wants to reuse protocol work from other open-source earbud projects (GPL, MIT and Apache-2.0) to close Samaat's biggest gap: brand coverage. A program that combines all three can only be distributed under GPL-3.0:

| Source licence | Into GPL-3.0 Samaat? | Into an MIT or Apache Samaat? |
|---|---|---|
| GPL-3.0 / GPL-3.0-or-later | ✅ Yes | ❌ No |
| MIT, BSD | ✅ Yes (keep their notice) | ✅ Yes |
| Apache-2.0 | ✅ Yes (GPL-3.0 accepts it; GPL-2.0-only doesn't) | ✅ Yes |
| AGPL-3.0 (Gadgetbridge) | ⚠️ Combinable, but those parts keep AGPL terms. **Behaviour only** | ❌ No |
| GPL-2.0-only | ❌ No: behaviour only | ❌ No |
| Vendor apps, SDKs, firmware, decompiled code | ❌ Never | ❌ Never |

"Or later" keeps Samaat compatible with a future GPL version. Code taken from a GPL-3.0-only project (GalaxyBudsClient states "GPLv3" without "or later") can still go in; the combined program is then distributable under GPL-3.0 only, until that code is replaced. That is acceptable: Samaat doesn't plan to move off GPL-3.0.

## Alternatives turned down

- **MIT.** Most permissive and Store-friendly, but it can't take GPL code: GalaxyBudsClient, OpenSCQ30, PairFree, QuickBuds, BudsLink and LibrePods would all be off-limits, leaving only MIT/Apache sources (SonyHeadphonesClient) and clean-room work.
- **Apache-2.0.** Same problem as MIT, plus a patent grant that's welcome but doesn't help reuse.
- **AGPL-3.0.** Would make Gadgetbridge portable, but Samaat has no network service for the AGPL's extra clause to protect, and it would make every downstream use stricter. Gadgetbridge stays behaviour only; revisit only if a brand can't be done any other way.

## Checks before adding the licence

### Contributor rights

`git shortlog -sne --all` over the full history (the clone isn't shallow), plus `Co-Authored-By` trailers and committers:

| Repo | Author | Commits | Who |
|---|---|---|---|
| samaat.io | `abdul.moiz <abdul.moiz@zixel.cn>` | 89 | The owner |
| samaat.io | `Claude <noreply@anthropic.com>` | 50 | Claude sessions working for the owner |
| samaat.io | `MuhammadAbdulMoiz <97826635+…@users.noreply.github.com>` | 5 | The owner (GitHub web merges) |
| samaat.io | Co-authors: `Claude`, `Claude Opus 5.5` | 54 trailers | Claude sessions |
| samaat.io | Committer `GitHub <noreply@github.com>` | 5 | Merge commits made on GitHub |
| samaat-mobile | **not checked**: the repo couldn't be attached to this session (see "What's left") | | |

**Result for samaat.io:** no other human authored code. No bot authored any commit in the history.

**One thing for the owner to confirm:** 89 commits use a company address (`@zixel.cn`). If Samaat was written under an employment or contractor agreement, the employer could hold rights in that work. Confirm Samaat is a personal project outside that agreement (or get the employer's written OK) before the first release. This is a checkbox, not a blocker.

### Microsoft Store and copyleft

The Store's own pages (learn.microsoft.com and Microsoft's CDN) couldn't be reached from this session, so this rests on search excerpts of the official App Developer Agreement and should be read first-hand by the owner:

- **App Developer Agreement** ([v8.11, published 2026-03-17](https://docs.microsoft.com/en-us/legal/windows/agreements/app-developer-agreement); [v8.10, 2025-09-12](https://cdn-dynmedia-1.microsoft.com/is/content/microsoftcorp/microsoft/store/documents/legal/ada/fy26/MS.Store.ADAv8.10.EN.US.pdf)): v8.10 added §4(c), "clarifying and consolidating terms applicable to FOSS Licenses". The excerpt says that if the app uses any FOSS licence, the developer is "solely responsible for compliance with those license terms, including any source code availability requirements". Nothing found forbids GPL apps.
- **History:** the 2011 Windows Store terms explicitly let FOSS-licensed apps' terms override the Store's standard licence terms "to the extent required by the FOSS" ([PCWorld](https://www.pcworld.com/article/472689/windows_store_makes_a_special_allowance_for_open_source.html)). In 2022 Microsoft withdrew a planned ban on charging for open-source apps; the policy now asks that such apps be listed by their creator or someone licensed to ([Windows Central](https://www.windowscentral.com/microsoft/microsoft-removes-restrictions-on-generally-free-and-open-source-apps-in-microsoft-store)).

**Conclusion:** no blocker found. GPL-3.0 needs the Corresponding Source offered for each build: the release notes for each `v*` tag should link the tagged source, and the Store listing should link the repository. Owner to read ADA §4(c) and Store Policies §10 directly before the first Store submission.

### F-Droid

GPL-3.0 makes the phone app eligible for F-Droid (not pursued here).

## What changed (samaat.io, branch `claude/licence-gpl`)

- `LICENSE`: the unmodified GPL-3.0 text (sha256 `3972dc97…6986`). gnu.org and the GitHub licences API were blocked here, so it comes from Ubuntu's `base-files` copy of the FSF text and was compared word for word with the SPDX list's GPL-3.0-only text (`spdx-license-list` 6.12.0 from npm): identical apart from straight vs curly quotes and SPDX's older `why-not-lgpl` URL.
- `Directory.Build.props`: `<PackageLicenseExpression>GPL-3.0-or-later</PackageLicenseExpression>`. No SPDX headers added to existing files.
- Settings → About: "Free software under the GNU GPL v3 or later. No warranty." with links to the licence and the source (GPL-3.0 §0 and §5(d), "Appropriate Legal Notices"). `LegalNoticeTests`.
- `CONTRIBUTING.md`: the porting rule (ported with provenance / behaviour only / never / no trademarks), the provenance header, hardware-evidence rules unchanged.
- `THIRD_PARTY_NOTICES.md`: a "Ported protocol code" table, empty.
- `ProvenanceTests`: every file under `src/Samaat.Protocols.*` that says "Ported from" needs an SPDX line and a source listed in the notices; SPDX identifiers outside the allowed list fail.
- `README.md`: a Licence section and badge.
- `docs/COMPETITIVE-LANDSCAPE.md`: the verified projects and the porting order.

## What changes for contributors

- Contributions are accepted under GPL-3.0-or-later (inbound = outbound; no CLA).
- Code from GPL-3.0, MIT, BSD and Apache-2.0 projects may be ported, with the provenance header and a notices entry; the build checks both.
- AGPL-3.0 and GPL-2.0-only code is read for behaviour only. Vendor code never.
- No names, logos, fonts or icons from other projects or vendors; "works with <model>" wording.

## What's left

- [ ] **samaat-mobile:** the same foundations (LICENSE, metadata, notice in the phone UI, CONTRIBUTING, `THIRD_PARTY_NOTICES.md`, a decision record) and its rights check. Not done: attaching the repo to this session was refused by the session's auto-mode permission check.
- [ ] Owner: confirm rights in the `@zixel.cn` commits (above).
- [ ] Owner: read the Store's ADA §4(c) and Store Policies first-hand; link the tagged source in each release's notes and in the Store listing.
- [ ] `src/Samaat.Protocols.Jieli/JlAuthCipher.cs` predates the provenance header. It's credited in `THIRD_PARTY_NOTICES.md` (MIT). Adding the full header (with commits) is a small follow-up, left out because this PR doesn't touch existing files.
- [ ] `THIRD_PARTY_NOTICES.md` credits NAudio but not the other packages the app bundles: Avalonia (MIT), CommunityToolkit.Mvvm (MIT), the Inter font through `Avalonia.Fonts.Inter` (SIL OFL 1.1) and `System.Security.Cryptography.ProtectedData` (MIT). These licences are from memory, not checked here; verify and list them before the first release.
- [ ] The first port (Galaxy Buds from GalaxyBudsClient), per the plan §3–4.

*(Not legal advice; check anything unusual with someone qualified.)*

# Plan: merging the open PRs and the first release to `master`

**Date:** 2026-11-05 · **Applies to:** `samaat.io` (desktop) and `samaat-mobile` (phone) · **First release:** `v1.0.0`

## State at the time of writing (checked)

| PR | Repo | Branch → target | CI | Mergeable |
|---|---|---|---|---|
| **#6** Audit fixes: issues 2–10 | samaat.io | `claude/audit-fixes` → `feature/ui-redesign` | ✅ Ubuntu and Windows | ✅ Clean |
| **#1** Mobile: shared foundations, design system and quality passes | samaat-mobile | `claude/mobile-improvements` → `main` | ✅ Ubuntu, Windows and **Android (APK builds)** | ✅ Clean |
| (no PR yet) Session record and plans | samaat.io | `docs/session-2026-11-05` | — | ✅ Clean after #6 (checked) |

**Other facts:**
- `master` (`88614a0`) has nothing PR #6 lacks, because #6 merged `master` in. Once #6 is in dev, a release PR from dev into `master` is conflict-free.
- **The release workflow** (`.github/workflows/release.yml`, added by #6):
  - It runs on `v*` tags and refuses a tag that doesn't match `<Version>` in `Directory.Build.props` (`1.0.0`).
  - It builds and tests on Windows, then creates a GitHub release with a win-x64 zip and the unsigned MSIX.
- **The release rule** (CONTRIBUTING, "Branches and releases"): release by a PR from dev into `master`, then tag the merge commit on `master`.

---

## Phase A: merge the open PRs (today or tomorrow)

1. **samaat.io #6**, with **"Create a merge commit"** (not squash or rebase). It contains merges of `master` and of this branch's earlier commit; squashing would flatten them, and the next sync with `master` would conflict again.
2. **samaat-mobile #1**, with **"Create a merge commit"**. It isn't stacked on anything, but a merge commit keeps its 7 step commits for history.
3. **samaat.io: the session record and plans.**
   - Open a PR from `docs/session-2026-11-05` into `feature/ui-redesign` and merge it. It's docs only, and it puts the record, the licensing plan and this plan in dev, where tomorrow's sessions find them.
   - Optional: if you'd rather keep it as a reference branch, skip this step.
4. **samaat-mobile follow-up PR:** a small branch from `main` that:
   - moves `external/samaat.io` to the new `feature/ui-redesign` tip (with #6);
   - replaces the phone's `MobileLog` with the shared `SamaatLog`;
   - honours the shared phone-handoff flag where the phone has the equivalent.

   Merge when CI, including the Android job, is green.
5. **Clean up:**
   - Delete the 5 stale branches: `chore/claude-plugins-skills`, `claude/dashboard-views-plan`, `claude/funny-cerf-0f3kyg`, `claude/phone-handoff`, `feature/remaining-modules`.
   - Turn on "Automatically delete head branches" in both repos.

**After Phase A,** `feature/ui-redesign` holds everything, and `master` is a strict ancestor of it.

---

## Phase B: get ready for v1.0.0 (on dev, before releasing)

These are the decisions and checks a first public release needs. Each code change is a normal PR into dev.

**Blocking: do before tagging**

- [ ] **Licence:**
  - add `LICENSE` with **GPL-3.0-or-later** to both repos, plus a README licence line (`docs/plans/2026-11-05-licensing-and-porting.md` §1–2);
  - check the Microsoft Store's current policy on copyleft licences;
  - confirm nobody else holds rights in the code.

  Shipping a release without a licence means nobody may legally use or redistribute it.
- [ ] **Phone-handoff flag default:** confirm "on in normal builds, off in the Store build" (`AppOptions.PhoneHandoffDefault`). Phone handoff has never run on a real phone, so off in the Store build is the safe default.
- [ ] **Version:** confirm `1.0.0` (it matches the MSIX version the Store expects; the Store only accepts increasing versions). If you'd rather signal "early", say so now: changing it later is harder once the Store has a version.
- [ ] **Smoke test on a real Windows PC with real earbuds,** using the zip from a CI build of dev:
  - **Launch and detection:** the app starts, detects the earbuds, shows battery, and the tray works.
  - **Connect/disconnect:** never run on hardware before. If it fails, ship with it hidden and note it.
  - **One driver model:** a Jieli or Fast Pair model, if you have one.
  - **Store build:** the phone panels show "Coming soon".
- [ ] **Branch protection on `master`:** require a PR and passing CI. No direct pushes; the three commits that caused the drift were direct pushes.

**Not blocking, but worth doing before tagging**

- [ ] The `.claude/` keep-list (`.claude/README.md`).
- [ ] The "Overall" battery label decision.
- [ ] Release notes: `--generate-notes` builds them from PR titles. Optionally prepare a short user-facing summary (what works, which earbuds, what's coming) to paste into the GitHub release and the Store listing.
- [ ] `docs/STATUS.md`: make sure it says 1.0.0 and lists what's verified on hardware.

---

## Phase C: release v1.0.0 to `master`

1. **Freeze dev.** Merge nothing else into `feature/ui-redesign` until the release is tagged.
2. **Check dev:** CI on the tip of `feature/ui-redesign` is green, and `<Version>` in `Directory.Build.props` is `1.0.0`.
3. **Open the release PR:** `feature/ui-redesign` → `master`, titled **"Release 1.0.0"**. The description is the release notes summary plus the Phase B checklist, ticked.
4. **Merge it with "Create a merge commit".** Never squash into `master`: master would stop being an ancestor of dev, and every later release PR would conflict.
5. **Tag the merge commit on `master`:**
   ```bash
   git fetch origin master
   git tag -a v1.0.0 origin/master -m "Samaat 1.0.0"
   git push origin v1.0.0
   ```
6. **Watch the Release workflow** on the Actions tab. It:
   - refuses the tag if it doesn't match `1.0.0`;
   - builds and tests on Windows;
   - creates the GitHub release "Samaat 1.0.0" with `Samaat-1.0.0-win-x64.zip` and, if the runner can pack it, `Samaat_1.0.0.0_x64.msix` (unsigned).

   If it fails, fix on dev. Then either delete the tag (`git push origin :refs/tags/v1.0.0`) and re-tag after the fix reaches `master` through another release PR, or release `v1.0.1`.
7. **Check the release:** download the zip on Windows and run it. Check that the notes and assets look right.
8. **Microsoft Store (manual):** in Partner Center, upload the MSIX from the release (or build it locally with `packaging/msix/build-msix.ps1`; Store builds use `-p:SamaatStoreBuild=true`, which turns phone handoff off). The Store signs it. The privacy policy URL is `PRIVACY.md` on `master`.

---

## Phase D: the phone app's matching release

`samaat-mobile` has only `main`; there's no dev/master split. For a release:

1. **Point the submodule at the desktop release:** move `external/samaat.io` to the `v1.0.0` commit, so the released phone app is built from released desktop code. Consider changing `.gitmodules` `branch =` to follow releases, or keep following dev for development and pin to tags for releases only (recommended).
2. **Set the Android app version** (`ApplicationDisplayVersion`, and `ApplicationVersion` as an increasing integer, in `src/Samaat.Android/Samaat.Android.csproj`). Merge by PR into `main`.
3. **Tag `main`** as `v1.0.0`. There's no release workflow in samaat-mobile yet; until there is, take the APK from the CI run's "samaat-android-apk" artifact. Follow-up: add a tag-triggered release workflow like the desktop's.
4. **Distribution:**
   - F-Droid needs a FOSS licence, which GPL-3.0 satisfies.
   - Google Play needs a signing key and a listing.
   - Phone handoff has never run on a real phone; test it before advertising it.
   - There's no iPhone app: iOS doesn't let an app connect or disconnect Bluetooth headphones, so the store is Google Play (and F-Droid optionally), not the App Store.

### Rule: the phone app is on Google Play before the desktop app turns phone handoff on

Owner decision, 2026-11-05. The desktop Store build ships v1.0.0 with phone handoff **off** (`AppOptions.PhoneHandoffDefault`, `SAMAAT_STORE`). It's switched on only once people can actually install the phone app:

1. **Phone app on Google Play.**
   - Add a tag-triggered release workflow to samaat-mobile that builds a signed `.aab` (and an `.apk` for F-Droid and sideloading).
   - Create the upload key and turn on Play App Signing. Keep the key in GitHub secrets, never in the repo.
   - Write the Play listing, the data-safety form (no account, no server, local network only) and the privacy policy link.
   - Release to the internal test track, check handoff on a real phone, then go to production.
2. **QR code in the desktop app to get the phone app** (its own feature branch from dev, test-first, with a decision record):
   - Show it in Settings → Phone and on the Links page's "Your phone" card, next to the pairing flow.
   - Generate the QR code locally (a small MIT or Apache QR library, or a hand-written encoder). No web request.
   - The store URL lives in one place (config or a constant in `Samaat.Handoff`), and the link text is shown under the code for people who can't scan.
   - Headless UI tests: the code renders in both themes, it has an accessible name, and it meets the contrast and hit-target checks. The QR's dark-on-light colours become tokens in `Tokens.axaml`.
3. **Turn handoff on in the Store build**, and release the desktop app as `v1.1.0`.

Until step 1 is done, the QR code and the "Your phone" card stay hidden in the Store build.

---

## After the release

- **Development continues on dev,** with feature branches, PRs into dev, a decision record each, then the next release PR (`v1.1.0`, …).
- **Hotfixes:**
  1. Branch `hotfix/<name>` from `master`.
  2. PR into `master` (merge commit).
  3. Bump the patch version, e.g. `1.0.1`, and tag it.
  4. **Merge `master` back into dev** by PR, so dev never lacks a fix.
- **Optional:** rename `feature/ui-redesign` to `dev` (GitHub → Branches → ✏️). Then update `samaat-mobile/.gitmodules` (`branch = dev`) and the CONTRIBUTING mentions; CI already lists `dev`.
- **Next after release:** the licensing foundations PRs, then the first protocol port (Galaxy Buds), per the licensing and porting plan.

## Merge methods at a glance

| PR | Method | Why |
|---|---|---|
| Feature or fix → dev | Squash or merge commit | Either is fine when nothing is stacked on the branch |
| A PR that another PR is stacked on, or that contains merges (#6) | **Merge commit** | Keeps commit identities, so the stacked or merged history doesn't conflict |
| Release dev → `master` | **Merge commit, always** | Keeps `master` an ancestor of dev; the tag goes on this merge commit |
| Hotfix → `master`, then `master` → dev | **Merge commit** | Same reason |

# Session record: 2026-11-05

A full record of one Claude Code cloud session on Samaat (`samaat.io`, plus a first look at `samaat-mobile`): what was done, what was decided and why, what is open, and how to pick it up again in a new session. **Read this first when resuming.**

- **Session:** `https://claude.ai/code/session_01X2PKY6bSJk1Jq7HpNNXMem`
- **Branch holding this record:** `docs/session-2026-11-05`, from `feature/ui-redesign`
- **State as of writing:**
  - `feature/ui-redesign` is at `2fe441b` (PR #3 merged); `master` is at `88614a0`.
  - PRs #2, #4 and #5 are open.

---

## 1. Where things stand

### Pull requests (samaat.io, all into `feature/ui-redesign`)

| PR | Branch | State | What it is | Decision record |
|---|---|---|---|---|
| #1 | `claude/redesign-improvements` (deleted) | **Merged** 13:47 UTC | ECC; design tokens beyond colour (`Metrics.axaml`); `DesignSystemTests`; one driver list (`Samaat.Drivers`); `ArchitectureTests`; `BuiltInDriversTests`; `CommandRunner` cancels on timeout; ARCHITECTURE, README and FEATURE-IDEAS rewritten | `docs/decisions/2026-11-05-pr1-tokens-layering-drivers.md` (arrived with #3) |
| #3 | `claude/branching-workflow` (deleted) | **Merged** 15:21 UTC | CI runs on pushes to `master`, `feature/ui-redesign` and `dev`; "Branches and releases" in CONTRIBUTING | `docs/decisions/2026-11-05-pr3-branching-workflow.md` |
| #2 | `claude/unslop-polish` | Open | Unslop polish: neutral selection, plain icons instead of sparkles, 600-weight headings, captions without full stops, 4 px spacing | `docs/decisions/2026-11-05-pr2-unslop-polish.md` (on the branch) |
| #4 | `claude/ui-quality` | Open, **stacked on #2** | Worst-case simulator data and stress renders; fixes for what broke; motion review fixes; dimensional drawing of row, tile and option | `docs/decisions/2026-11-05-pr4-ui-quality.md` (on the branch) |
| #5 | `claude/fix-pairing-race` | Open | Pair one phone at a time (High-severity race found by the audit) | `docs/decisions/2026-11-05-pairing-race.md` (on the branch) |

### Merge order (all orders simulated with `git merge-tree`: no conflicts)

1. **#5**: any merge method.
2. **#2**: **"Create a merge commit"**.
3. **#4**: **"Create a merge commit"**. After #2 it shows its own 4 commits.

**Do not squash or rebase #2.** #4 contains #2's commit; giving it a new identity makes #4 conflict in `docs/design/ui-audit.md` and about 50 screenshot PNGs (tested).

### Branches to delete (verified fully merged into both `master` and `feature/ui-redesign`)

The session's git proxy refused to delete branches it hadn't created (HTTP 403), so these need the owner:

```bash
git push origin --delete chore/claude-plugins-skills claude/dashboard-views-plan claude/funny-cerf-0f3kyg claude/phone-handoff feature/remaining-modules
```

After #2, #4 and #5 merge, also delete `claude/unslop-polish`, `claude/ui-quality` and `claude/fix-pairing-race`, or turn on automatic deletion (below). This record's branch can go once it's merged or no longer needed.

### Repository settings only the owner can change (GitHub → Settings)

- **General → Default branch:** `feature/ui-redesign`. New PRs then target dev; #2 and #4 were first opened against `master` by default and picked up wrong titles.
- **General → Automatically delete head branches:** on.
- **Branches → protection rules** on `master` and `feature/ui-redesign`: require a PR and passing CI.
- **Later:** rename `feature/ui-redesign` to `dev` (Branches → ✏️). Open PRs and the default branch move with it; CI already lists `dev`.

---

## 2. What was done, in order

1. **ECC installed.**
   - The `ecc@ecc` plugin is enabled in `.claude/settings.json`, so every cloud session loads it.
   - The `common` and `csharp` rule packs are vendored in `.claude/rules/ecc/`.
   - Where ECC conflicts with this repo, the repo wins: commits keep `Co-Authored-By`.
2. **Design tokens (PR #1).**
   - `Samaat.UI/Styles/Metrics.axaml` holds radii, the type ramp, hit targets, padding, shadows and the accent swatches.
   - Every style and view uses the tokens.
   - `DesignSystemTests` enforce this.
   - All 58 screenshots were pixel-identical before and after.
3. **Backend architecture (PR #1).**
   - `Samaat.Drivers` replaced three hand-written driver lists.
   - `ArchitectureTests` enforce the per-layer allow-list.
   - The catalog/driver consistency test was added.
   - `CommandRunner` now cancels the command's token on timeout (test-first).
   - `ARCHITECTURE.md` rewritten; README improved; `docs/FEATURE-IDEAS.md` (18 ideas) added.
4. **Branch model (PR #3).** `master` for releases, dev for integration, short-lived work branches, hotfixes from `master`. CI runs on dev pushes.
5. **Unslop polish (PR #2).** Audit result: light slop, so polish mode. Logo glow kept (the brand default).
6. **break-ui (PR #4).**
   - `SimulatorScenario` (Demo, WorstCase, Empty), picked with `--simulate=worst` or `--simulate=empty`.
   - `WorstCaseRenderTests`: 87 frames in both themes and two widths.
   - **Fixes:**
     - one toast at a time with "+N more";
     - no "Updated" time or source until a level arrives;
     - the caption names every piece without a level;
     - tooltips on truncated names.
   - **Reverted:** a two-line cap on the rail title, because Avalonia drops the overflow without an ellipsis.
7. **review-animations (PR #4).**
   - Tabs no longer cross-fade.
   - The battery ring no longer glides between devices.
   - A strong ease-out spline for the value glides.
   - `Gauges.axaml` loads last; its reduce-motion rule had been silently overridden.
   - The busy bar holds still under Reduce motion.
   - Press feedback added.
8. **dimensional-drawing (PR #4).** `docs/design/drawings/list-row-tile-option.html` plus a PNG, measured from the rendered visual tree.
9. **A flaky test fixed, not skipped:** `RailShowsEachPartWithItsLevel` used the real clock; it now uses a fake one.
10. **Repository audit,** run in a separate session (`session_01X2PKY5Vzd1NvxaL4VoAnQr`). The report `docs/AUDIT-2026-11-05.md` is **uncommitted in that session's working copy**. Summary in §4.
11. **Pairing race fixed (PR #5).** One pairing slot, taken under the lock before the handshake. Three new tests; the race test failed 3 of 3 runs before the fix.
12. **samaat-mobile surveyed.** Findings and plan in §6. No changes were made there.

### Skills and plugins

- **The user's skill repos** (`MuhammadAbdulMoiz/skills`, `MuhammadAbdulMoiz/dimensional-drawing`): all 18 skills are already in `.claude/skills/`, byte-identical.
- **`.claude/skills/`** holds 50 skills, including 29 Lark/Feishu ones that don't relate to this app.
- **`.claude/settings.json`** enables 7 plugins. Only ECC was installed in the cloud container.
- **Ponytail:** installing it was blocked by the auto-mode safety check ("code from external"), so it was never installed or audited.
- **`/animate-expo`:** not applicable (no Expo or React Native code).
- **`/review-animations`:** must be typed by the user (`disable-model-invocation`).

---

## 3. Decisions and the rules behind them (index)

The full reasoning is in each `docs/decisions/` file. The key ones:

| Decision | Why | Where |
|---|---|---|
| Tokens live in `Samaat.UI` (shared with the phone); `Artwork.axaml` merges `Metrics.axaml` itself | One copy for both apps; the phone keeps working | PR #1 |
| Keep `Samaat.Platform.Windows → Samaat.Handoff` | A platform implementing a module's contract matches Android; moving `ISecretProtector` would break samaat-mobile | PR #1 |
| Keep `DeviceCatalog` and `DeviceProfileDb` separate for now | Larger change; listed as debt | PR #1 |
| `feature/ui-redesign` acts as dev; no `staging` branch | A rename can come later; a release PR is enough review (YAGNI) | PR #3 |
| Keep the logo's glow | Real brand mark; no choice made by the user | PR #2 |
| Worst-case data comes in through the simulator, and values are realistic | break-ui rule: change the data, not the component; nonsense data proves nothing | PR #4 |
| Rail title wraps rather than clamping | Avalonia `MaxLines` cuts without an ellipsis, so a cut name looks complete | PR #4 |
| Screenshots not regenerated after the motion fixes | The diffs were only real-clock drift in the demo simulator | PR #4 |
| One pairing slot, reserved before the handshake; refusals awaited | Fixes the race; a queue would show unexpected prompts | PR #5 |

**Rules applied throughout:**
- `CONTRIBUTING.md`: test-first, warnings as errors, never hide an unsupported feature.
- The ECC common and C# rules.
- Each skill's own standards: unslop (CC BY 4.0, Damir Sokolovsky); break-ui and review-animations (Emil Kowalski); dimensional-drawing.
- Pixel comparison for refactors.
- A full test run before every push.
- A decision record in every PR.

---

## 4. Audit summary (separate session; full report uncommitted there)

**Overall: 6.5 / 10.**
- **Builds:** dev 966 tests and `claude/ui-quality` 1,005 tests, all passing with 0 warnings, and no flaky tests on reruns.

| Area | Score | Area | Score |
|---|---|---|---|
| Architecture | 9 | Documentation | 6 |
| Backend quality | 7 | Build, CI, release | 5 |
| Security and privacy | 6 | Product completeness | 5 |
| UI/UX, design system | 8 | Maintainability | 7 |
| Tests | 7 | Repo hygiene | 4 |

**Top issues and their status:**

| # | Severity | Issue | Status |
|---|---|---|---|
| 1 | High | Pairing race: several phones pairing at once each get a prompt | **Fixed in #5** |
| 2 | High | No LICENSE | Open: owner decision |
| 3 | High | `master` and dev drift: Store packaging, privacy policy and a "coming soon" phone screen pushed straight to `master` | Open: needs the phone-feature decision (§5) |
| 4 | High | Windows platform code has no tests; PC connect has never run on hardware | Open |
| 5 | Medium | About 60 errors caught and dropped with no logging; 5 `async void` handlers | Open |
| 6 | Medium | Plaintext secrets outside Windows; computer name broadcast while any phone is paired; no handshake cap; PRIVACY.md silent on local-network traffic | Open: agreed to handle later |
| 7 | Medium | Stale docs: STATUS says 732 tests, TRY-IT names a merged branch, `traces/` doesn't exist, PLAN lists an old stack | Open |
| 8 | Medium | `.claude/` noise (29 Lark skills, about 8 MB of PNGs, 7 third-party plugins enabled for everyone) | Open: owner decision |
| 9 | Medium | No release process: no version, tags, release workflow, Dependabot or coverage report | Open |
| 10 | Low | Screenshot churn from the real-clock demo simulator; git history 64 MB | Open |

**What the audit said not to change:**
- the layering, enforced by a test;
- the capability model;
- confirmed writes;
- the pairing crypto design;
- protocol parsers tested against captured bytes;
- the habit of requiring hardware evidence.

---

## 5. What remains (priority order)

### Owner decisions

1. Merge #5, then #2, then #4 (§1).
2. Delete the 5 merged branches, and change the repository settings (§1).
3. **Licence:** choose one and add it.
4. **Phone handoff in the first release?** Recommended: a feature flag that keeps it off for releases until it has run on a real phone. This resolves the `master`/dev drift instead of editing screens per branch. *(Flag added in the audit fixes; confirm its default.)*
5. **`.claude/` cleanup:** which skills and plugins stay. Personal ones belong in the user folder.
6. **"Overall" battery label:** it shows the lowest bud, e.g. 1 % beside a bud at 100 %. Rename it to "lowest bud"?

### Engineering (each on its own branch from dev, with a PR and a decision record)

1. **Phone-link hardening (audit issue 6), agreed for later:**
   - re-check that pairing is still open after the handshake (a pairing closed mid-handshake still raises a prompt);
   - cap simultaneous handshakes;
   - broadcast the beacon only while pairing is open;
   - restrict permissions on the plaintext secrets file outside Windows;
   - mention local-network traffic in PRIVACY.md.
2. **Logging:** a small log file, included in "Copy report"; replace silent catches.
3. **Release groundwork:** a version number, a release workflow on tags (app and MSIX), Dependabot, a coverage report.
4. **`master`/dev sync,** after decision 4:
   - do it on its own branch;
   - conflicts are expected in `ConnectionsPage.axaml` and `SettingsPage.axaml` (the phone panels) and `AppOptions.cs` (keep both sides);
   - then the first release PR into `master`, with a tag.
5. **Docs refresh:** STATUS, TRY-IT, CONTRIBUTING (`traces/`), PLAN (stack superseded by ARCHITECTURE).
6. **Deterministic screenshots:** give `RendersScreenshots` a fake clock; consider committing only the README images.
7. **Windows platform tests:** a Windows-only test project on the Windows CI runner, plus the hardware checklist on real earbuds.
8. **Debt from ARCHITECTURE.md:**
   - merge the two `devices/` parsers;
   - a per-OS platform-services object instead of the `#if` checks in `AppOptions`;
   - split `DeviceViewModel`, `PhoneLinkHost` and `PhoneLinkClient`.
9. **Commit the audit report:** from session `session_01X2PKY5Vzd1NvxaL4VoAnQr`, ask it to commit `docs/AUDIT-2026-11-05.md` to a `docs/audit-2026-11-05` branch with a PR. Or recreate it from §4.

---

## 6. samaat-mobile: findings and plan

**Repo:** `MuhammadAbdulMoiz/samaat-mobile`, default branch `main` (`6c29407` when surveyed).
**Projects:** `Samaat.Mobile` (Avalonia UI), `Samaat.Mobile.Preview` (runs in a window on a laptop), `Samaat.Platform.Android`, `Samaat.Android`, and `tests/Samaat.Mobile.Tests`.
**Shared code:** comes from `samaat.io` through the submodule `external/samaat.io` (tracks `feature/ui-redesign`).

**Baseline at the current pin:** `dotnet build Samaat.Mobile.slnx` passes with 0 warnings; `dotnet test` passes all 18 tests.

### Findings

- **The submodule is pinned at `e264134`,** from before PR #1. The phone has none of the shared improvements: tokens, `Samaat.Drivers`, the `CommandRunner` fix, the motion fixes, the pairing fix.
- **A fourth copy of the driver list:** `src/Samaat.Android/AndroidHost.cs:44-46` registers only Jieli and Fast Pair. `Samaat.Drivers.BuiltInDrivers` exists to replace exactly this, and would also give the phone Bose.
- **The shared solution list** in `Samaat.Mobile*.slnx` lacks `Samaat.Drivers` and `Samaat.Protocols.Bose`.
- **No `.claude/` setup,** no ECC, no architecture test, no design-system or contrast tests, no decision records.
- **`src/Samaat.Mobile/Styles/Mobile.axaml`** is 329 lines of styles, not yet checked against the shared tokens.
- **`src/Samaat.Mobile/ViewModels/DeviceItemViewModel.cs`** is 696 lines, the largest file.
- **Android code** (`Samaat.Platform.Android`, `Samaat.Android`) can't be run in a cloud container: no Android SDK and no phone. Only the laptop preview and the tests run there.

### Plan

Start **after** #5, #2 and #4 are merged into `feature/ui-redesign`. Use one branch from `main`, with a PR and a decision record.

1. **Foundations**
   - Move the submodule pin to the merged `feature/ui-redesign`.
   - Add `Samaat.Drivers` and `Samaat.Protocols.Bose` to the solutions.
   - Replace `AndroidHost.cs:44-46` with `BuiltInDrivers.CreateRegistry(devices)`.
   - Enable ECC like the desktop (`.claude/settings.json`, `.claude/rules/ecc/`).
   - Add an architecture test with the phone's layer rules (Mobile → shared only; Platform.Android implements contracts; nothing references Android projects except `Samaat.Android`).
2. **Design system**
   - Move `Mobile.axaml` and the views (`MainView`, `DeviceView`, `ComputerView`) onto `Tokens.axaml` and `Metrics.axaml`.
   - Port `DesignSystemTests` and `ContrastTests` to `Samaat.Mobile.Tests`.
   - Check pixel parity of the existing mobile screenshots for pure refactors.
3. **Unslop pass** on the devices list, device page and computer pairing. Keep the desktop's direction ("Listening room").
4. **Stress test (break-ui)** with `SimulatorScenario.WorstCase` and `Empty` (from #4) on the phone screens, then fix what breaks. The phone is narrower, so long names and toasts matter more.
5. **Motion review:** the user types `/review-animations`. Check that the shared `BatteryRing` fix (no glide between devices) holds on the phone.
6. **Backend cleanup:** split `DeviceItemViewModel` (696 lines) along its responsibilities, test-first.
7. **Docs:** README, TRY-IT, a decision record, and update the phone section of the desktop's ARCHITECTURE.md if anything changes.

---

## Audit fixes (issues 2 to 10)

Done on `claude/audit-fixes` (from `feature/ui-redesign` after #5, #2 and #4 merged), one pull request into `feature/ui-redesign`. Full reasoning: [docs/decisions/2026-11-05-audit-fixes.md](../decisions/2026-11-05-audit-fixes.md). Tests: 1,008 → 1,063, 0 warnings.

**Done:**
- **3, drift:** the phone-handoff flag (`AppOptions.PhoneHandoff`, off in the Store build), then `master` merged in; `master` has nothing dev lacks.
- **4, Windows tests:** `tests/Samaat.Platform.Windows.Tests` (25 tests) on the Windows CI job.
- **5, silent failures:** `SamaatLog` in Core, a 256 KB rolling log file, the log tail in Copy report, the six `async void` handlers guarded, 46 handled failures logged.
- **6, phone link:** pairing re-checked after the handshake, at most 4 pending handshakes, beacon only while pairing, `phones.json` 0600 outside Windows, PRIVACY.md updated.
- **7, docs:** STATUS, TRY-IT, CONTRIBUTING (recorded bytes), PLAN, ARCHITECTURE.
- **9, release:** version 1.0.0, release workflow on `v*` tags, Dependabot, coverage in CI.
- **10, screenshots:** a fixed clock; regenerated once, and reruns change nothing.

**Left for the owner:**
- **2, licence:** choose one (options in the PR and the decision record).
- **3, flag default:** confirm "off in the Store build".
- **8, `.claude/`:** choose the keep-list in `.claude/README.md`.
- The first release tag; the Windows hardware checklist; the phone handoff on a real phone.

---

## 7. How to resume in a new session (local or cloud)

**Prerequisites:**
- The .NET 10 SDK. In a cloud container, Microsoft's download hosts are blocked; use `apt-get install -y dotnet-sdk-10.0` (gives 10.0.114).
- ECC's GateGuard hook asks you to state facts before first edits and destructive commands. `ECC_GATEGUARD=off` disables it.
- The stop hook asks to commit and push uncommitted changes. A read-only task's explicit instruction overrides it.

```bash
# desktop
git clone https://github.com/MuhammadAbdulMoiz/samaat.io.git && cd samaat.io
git switch feature/ui-redesign
dotnet build Samaat.slnx && dotnet test Samaat.slnx      # 966 tests at 2fe441b; 1,005 on claude/ui-quality; 969 on claude/fix-pairing-race

# phone
git clone --recursive https://github.com/MuhammadAbdulMoiz/samaat-mobile.git && cd samaat-mobile
dotnet build Samaat.Mobile.slnx && dotnet test Samaat.Mobile.slnx   # 18 tests at the current pin
dotnet run --project src/Samaat.Mobile.Preview                      # phone UI in a window
```

**Useful flags and environment variables:**
- `--simulate=worst` and `--simulate=empty`: the stress data sets (after #4 merges).
- `SAMAAT_SCREENSHOTS=<dir>`: save the docs screenshots.
- `SAMAAT_STRESS_SCREENSHOTS=<dir>`: save the worst-case frames.

**Working rules carried forward:**
- One branch per change, from dev, with a PR into `feature/ui-redesign`.
- A decision record in `docs/decisions/` for every PR.
- Test-first.
- A full test run before pushing.
- "Create a merge commit" whenever a PR is stacked on another.

---

## End of day: where everything stands (2026-11-05, evening)

**Start here tomorrow.** This section supersedes the PR and branch status in §1.

### What finished since the morning

- **Desktop PRs #2, #4 and #5 merged** into `feature/ui-redesign`, which is now at `473a430`.
- **Desktop PR #6, "Audit fixes: issues 2–10"** (`claude/audit-fixes`), opened by session `session_01X2PKY3hiFnxUGWkJ1UA4D7`.
  - Done: issues 3–7, 9 and 10. Issues 2 (licence) and 8 (`.claude/`) are prepared and waiting on the owner.
  - **1,038 tests pass** (Core 310, Protocols 215, App 406, Handoff 107), plus 25 Windows platform tests that CI runs on `windows-latest`.
  - The PR **already merged `master`** (the drift is gone) **and an earlier version of this branch**.
  - Decision record: `docs/decisions/2026-11-05-audit-fixes.md`.
- **Mobile PR #1, "Mobile: shared foundations, design system and quality passes"** (`samaat-mobile`, `claude/mobile-improvements` → `main`), opened by session `session_01X2PKY5L5KEwvM7pq2gchR2`.
  - 7 steps, one commit each; **18 → 163 tests**.
  - The submodule pin moves to `473a430` (it doesn't include #6).
  - Uses `BuiltInDrivers`, so the phone gains Bose.
  - Adds phone design tokens (`Styles/Metrics.Mobile.axaml`: 44 px touch targets, body text 15), an unslop polish, a stress test that fixed 7 breaks, Android "Remove animations" support, the `DeviceItemViewModel` split, and `MobileLog`.
  - Android projects were **not compiled** (no Android SDK in the container); the Android CI job is the first real check.
  - Decision record: `docs/decisions/2026-11-05-mobile-improvements.md` (in `samaat-mobile`).
- **Licensing direction chosen by the owner:** reuse GPL, MIT and Apache code, which means **GPL-3.0-or-later for both repos**. The plan is in [`docs/plans/2026-11-05-licensing-and-porting.md`](../plans/2026-11-05-licensing-and-porting.md), on this branch.
- **Competitive research:**
  - Verified real: PairFree, BudsLink, QuickBuds, Gadgetbridge, GalaxyBudsClient, OpenSCQ30, SonyHeadphonesClient, LibrePods.
  - **Not found** (don't benchmark against them): "Earbridge", "OpenPeats", "B4S".
  - The comparison and porting order are in the plan above.

### Merge order now

1. **samaat.io PR #6** into `feature/ui-redesign`, with **"Create a merge commit"**. It contains merges of `master` and of this branch's earlier commit; a merge commit keeps those intact.
2. **samaat-mobile PR #1** into `main`, after its Android CI job passes.
3. **This branch** (`docs/session-2026-11-05`), after #6: open a PR into `feature/ui-redesign` if you want the record and the plan in dev. It was built on #6's version of the record, so it merges cleanly after #6.
4. **Then a small mobile follow-up:** move the phone's submodule pin to include #6. It brings shared logging (`SamaatLog`, to replace `MobileLog`), the phone-handoff flag and the hardening.

### Branches

| Repo | Branch | State | Next |
|---|---|---|---|
| samaat.io | `feature/ui-redesign` | dev, at `473a430` | Receives #6 |
| samaat.io | `master` | releases, at `88614a0` | Nothing pending once #6 merges (#6 contains `master`). The first release comes from dev with tag `v1.0.0` |
| samaat.io | `claude/audit-fixes` | PR #6, open | Merge (merge commit) |
| samaat.io | `docs/session-2026-11-05` | This record and the licensing plan; not in dev | Merge after #6, or keep as reference |
| samaat.io | `chore/claude-plugins-skills`, `claude/dashboard-views-plan`, `claude/funny-cerf-0f3kyg`, `claude/phone-handoff`, `feature/remaining-modules` | Fully merged, stale | **Delete** (owner; the session proxy refused) |
| samaat-mobile | `main` | at `6c29407` | Receives mobile #1 |
| samaat-mobile | `claude/mobile-improvements` | mobile PR #1, open | Merge after Android CI |

Merged PR branches (`claude/redesign-improvements`, `claude/branching-workflow`, `claude/unslop-polish`, `claude/ui-quality`, `claude/fix-pairing-race`) are already deleted on GitHub.

### Documents to read

| Doc | Where | What |
|---|---|---|
| This record | `docs/sessions/2026-11-05-session-record.md` on `docs/session-2026-11-05` | Everything; start here |
| Merge and release plan | `docs/plans/2026-11-05-merge-and-release.md` on `docs/session-2026-11-05` | Merging the open PRs, preparing v1.0.0, releasing to `master` with a tag, the phone app's release, hotfixes |
| Launch checklist (tickable) | https://claude.ai/artifact/JXH3HjdC7FiqSXZJMqHRa4 (private to the owner; ticks save themselves) | Every step from merging the PRs to Google Play, the QR code and turning handoff on |
| Licensing and porting plan | `docs/plans/2026-11-05-licensing-and-porting.md` on `docs/session-2026-11-05` | GPL-3.0-or-later, porting rules, brand order, per-port checklist |
| Decision records | `docs/decisions/` on `feature/ui-redesign` (PR #1–#5, pairing race) and on `claude/audit-fixes` (audit fixes) | Why each change was made |
| Mobile decision record | `samaat-mobile`: `docs/decisions/2026-11-05-mobile-improvements.md` on `claude/mobile-improvements` | The phone work |
| `.claude/` keep-list proposal | `.claude/README.md` on `claude/audit-fixes` | Owner decision 8 |
| Architecture, UI notes, ideas | `docs/ARCHITECTURE.md`, `docs/design/ui-audit.md`, `docs/FEATURE-IDEAS.md` | Reference |
| Audit report | Uncommitted in session `session_01X2PKY5Vzd1NvxaL4VoAnQr` | Summary in §4; ask that session to commit it, or skip |

### Owner decisions (consolidated)

- [ ] **Licence:** add `LICENSE` with GPL-3.0-or-later to both repos (direction chosen; follow the plan's §1 checks: contributor rights, Store policy).
- [ ] **Phone-handoff flag default:** on in normal builds, off in the Store build (`AppOptions.PhoneHandoffDefault`). Decided: it stays off in the Store build until the phone app is on Google Play and the desktop app shows a QR code to download it (merge and release plan, Phase D rule). Then it's switched on in `v1.1.0`.
- [ ] **Version:** `1.0.0` (matches the MSIX script). Confirm before tagging `v1.0.0`.
- [ ] **`.claude/` keep-list** from `.claude/README.md` (on `claude/audit-fixes`).
- [ ] **ECC plugin in samaat-mobile:** add it to `samaat-mobile/.claude/settings.json` yourself; auto mode blocked the session. The JSON is in the mobile decision record.
- [ ] **"Overall" battery label** (desktop and phone): rename to "lowest bud"?
- [ ] **Delete the 5 stale branches;** turn on "Automatically delete head branches"; set the default branch to `feature/ui-redesign`; protect `master` and dev.
- [ ] **On real hardware:** the Windows checklist, phone handoff on a real phone, Bose on the phone, "Remove animations" on Android.

### Follow-ups for the desktop repo (from mobile PR #1)

- `src/Samaat.Handoff/Moving/HandoffCoordinator.cs:102`, `:135`: "… moved to …." messages end with a full stop.
- `src/Samaat.App/Features/Overview/DeviceViewModel.cs:505-519`: the battery caption rule is duplicated in the phone's `BatterySummary`. Move it to shared presentation code.
- `src/Samaat.Simulator/WorstCaseDevices.cs:55`, `:63`, `:70`: these worst-case devices have no Bluetooth address, so the phone can't test moves with them.
- `src/Samaat.UI/Styles/Artwork.axaml:75-83`: no shared reduce-motion rule for the ring glide.
- `src/Samaat.UI/Styles/Icons.axaml:25`: `Icon.Sparkle` is now unused by both apps.
- Replace the phone's `MobileLog` with the shared `SamaatLog` once #6 merges.

### Tomorrow, in order

*(The full merge and release steps are in [`docs/plans/2026-11-05-merge-and-release.md`](../plans/2026-11-05-merge-and-release.md). CI is already green on #6 and on mobile #1, including the Android job.)*

1. Check CI on PR #6 (Ubuntu and Windows, including the Windows test project and the coverage summary) and on mobile PR #1 (the Android job).
2. Merge #6 (merge commit), then mobile #1.
3. Make the owner decisions above. The licence comes first, because the porting plan depends on it.
4. Open a branch for the licensing foundations (plan §2), one PR per repo.
5. Bump the mobile submodule to include #6; replace `MobileLog` with `SamaatLog`.
6. Start the first port: Samsung Galaxy Buds from GalaxyBudsClient (plan §3, phase 1).
7. Do the desktop follow-ups above, in a small PR.
8. Optionally, tag `v1.0.0` once the decisions are made. The release workflow then builds the zip and the MSIX.
9. After v1.0.0: put the phone app on Google Play, add the QR code to the desktop app, then turn phone handoff on in the Store build and release `v1.1.0` (plan, Phase D rule).

## Licence work (2026-11-06)

Done by session `session_01X2PKY3q149pLosj8ZNQWeT`, following the licensing plan's §1–2.

- **Desktop: [PR #7](https://github.com/MuhammadAbdulMoiz/samaat.io/pull/7), "Licence: GPL-3.0-or-later and porting rules"** (`claude/licence-gpl` → `feature/ui-redesign`).
  - Stacked on #6. Merge it **after #6**, with "Create a merge commit".
  - Adds `LICENSE` (the unmodified GPL-3.0 text), `PackageLicenseExpression`, and the legal notice in Settings → About.
  - Adds the CONTRIBUTING porting rule, the "Ported protocol code" table in `THIRD_PARTY_NOTICES.md`, `ProvenanceTests`, a README Licence section, `docs/COMPETITIVE-LANDSCAPE.md` and the decision record `docs/decisions/2026-11-05-licence-gpl.md`.
  - Build: 0 warnings. **1,050 tests pass** (Core 320, Protocols 215, App 408, Handoff 107).
- **Rights check (desktop):** the only authors are the owner and Claude sessions, so nobody else's agreement is needed. Owner to confirm: 89 commits use a `@zixel.cn` company address, so check that no employer holds rights in that work.
- **Microsoft Store:** no blocker found. The App Developer Agreement §4(c) makes the developer responsible for FOSS compliance, including source availability. Microsoft's pages were blocked from the session, so the owner should read them directly.
- **Research updates:**
  - QuickBuds is **GPL-3.0-or-later** (it was unconfirmed).
  - PairFree now lists **12 brands**.
  - Each project's licence was re-read from its own `LICENSE` file.
- **Phone: not done.** Attaching `samaat-mobile` to the session was refused by the session's permission check.
  - Its licence PR (`claude/licence-gpl` from `claude/mobile-improvements` → `main`, after mobile #1) and its rights check are still open.
  - Next step: run it in a session that already has `samaat-mobile` attached.
- **Left:**
  - the phone PR;
  - the owner checkboxes in PR #7;
  - a provenance header for `JlAuthCipher.cs`;
  - credits for the bundled packages in `THIRD_PARTY_NOTICES.md`.

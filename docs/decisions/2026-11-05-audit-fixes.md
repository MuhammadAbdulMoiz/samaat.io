# Audit fixes: issues 2 to 10

**Status:** branch `claude/audit-fixes`, from `feature/ui-redesign` at `473a430` (PRs #5, #2 and #4 already merged) · **Date:** 2026-11-05

## Why this work happened

The repository audit (2026-11-05, summary in [the session record](../sessions/2026-11-05-session-record.md#4-audit-summary-separate-session-full-report-uncommitted-there)) scored the project 6.5 / 10 and listed ten issues. Issue 1, the pairing race, was fixed in PR #5. This branch takes issues 2 to 10, one commit each (issue 2 needs no commit), in this order: 3, 4, 5, 6, 10, 9, 8, 7. The docs (7) came last so their numbers could be checked against the final code.

**Baseline:** `feature/ui-redesign` at `473a430` builds with 0 warnings and passes 1,008 tests (Core 298, Protocols 215, App 393, Handoff 102). **After:** 1,063 tests (Core 310, Protocols 215, App 406, Handoff 107, Platform.Windows 25), 0 warnings.

## Rules and procedures applied throughout

- `CONTRIBUTING.md`: test-first (each behaviour change has a test that failed first; for new seams a compile error counts), warnings as errors, never hide an unsupported feature, branches and releases.
- ECC common and C# rules (`.claude/rules/ecc/`): conventional commits, small focused files, errors handled explicitly, security review for the handoff changes, no secrets. Where ECC conflicts with the repo, the repo wins (commits keep `Co-Authored-By`).
- The layering: `ArchitectureTests` pass unchanged. They rule only `src/` projects, so the new test project needed no allow-list entry.
- A full `dotnet build Samaat.slnx` (0 warnings) and `dotnet test Samaat.slnx` before each push.
- Owner decisions are prepared, not taken: the licence, the flag's release default and the `.claude/` keep-list.

---

## Issue 2: no LICENSE (High), waiting on the owner

- **Decided:** no licence added. Choosing one is the owner's call; the pull request lists options.
- **Why:** a licence sets what everyone else may do with the code, and it is hard to take back once published.
- **Options prepared:**
  - **MIT:** anyone may use, change and sell the code, including in closed apps, with the copyright notice kept. Simplest; lets others build closed products on it.
  - **Apache-2.0:** like MIT, plus an explicit patent grant and a requirement to note changes. Common for projects that may attract companies.
  - **GPL-3.0 (or AGPL-3.0 for network use):** anyone may use and change it, but distributed changes must stay open under the same licence. Keeps forks open. The owner would have to offer the source of each Store build, and should check the Store's current policy on copyleft licences first.
- **Constraint either way:** CONTRIBUTING lets contributors read GPL and AGPL projects (Gadgetbridge, GalaxyBudsClient, OpenMouse) for behaviour only, never copying code. That keeps every option open, including MIT and Apache.
- **Left:** the owner picks one; then add `LICENSE` and a line in the README.

## Issue 3: `master` and dev drift (High), done (default to confirm)

- **Decided:** a phone-handoff feature flag, `AppOptions.PhoneHandoff`, replaces the per-branch screen edit (`e9917c7`). Then `origin/master` was merged in.
  - **Off:** the phone link is never created (nothing pairs, listens or broadcasts); the Settings and Links phone panels show the "Coming soon" card from `e9917c7`; the tray has no pairing item.
  - **On:** the full panels, and the tray offers "Pair a phone…" (which opens Settings).
  - **Default:** on for normal and Debug builds, off for the Store build. `packaging/msix/build-msix.ps1` builds with `-p:SamaatStoreBuild=true`, which defines `SAMAAT_STORE` and sets `AppOptions.PhoneHandoffDefault` to false.
  - **Merge:** both XAML conflicts resolved with the flag (dev's panels plus the card); `AppOptions.cs` keeps both sides (dev's `--simulate=<scenario>` and the flag, master's "start minimised when launched by the startup task"); packaging and `PRIVACY.md` kept. `master` has no commits left that dev lacks.
- **Why:** one switch in one place, instead of two branches with different screens that drift. The Store build stays as the owner shipped it, and dev keeps the full feature.
- **Turned down:**
  - **A runtime switch on package identity** (`PackagedApp.IsPackaged`): it ties the feature to how the app is installed, not to the release decision, and a sideloaded MSIX would lose the feature.
  - **A command-line switch or setting:** it would let Store users turn on a feature that hasn't run on a phone.
  - **Keeping master's edit and reverting it on dev:** that is the drift the audit flagged.
- **The tray:** neither branch had phone items in the tray, so honouring the flag there meant adding the one entry ("Pair a phone…"), shown only when the flag is on.
- **Verified:** `PhoneHandoffFlagTests` (4 tests: the default, the tray items, and both pages rendered with the flag off and on) failed to compile before the change. The Store configuration builds (`dotnet build src/Samaat.App -p:SamaatStoreBuild=true`).
- **Left:** the owner confirms the default (off in the Store build). The first release PR into `master`, with a tag.

## Issue 4: no tests for the Windows platform code (High), done (hardware checklist stays with the owner)

- **Decided:** a new `tests/Samaat.Platform.Windows.Tests` project (Windows TFM) for the platform's pure logic, with small seams extracted:
  - `WindowsSelectors`: the container-ID AQS filter and property reads (from `WindowsDeviceSource` and `OsBatteryFeature`);
  - `WindowsSignals`: endpoint name, Bluetooth name, IDs, services and manufacturer → `DeviceSignals`;
  - `OsBatteryFeature.ReadPercent`: `DEVPKEY_Bluetooth_Battery` conversion and clamping;
  - `KsProperty`: the `KSPROPSETID_BtAudio` one-shot request, its `KSPROPERTY` layout, `IOCTL_KS_PROPERTY` and `HRESULT_FROM_WIN32` (from `BluetoothAudioLink`);
  - `WindowsToastNotifier.ToastXml`: the toast and its escaping.
- **CI:** the project sets `IsTestProject` only on Windows, so `dotnet test Samaat.slnx` runs it on `windows-latest` and only builds it on `ubuntu-latest`. CI's commands didn't change.
- **Why:** the WinRT, COM and P/Invoke calls themselves need hardware, but the strings, structs and mappings around them were untested and are where silent mistakes hide (a wrong property key or struct size fails quietly).
- **Turned down:** mocking WinRT (`DeviceInformation`, `BluetoothDevice` are sealed runtime classes, so this would need wrappers for everything); running the project on Linux too (asked to keep it on Windows; see below).
- **Verified:** 25 tests. They failed to compile before the seams existed. Forced to run on Linux (`-p:IsTestProject=true`) they pass too, since the extracted logic is OS-independent; they still only run on Windows in CI, as asked.
- **Left:** the hardware checklist on real earbuds (PC connect has never run on hardware) is the owner's.

## Issue 5: silent failures (Medium), done

- **Decided:**
  - `Samaat.Core/Diagnostics/SamaatLog`: a static facade with a swappable `ILogSink` (`Info`, `Warn`, `Error`), no dependencies, never throws. The default sink is `Trace`, as before.
  - `Samaat.App/Diagnostics/RollingFileLog`: `logs/samaat.log` beside the settings, rolled to `samaat.log.1` at 256 KB (so at most about 512 KB), thread-safe, and silent if it can't write. Installed in `Program.Main`, which also logs unhandled and unobserved exceptions.
  - Diagnostics → Copy report ends with the last 40 log lines (or "nothing logged").
  - The **six** `async void` handlers (the audit counted 5; `DeviceRegistry.OnSourceChanged` is the sixth) run through `SamaatLog.Guard`, which logs a failure instead of crashing: Copy report, Copy row, the Windows connection handler, the two Jieli post-authentication reads, and `DeviceRegistry`.
  - 46 log calls added in `catch` blocks where the failure matters: settings, catalog and profile loading, failed commands, audio share, phone-link listening, handshakes, tampered frames and store errors, Windows device reads, PC link, RFCOMM, GATT writes, toasts, drivers that don't open, icons. Expected events (a phone dropping, a cancelled command, the beacon finding no network) stay quiet.
- **Why:** a static facade reaches every layer (protocols, handoff, platform) without changing dozens of constructors, and the app picks the sink. A file next to the settings is where a user can find it, and Copy report puts it in front of whoever helps them.
- **Turned down:** `Microsoft.Extensions.Logging` or Serilog (a dependency for every project, including the phone's); logging every catch (the expected ones would bury the real ones, and a flood of refused connections could fill the log); an injected `ILogger` everywhere (a large, mechanical change).
- **Verified:** `SamaatLogTests` (5), `LoggedFailureTests` (4: unreadable and unsaved settings, a broken catalog entry, a throwing command; all 4 failed before), `RollingFileLogTests` (6: location, format, the size cap, the tail across a roll-over, an empty tail, an unwritable folder), and two report tests in `DeviceInfoViewModelTests`. Tests that swap the global sink run in one non-parallel collection and filter by their own source, so other tests logging in parallel can't break them (Core tests run 3 times in a row: all pass).
- **Left:** nothing required. The log could later get a "Open log folder" button in Diagnostics.

## Issue 6: phone-link hardening (Medium), done

- **Decided** (security-sensitive; each change reviewed for slot and counter release on every path):
  - **Pairing re-checked after the handshake.** Each `OpenPairing` starts a new pairing window. A handshake that began in a window that has since closed (cancelled or timed out, even if reopened) is dropped with no prompt, and the slot is freed. Comparing windows, not just "is pairing open", also catches close-then-reopen.
  - **At most 4 connections in their handshake** (`PhoneLinkHostOptions.MaxPendingHandshakes`). Extra connections are closed at once, without a log line (a flood would fill the log).
  - **The beacon only while pairing is open.** It carries the computer's name in the clear. Paired phones dial the address and port they stored when they paired or last connected (`PhoneLinkClient.FindEndpoint` already fell back to it), and the host reuses its last port.
  - **`phones.json` created 0600 outside Windows**, where the secrets are stored as they are. The temp file is created with that mode, and the move keeps it.
  - **`PRIVACY.md`** now describes the local-network traffic (the beacon while pairing, the encrypted TCP session) and the diagnostics log; `docs/modules/phone-handoff.md` the new limits.
- **Why:** each closes a gap the audit named. The mid-handshake gap was also left open in PR #5's record.
- **Turned down:**
  - **Beaconing for a while after start-up or a network change**, to cover a computer whose address changed: it brings back the broadcast the audit flagged. Instead, opening "Pair a phone" for a moment lets a paired phone find the computer again without pairing again.
  - **Refusing extra handshakes with a message:** it spends work on exactly the connections being limited.
  - **Encrypting the store outside Windows:** there is no OS keystore in the simulator build, and a key next to the file adds nothing over file permissions.
- **Verified:** `HardeningTests`, 5 tests, all failing before the fix:
  - the mid-handshake test showed a prompt (it needs a paired phone so closing pairing doesn't stop the listener; without one it passed for the wrong reason, which the test now notes);
  - the cap test served the fifth connection;
  - the beacon test saw beacons while only paired;
  - the reconnect test (beacons always stale, host restarted with pairing closed) saw beacons, and now reconnects with none sent;
  - the permissions test found 0644.

  The handoff suite ran 5 times in a row: all pass. The existing `ComputerRestarts_PhoneReconnects` and the app's phone-flow tests pass unchanged.
- **Left:** if the computer's IP address changes, a paired phone can't find it until pairing is opened once (documented). Running the phone handoff on a real phone (STATUS 5b).

## Issue 7: stale docs (Medium), done

- **Decided:**
  - `docs/STATUS.md`: dated 2026-11-05; 1,063 tests with per-project counts taken from this branch's runs; the Bose QC Headphones (read-only BMAP noise mode, Windows battery, `verified: false`); the flag; the beacon and handshake limits; the new test project.
  - `docs/TRY-IT.md`: check out `feature/ui-redesign` instead of the merged `claude/phone-handoff`.
  - `CONTRIBUTING.md`: recorded bytes live where they actually are, as hex constants in `tests/Samaat.Protocols.Tests` (`Captured` in `JieliFakes.cs`, `FastPairCaptured` in `FastPairFakes.cs`, `BoseFrames` in `BoseBmapTests.cs`), each linked to its hardware test note. `FEATURE-IDEAS.md` follows.
  - `docs/PLAN.md`: the stack (§6) and the layout (§12) are marked superseded by `ARCHITECTURE.md`.
  - `docs/ARCHITECTURE.md`: logging, the Windows test project, coverage, and the current class sizes in "Known debt" (634, 707 and 410 lines). README: "over 1,000 tests".
- **Why:** point to what exists rather than create a `traces/` convention nobody uses; the existing one already ties each frame to its evidence.
- **Turned down:** moving the recorded bytes into `traces/<vendor>/` files: churn in three test projects for no new evidence.
- **Verified:** every count against `dotnet test` output and `wc -l`; that no project uses DI, System.Reactive, SQLite, Serilog or Verify; that every linked file exists.
- **Left:** nothing.

## Issue 8: `.claude/` noise (Medium), waiting on the owner

- **Decided:** `.claude/README.md` lists all 50 skills (9 relevant, 5 unsure, 36 not relevant, 29 of them Lark/Feishu) and all 7 plugins, says why for each, names the 5 third-party plugins enabled for everyone, and recommends a keep-list. Nothing deleted.
- **Why:** they are the owner's personal choices; the page makes the decision quick.
- **Turned down:** deleting the Lark skills and the 7.7 MB illustration skill now (not this branch's decision); rewriting history to drop the PNGs (64 MB history; disruptive for everyone with a clone).
- **Left:** the owner picks the keep-list and moves personal skills and plugins to `~/.claude/`.

## Issue 9: no release process (Medium), done (no tag or release created)

- **Decided:**
  - `<Version>1.0.0</Version>` in `Directory.Build.props`. **1.0.0, not 0.1.0:** the MSIX script already defaulted to `1.0.0.0`, and the Store only accepts increasing versions. `build-msix.ps1` now reads this version.
  - `.github/workflows/release.yml` on `v*` tags (windows-latest): refuses a tag that doesn't match `<Version>`, builds and tests, publishes the app as a self-contained win-x64 zip, packs the unsigned MSIX with the runner's Windows SDK (skipped if `makeappx` is missing, so the zip still ships), and creates a GitHub release with both and generated notes.
  - `.github/dependabot.yml`: NuGet (Avalonia and test packages grouped) and GitHub Actions, weekly.
  - Coverage: `coverlet.collector` 10.0.1 in every test project; CI collects it, `.github/scripts/coverage-summary.py` writes a per-assembly line-coverage table (lines keyed by file and number, since async state machines repeat them) to the job summary, and the table and Cobertura files are uploaded.
  - CONTRIBUTING's release steps describe all this.
- **Why:** a release is then "bump, merge, tag", with the tag checked against the code.
- **Turned down:** signing the MSIX in CI (the Store signs it, and a certificate would be a secret); ReportGenerator for the summary (another tool to pin, for one table); a version derived from git tags (MinVer and similar), since a plain property is easier to read and the workflow checks it.
- **Verified:** `ReleaseVersionTests` (3: the version is x.y.z, assemblies carry it, the workflow checks tags against it; all failed before). The workflows and `dependabot.yml` parse as YAML. Locally, coverage on Linux is 90.3% of lines (11,679 of 12,927; the Windows platform is measured on the Windows job). The release workflow itself can only run on a tag, so it hasn't run.
- **Left:** the first tag, by the owner. If the runner's Windows SDK lacks `makeappx`, the release has only the zip.

## Issue 10: screenshot churn (Low), done

- **Decided:** the docs screenshots run on `ScreenshotClock`, a `FakeTimeProvider` still at 2026-11-02 09:41 UTC, with no simulated command latency (its delays would wait on a clock that never moves). The phone screenshots also turn on Reduce motion (no transitions, a still busy bar), wait a real second and redraw the whole window before each capture, and show a fixed pairing code, since the real one comes from fresh random keys.
- **Why:** two runs differed in 50 of 58 images: battery drain on the simulator's 2 s timer, "Updated" ages and Diagnostics event times on the wall clock. After the clock fix only the four Links-page phone images still varied: a frame stitched from partial redraws, and transitions that run on the real clock.
- **Turned down:** comparing frames until two match (it never settled on some runs: something kept invalidating and the seams moved); committing only the README images (fewer docs pictures; still an option for the owner).
- **Verified:** `ScreenshotFrames_DontChange_AsRealTimePasses` renders a frame, waits past a simulator tick and renders it again (failed on the real clock). `docs/images` was regenerated once (49 files changed), then two more runs changed nothing (`git diff --name-only docs/images` empty after each). Five earlier back-to-back runs of both screenshot tests produced identical images.
- **Left:** the git history stays 64 MB; only a history rewrite would shrink it.

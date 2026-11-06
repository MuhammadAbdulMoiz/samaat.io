# Contributing to Samaat

## Workflow

We work test-first (see [docs/STATUS.md §4](docs/STATUS.md#4-working-on-samaat-tdd)): write a failing test, make it pass, then refactor. Run `dotnet test Samaat.slnx` before every commit. CI runs it on Ubuntu and Windows (`tests/Samaat.Platform.Windows.Tests` runs on Windows only), and each job's summary shows line coverage per assembly. Dependabot opens weekly pull requests for NuGet packages and GitHub Actions.

## Branches and releases

| Branch | What it's for | How changes get in |
|---|---|---|
| `master` | Released versions only. Each release is tagged, e.g. `v0.2.0` | A release pull request from dev. No direct pushes |
| dev (`feature/ui-redesign` for now) | Integration: every change lands here first | Pull requests with CI green |
| `feat/*`, `fix/*`, `docs/*`, `claude/*` | One change each, short-lived | Branch from dev, open a pull request into dev, delete the branch after merge |
| `hotfix/*` | An urgent fix to a released version | Branch from `master`, merge into `master`, then merge `master` back into dev |

- **Start work from dev:** `git switch -c feat/<short-name> origin/feature/ui-redesign`, and open the pull request against `feature/ui-redesign`, not `master`.
- **Release:**
  1. On dev, set `<Version>` in `Directory.Build.props` to the new version (it starts at `1.0.0`, the Store package's version; the Store only accepts increasing versions).
  2. Open a pull request from dev into `master` and merge it once CI passes.
  3. Tag the merge commit with the same version: `git tag v1.0.1 && git push origin v1.0.1`.
  4. The Release workflow (`.github/workflows/release.yml`) checks that the tag matches `<Version>`, builds and tests on Windows, and attaches the Windows app (zip) and the unsigned Store package (`.msix`) to a GitHub release. Upload the `.msix` to Partner Center yourself; the Store signs it.
- **Keep dev current:** after a hotfix, or any change made on `master`, merge `master` back into dev so the two don't drift.
- When dev is renamed to `dev`, GitHub moves open pull requests and the default-branch setting with it. Only this section and the CI trigger mention the old name.

## Adding or changing device support

Device support is only as good as its evidence. A catalog entry may set `"verified": true` only when the pull request includes:

1. **The model and firmware**: exact retail name, the firmware version shown by the vendor app, the region if it matters.
2. **A Samaat report**: Info page → **Copy report** (or `samaat-cli --watch` output), with the device connected.
3. **For a driver**: the bytes of each command the driver sends and the device's replies, recorded from the real device and replayed in tests. They live in `tests/Samaat.Protocols.Tests` as hex constants in a class named for their source (`Captured` in `JieliFakes.cs`, `FastPairCaptured` in `FastPairFakes.cs`, `BoseFrames` in `BoseBmapTests.cs`), whose summary links the hardware test note in `docs/hardware-tests/` that recorded them. Label any synthetic frame as synthetic.
4. **A manual checklist**, completed on real hardware:
   - [ ] Device is listed and recognised as the right model
   - [ ] Battery matches the vendor app (each component the model reports)
   - [ ] Every exposed control changes the device, and the change survives a reconnect
   - [ ] Changes made on the earbuds show up in Samaat
   - [ ] Unsupported features show the right reason
   - [ ] Disconnect / reconnect doesn't crash or leave stale state

Without hardware evidence, entries stay `"verified": false` and the UI marks them "recognised, not yet verified on hardware".

## Protocol work and licences

- Learn protocols from **your own captures** (Android HCI snoop log + Wireshark, Windows Bluetooth ETW traces).
- Samaat is licensed under **GPL-3.0-or-later** ([LICENSE](LICENSE)), so code from other open-source projects may be reused under these rules:

  | Source | Rule |
  |---|---|
  | GPL-3.0, GPL-3.0-or-later, MIT, BSD, Apache-2.0 | **May be ported, with provenance** (below) |
  | AGPL-3.0 (e.g. Gadgetbridge, OpenMouse), GPL-2.0-only | **Behaviour only**: read it to learn the protocol, then write Samaat's own code. Don't copy it |
  | Vendor apps, SDKs, firmware, decompiled code | **Never** |

- **No trademarks or assets:** don't copy names, logos, fonts or icons from another project or vendor. Keep "works with <model>" wording.
- **Provenance:** every ported file starts with this header, and its source gets an entry in the "Ported protocol code" table of [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) (project, URL, commit, licence, files). The ported file is licensed GPL-3.0-or-later; the original licence is recorded in the header and the notices.

  ```csharp
  // SPDX-License-Identifier: GPL-3.0-or-later
  // Ported from <repo URL> @ <commit>, <path>, Copyright (c) <year> <authors>, <original licence>.
  // Changes: rewritten onto Samaat.Transport (IRfcommChannel/IGattClient) and Samaat.Core features.
  ```

  `ProvenanceTests` fails the build if a file under `src/Samaat.Protocols.*` says "Ported from" without an SPDX line or with a source missing from the notices, or uses an SPDX identifier outside GPL-3.0-only, GPL-3.0-or-later, MIT, BSD-2-Clause, BSD-3-Clause and Apache-2.0.
- Port only the protocol layer (framing, commands, parsing), one main source per brand, and record each port in a decision record. Another project's model list is a lead, not hardware evidence: the rules in "Adding or changing device support" still apply.
- Never bypass encryption or authentication, and never redistribute vendor firmware.

## Code style

- Match the surrounding code. Warnings are errors.
- New capabilities start as a feature interface in `Samaat.Core`, then the simulator, then the UI.
- A new vendor driver is registered in `Samaat.Drivers` (`BuiltInDrivers`), never directly in the app or the CLI. Project references must follow the layering in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#projects-and-layers); `ArchitectureTests` fails the build otherwise.
- Every write returns a `CommandResult`, and the UI must handle refused and timed-out commands.
- Never hide an unsupported feature: show it with a reason.
- UI values come from the design system: colours from `Samaat.UI/Styles/Tokens.axaml`, radii, type sizes, hit targets and shadows from `Samaat.UI/Styles/Metrics.axaml`, and repeated looks from a style class. `DesignSystemTests` fails the build on a raw hex colour, font size, shadow or tokenised radius in `.axaml`.

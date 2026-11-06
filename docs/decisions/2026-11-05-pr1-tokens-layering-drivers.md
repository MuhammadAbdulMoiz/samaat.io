# PR #1: design tokens, enforced layering, one driver list

**Status:** merged into `feature/ui-redesign` (merge commit `1f34892`) · **Date:** 2026-11-05 · **Branch:** `claude/redesign-improvements`

## Why this work happened

- **The ask:** install ECC for cloud sessions, then improve the project "starting from design structure", and later "design does not mean just UI, backend too", following proper rules and procedures.
- **What the review found:**
  - Only colours were design tokens. Radii, font sizes, hit targets and shadows were literals repeated in 14 files, and the Settings swatches copied `AccentPalette`'s hex values by hand.
  - The shipped driver list was written out three times (`AppOptions`, the CLI, `ShippedDeviceTests`), but `ARCHITECTURE.md` told contributors to update only the first.
  - `CommandRunner` reported `TimedOut` but left the command running, so a driver could keep waiting and hold its channel against the next command.
  - `ARCHITECTURE.md` still called vendor protocols "next", and left out `Samaat.UI`, `Samaat.Drivers` and the newer pages.

## Decisions

| Decision | Why | Rejected alternative |
|---|---|---|
| Enable ECC through project `.claude/settings.json` and vendor its `common` and `csharp` rule packs in `.claude/rules/ecc/` | Cloud containers start fresh, so a user-level install doesn't survive. Plugins can't ship rules | User-level install only (lost every session) |
| Theme-independent tokens go in `Samaat.UI/Styles/Metrics.axaml`, beside the colour tokens | `Samaat.UI` is shared with the phone app, so one copy serves both | Tokens in `Samaat.App` (the phone would drift) |
| `Artwork.axaml` merges `Metrics.axaml` itself | The phone app includes Artwork without knowing about the new dictionary; it must keep working | Requiring samaat-mobile to change first |
| `DesignSystemTests` fail on raw hex colours, literal font sizes and shadows, and literal radii that have a token | A rule that isn't enforced decays | A written guideline only |
| One `Samaat.Drivers` project (`BuiltInDrivers`) | Adding a vendor becomes one line in one place; the apps no longer reference vendor protocols | Keep three lists and document them |
| `ArchitectureTests` check every `.csproj` against a per-layer allow-list | Makes the layering in `ARCHITECTURE.md` a build failure rather than a hope | Code review only |
| Keep `Samaat.Platform.Windows → Samaat.Handoff` | A platform implementing a feature module's contract (`ISecretProtector`) matches Android's `IPhoneAudioLink`. Moving the interface would break samaat-mobile, which can't be checked from here | Move `ISecretProtector` into Core |
| On timeout, cancel the token the command received | Frees the channel for the next command; only the caller's own cancellation is rethrown | Leave the command running |
| Keep `DeviceCatalog` and `DeviceProfileDb` separate | Merging the two parsers is a larger change; listed as known debt | Merge now |
| The README claims no model is verified | Every catalog entry is `"verified": false` | A "verified" column |

## Rules and procedures applied

- `CONTRIBUTING.md`: test-first (each new test failed before its change), warnings as errors, never hide an unsupported feature.
- ECC common rules: DRY (one driver list, one token per value), KISS, high cohesion, files under 800 lines, explicit error handling. Where ECC conflicts with this repository (ECC defaults commits to no `Co-Authored-By`), the repository and session instructions win.
- Verification: refactors had to be pixel-identical. All 58 dark and light screenshots were rendered from the old and new code and compared (0 differences). Full suite: 966 tests.

## Constraints met along the way

- **.NET SDK:** Microsoft's direct downloads are blocked by the network policy, so the .NET 10 SDK came from Ubuntu's apt packages (`dotnet-sdk-10.0`).
- **GateGuard:** ECC's hook asks for facts before first edits and destructive commands. `ECC_GATEGUARD=off` disables it.

## Open items

- Add a LICENSE file.
- `PRIVACY.md` exists only on `master`.
- Known debt, listed in `ARCHITECTURE.md`: the two catalog parsers, the `#if SAMAAT_WINDOWS` checks spread through `AppOptions`, and the size of `DeviceViewModel`, `PhoneLinkHost` and `PhoneLinkClient`.

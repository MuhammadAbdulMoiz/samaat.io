# Plan: UI redesign using OpenMouse as the benchmark

Branch `feature/ui-redesign` from `master` (`c0144f1`). User approved the direction from a mockup.
Benchmark: OpenMouse `DESIGN.md` and control.openmouse.app. OpenMouse is AGPL-3.0: ideas only, no code, assets or artwork copied (CONTRIBUTING.md).
Stack: Avalonia 11.3, CommunityToolkit.Mvvm, xUnit + Avalonia.Headless. TDD (docs/STATUS.md §4). Warnings are errors.

## Goals (what the user approved)
1. **Sidebar** (~240 px, labelled) replaces the 76 px icon rail and the top device strip. Contents top to bottom: brand; "Your devices" list (name, battery figure, connection line; selecting one shows that device); flexible space; alert counter (when any device is low) and simulation note; Refresh devices; Settings. Collapses to a 68 px icon rail below 900 px window width (device rows show initials + battery).
2. **Sentence case everywhere**: no uppercase micro-labels ("YOUR DEVICES", "BASIC SUPPORT", "CAPABILITIES"), no decorative dots except real state (connected/low).
3. **Device page** = rail + workspace.
   - Rail (260 px; stacks above the workspace below 760 px of content width, using the existing SplitPanel): device name (page title 22), model/maker subtitle, earbud artwork on the canvas (no card; reuse the existing artwork controls), battery as a figure (value 30/600 + "%" unit + meta "from Windows" / source), connection and support tier as one line.
   - Workspace: a segmented tab well — Overview · Sound · Links · Details · Diagnostics — then that tab's panels. "Diagnostics" is master's Info page (renamed label only); "Details" is the Bluetooth details page. Controls (roadmap) is dropped from navigation and stays listed in STATUS.
4. **Panels show real values**: battery panel shows Overall/Left/Right/Case figures with "–" for unreported values and one short reason line; noise and links panels keep their current behaviour and honest unavailable text. Capability chips stay but move into the Diagnostics tab only (Overview no longer lists every unsupported feature).
5. **Theme presets**: Settings › Appearance gets accent presets Lime (current, default), Emerald, Ice, Mono, plus the existing System/Light/Dark mode. Persisted in `AppSettings` (`Accent` enum, default Lime; older settings files without it load as Lime). Applied by replacing the accent brushes (`Samaat.Accent`, `AccentSoft`, `OnAccent`, `Wave`, `SystemAccentColor`) in both theme dictionaries at runtime.
6. **Shape and rhythm**: 4 px grid; panel padding 16, panel gap 16, rows ≥ 40, buttons 32, switches 36×20; radii 6 (controls/rows), 10 (panels), 14 (artwork stage, dialogs); numbers use tabular figures (`FontFeatures="+tnum"`). Motion: keep the existing cross-fade and value animations (120–260 ms).
7. **Reduced motion (new; the app has none today)**: `AppSettings.ReduceMotion` (bool?, null = follow the OS). OS source: `IMotionPreference` in `Samaat.Core/Platform` (no-op returns false), Windows impl reads `Windows.UI.ViewManagement.UISettings.AnimationsEnabled` (try/catch → false), chosen in `AppOptions` like the other platform services. Settings › Appearance shows a "Reduce motion" switch (initial state = OS value until the user changes it). When on: the page cross-fade duration is 0 and the ring/bar value transitions are removed (a `reduce-motion` class on the window that styles select on). Test: with ReduceMotion on, the TransitioningContentControl's transition has zero duration and the gauge style has no transitions.
   Persistence: `SettingsViewModel` keeps the nullable override (`_reduceMotionOverride`) separately from the effective switch value (`ReduceMotion => _reduceMotionOverride ?? os`); `Current` writes the override, never the effective value; only the user toggling the switch sets an explicit bool. Tests: (a) null follows a fake OS preference of true and of false, (b) changing an unrelated setting keeps null on disk, (c) toggling the switch writes an explicit bool.
8. **Responsive mechanism**: `MainWindow` code-behind sets `MainWindowViewModel.IsSidebarCompact = Bounds.Width < 900` on open and on every `SizeChanged`. The device rail uses the existing `SplitPanel` (it already measures its own width) for the 760 px stack. Test: show a headless window at 1180 then resize to 800 and assert compact; resize back and assert expanded.
9. **Accessible names** (asserted in the shell accessibility test): compact-sidebar device rows ("<name>, <battery>, <connection>"), Settings, Refresh devices, alert counter (its count text), each tab ("Overview", "Sound", "Links", "Details", "Diagnostics"), battery figure ("Battery <n> percent" / "Battery not reported"), artwork ("<name> earbuds"), Reduce motion switch.

Fonts: keep Inter (from the `Avalonia.Fonts.Inter` package via `.WithInterFont`) for text and the bundled Space Grotesk for the page title and figures; no new font files (the mockup's DM Sans is not added). Inter has tabular figures.

## Approach (keep the structure)
- View models and features stay; changes are in `Shell/MainWindow.axaml`, `Shell/MainWindowViewModel.cs` (sidebar state: `IsSidebarCompact`, device tabs as `AppSection` values, Settings outside the device tabs), `Styles/*.axaml`, `Styles/Tokens.axaml`, and each page's XAML. No new NuGet packages.
- `AppSection` keeps its values; `Info` displays as "Diagnostics". Selecting a device from the sidebar keeps the current tab (or Overview when coming from Settings).
- Empty state (no devices) and toasts keep their behaviour, restyled.
- Tray, settings store, notifications, simulator and Windows code are untouched.

## Tests (written first)
- Shell: sidebar lists all devices with battery text; selecting a device changes `SelectedDevice`; tab well switches `AppSection`; Settings is reachable with and without devices; compact sidebar below 900 px; device rail stacks below 760 px.
- **No uppercase micro-labels**: walk every TextBlock of every page (simulated Buds Pro, Lite, Headset, plus Settings) and fail on any text of ≥ 4 letters that is entirely uppercase (allow-list acronyms such as ANC, SPP, HFP, AVRCP, A2DP, VID, PID, ID).
- Battery panel shows "–" for unreported components and figures for reported ones.
- Theme presets: setting persists, older file without `Accent` loads Lime, applying a preset changes `Samaat.Accent`; extend the existing WCAG AA contrast tests to every preset in dark and light.
- Existing tests updated, not deleted, where behaviour moved (navigation, screenshots). Screenshots re-rendered into docs/images (same file names where the page still exists) via `SAMAAT_SCREENSHOTS`.

## Verification
- `dotnet build Samaat.slnx`, `dotnet test Samaat.slnx` green.
- Run the app on Windows with the real Quantum Zbuds and with `--simulate`; capture Overview/Sound/Links/Details/Diagnostics/Settings and a narrow window.
- Docs: STATUS.md (UI section), docs/design/ui-audit.md short "OpenMouse-style redesign" entry.

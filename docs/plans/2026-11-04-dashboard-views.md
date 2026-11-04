# Plan: switchable dashboard views

Status: **proposal, not started.** Branch for the plan: `claude/dashboard-views-plan`. The work itself builds on `feature/ui-redesign`, which another session is still changing (Overview, Details, Diagnostics, Settings, sidebar). Read this together with `docs/plans/2026-11-03-ui-redesign.md` and `docs/design/ui-audit.md`.
Stack: .NET 10, Avalonia 11.3, compiled bindings, CommunityToolkit.Mvvm, xUnit + Avalonia.Headless. TDD (docs/STATUS.md §4). Warnings are errors. No new NuGet packages.

The `design:design-critique`, `design:design-system` and `design:ux-copy` skills were not available in this session. I applied their checklists by hand, using the repo's own rules: sentence case, numbers first, "–" for unreported values, honest unavailable text, 32 px targets, and the tokens in `Styles/Tokens.axaml`.

## 1. Goal

The user picks one of a few dashboard layouts and can switch between them at any time. Each layout shows the same live data, comes from the same view models and uses the same design tokens. Only the arrangement and density change. The choice persists across restarts.

Non-goals: per-device layouts, user-arranged widgets, new colours or themes (accent presets already cover colour), and a tray flyout. A flyout would reuse Glance later (Q9).

## 2. The views

There are four views. Studio is today's layout and stays the default. The other three each answer a different question:

| View | The question it answers | Scope | Density |
|---|---|---|---|
| **Studio** | "Let me look at and change everything about this device." | One device | Normal |
| **Focus** | "How are my earbuds, and can I switch noise mode quickly?" | One device | Low, large type |
| **Glance** | "How charged is everything I own?" | All devices | Small window size |
| **Table** | "Compare all my devices and act on them quickly." | All devices | High |

### 2.1 Studio (today's layout, the default)

- **Purpose:** the full control surface. Nothing changes for current users.
- **Layout:** sidebar (240 px, 68 px rail below 900 px) + device page. The page has the 260 px rail (name, subtitle, artwork stage, battery figure, status line, Connect) and a workspace with the tab well (Overview · Sound · Links · Details · Diagnostics) above the page in a `TransitioningContentControl`.
- **Shows:** everything. **Hides:** nothing.
- **Narrow:** as it works today. The sidebar collapses below 900 px. The `SplitPanel` stacks the rail above the workspace when the primary column would be narrower than 484 px.
- **Dark/light:** as today. The screenshot tests already cover both.
- **Work:** extraction only. The `SplitPanel` block in `MainWindow.axaml` moves, unchanged, into `Shell/Views/StudioView.axaml` (§4.3).

### 2.2 Focus: one device, big and calm

- **Purpose:** the "just tell me" screen. You glance at it from across the desk or leave it on a second monitor. It also suits people who find the tab well busy.
- **Layout:** sidebar as in Studio. The content area is one centred column, at most 640 px wide:
  1. **Hero:** the artwork stage at about 1.5× Studio's size (stage height 320), with the device name (title) and the status line under it.
  2. **Battery row:** the `ShownFigures` cells (Overall/Left/Right/Case) as large figures in `AdaptiveColumns`, max 4 columns, with the same level bars, low and critical icons, labels and "–" values as Overview.
  3. **Quick controls**, one row of at most three: the noise mode segmented control (only when `NoiseControl` is supported; otherwise one honest line such as "Noise control isn't available for these earbuds"), Connect or Disconnect (`ShowConnect`/`ShowDisconnect`), and Refresh battery.
  4. **More:** a quiet row of ghost buttons, "Sound", "Links", "Details" and "Diagnostics". Each opens that page in place under a compact hero (the hero shrinks to a 64 px header with name and battery), with a "Back" button. The user stays in Focus. See open question Q3.
- **Shows:** battery, connection, noise mode, connect, refresh. **Hides:** EQ, audio share, multipoint hosts, capability chips and IDs. They stay one click away under "More", never removed.
- **Narrow:** the column fills the width (16 px gutters). The quick controls wrap into one per row below 480 px of content width. The stage scales down to 216 px (Studio's size) below 560 px.
- **Dark/light:** same tokens. The larger stage uses the existing `stage` style and `SoundWaves` at the same 0.14 opacity. No new surfaces.

### 2.3 Glance: every device at once, about tray size

- **Purpose:** check battery across several devices (earbuds, headset, a partner's buds) in a window you can keep small in a corner.
- **Layout:** the sidebar's device list is redundant here, so Glance always uses the compact 68 px rail (brand, layout switcher, alert counter, Refresh, Settings), whatever the window width. The content is a vertical list of **device cards**, one per device, 8 px apart:
  - Line 1: name (trimmed, full name in a tooltip) and the connection dot + `ConnectionText`.
  - Line 2: the figures as compact pairs: "L 80 %", "R 75 %", "Case 40 %". Single-level devices show one "Battery 62 %". Unreported values show "–". Each figure has a thin level bar (`ProgressBar.level`, with the low and critical classes and their icons).
  - On the right: a 32 px ghost button, "Open", which selects the device and opens Focus or Studio (Q4).
  - A device with an alert gets the existing `alert` treatment on its card (icon + text, not colour alone).
- **Shows:** battery and connection for all devices. **Hides:** artwork, noise, EQ, links and diagnostics.
- **Narrow:** built for narrow windows. Cards fill the width from 360 px up. Above 760 px of content width the list becomes a 2-column grid (`AdaptiveColumns MinColumnWidth="320" MaxColumns="3"`), so a wide window doesn't stretch cards across the screen. Glance needs the window to go below today's `MinWidth="640"`, which is Q2.
- **Dark/light:** cards use `Surface` on `Bg` with the `PanelEdge` hairline, the same as panels. The contrast tests already cover every text pair used.

### 2.4 Table: dense, comparative, keyboard-first

- **Purpose:** for people with several devices who want numbers side by side and fast actions without opening each device.
- **Layout:** sidebar as in Studio (it still selects the device for the detail strip). The content is one panel holding a table built from `ItemsControl` + `Grid.IsSharedSizeScope` (no DataGrid package), with 40 px rows and tabular figures:

  | Device | Connection | Overall | Left | Right | Case | Noise | Updated | |
  |---|---|---|---|---|---|---|---|---|
  | name + subtitle (meta) | dot + text | figure | figure | figure | figure | mode or "–" | "2 min ago" | Connect/Refresh ghost buttons |

  - Column headers are buttons that sort the table (ascending/descending, with an arrow icon and `AutomationProperties.Name` such as "Sort by left battery, ascending").
  - Selecting a row (click, or arrow keys + Enter) opens a **detail strip** under the table for that device: noise segmented control, Connect/Disconnect, Refresh, and "Open in Studio" (Q4). It is the same quick-controls fragment as Focus (§4.3).
- **Shows:** every number at once, plus noise mode and freshness. **Hides:** artwork, EQ, share and hosts (reached through the detail strip).
- **Narrow:** columns drop in a fixed order as the content narrows: Updated (< 900 px), Case (< 800), Noise (< 720). Below 600 px each row becomes two lines (name + connection; figures under it). It never scrolls sideways.
- **Dark/light:** row separators use the existing hairline brush. The selected row uses `AccentSoft` with a 2 px accent bar on the left, so selection doesn't rely on fill alone. No zebra striping, which adds noise in dark mode.

### 2.5 What every view keeps

The sidebar (or its compact rail), the alert counter, toasts, the empty state ("No earbuds yet"), the simulation chip, Settings (always reachable, always full page) and the reduce-motion class on the window. A view never hides a low-battery alert.

## 3. The switcher and persistence

### 3.1 Where it lives (three entry points, one property)

1. **Settings › Appearance › "Dashboard layout"**: four radio cards, each with a small vector diagram of the layout (drawn from rectangles with existing brushes, not screenshots, so it follows theme and accent), a name and one line of description:
   - Studio: "Every control, one device at a time"
   - Focus: "One device, big and simple"
   - Glance: "All your devices' batteries, in a small window"
   - Table: "All devices side by side"
2. **Sidebar footer:** a "Layout" nav button above Settings, with the `Icon.Grid`-style icon (a new path in `Icons.axaml`, from Fluent UI System Icons, MIT, like the others). It opens a `MenuFlyout` with the four names, a check on the current one and the shortcut as `InputGesture`. In the compact rail it shows the icon only, with a tooltip and an accessible name ("Layout: Studio").
3. **Keyboard:** `Ctrl+Shift+1` to `Ctrl+Shift+4` pick a view directly. These are `KeyBinding`s on `MainWindow` bound to `SetDashboardCommand`. `Ctrl+1` to `Ctrl+5` stay free for the tab well later. See Q5.

Switching a view never moves to another device and never leaves Settings. Picking a view while in Settings saves it and applies it when the user goes back.

### 3.2 Settings schema (`Samaat.Core.Settings`)

```csharp
public enum DashboardView { Studio, Focus, Glance, Table }

public sealed record AppSettings
{
    /// <summary>Schema version; files without it are version 1.</summary>
    public int Version { get; init; } = AppSettings.CurrentVersion;   // CurrentVersion = 2
    public DashboardView Dashboard { get; init; } = DashboardView.Studio;
    // …existing properties unchanged…
}
```

**Migration.** Today `SettingsStore` serializes enums with `JsonStringEnumConverter(camelCase)`, and an unknown enum string throws `JsonException`. The store then **moves the whole file to `settings.json.bad` and resets every setting**. This already affects `Theme` and `Accent`, and a new enum makes it more likely, for example if the user runs an older build after trying a newer one with a fifth view. So:

- Missing `dashboard` key (every v1 file): loads as `Studio`, through the property default. No rewrite on load. The next save writes `version: 2`.
- Unknown `dashboard` value: loads as `Studio`, and **the rest of the file is kept**. This uses a small `LenientEnumConverter<T>` (unknown → `default(T)`) registered for `DashboardView`. Applying it to `ThemeChoice` and `AccentChoice` as well is a cheap hardening (Q6).
- `Version` > `CurrentVersion` (file from a newer build): load what we understand and **don't downgrade the number on save**. `Current` carries the loaded version forward: `Math.Max(loaded, CurrentVersion)`.
- `Normalize()` maps any out-of-range numeric enum value (`"dashboard": 7`) to `Studio`.

Tests (in `tests/Samaat.Core.Tests`, next to the existing settings tests):
- a v1 file without `dashboard` loads `Studio` and keeps all other values
- `"dashboard": "orbit"` loads `Studio`, keeps `theme`/`accent`/thresholds, and **no `.bad` file is created**
- `"dashboard": 7` normalizes to `Studio`
- a round trip of every value
- a save after loading v1 writes `"version": 2`
- a file with `"version": 3` keeps 3 on save

### 3.3 Settings view model

`SettingsViewModel` gets `[ObservableProperty] DashboardView Dashboard`, plus `IsStudioView` … `IsTableView` for the radio cards (the same pattern as `Accent`), and `OnDashboardChanged => Changed()`. `Current` includes it. Tests: changing it saves; loading a store with `Focus` sets it.

## 4. Architecture

### 4.1 Principle: views are templates, not features

No new feature view models with their own device logic. Every view binds to what already exists: `MainWindowViewModel` (devices, selection, alerts, commands) and `DeviceViewModel` with its children (`Figures`/`ShownFigures`, `NoiseControl`, `Equalizer`, `Connections`, `Info`, `Details`, `Share`, connect/disconnect/refresh commands, the accessible texts). If a view needs a new derived value, such as a short noise-mode label for the Table column, it is added **once** to the owning view model and tested there. XAML in a view must not compute what the view model could give it.

### 4.2 Shell view model

```csharp
// Shell/MainWindowViewModel.cs
public DashboardView Dashboard => Settings.Dashboard;          // raised when Settings.Dashboard changes
[RelayCommand] private void SetDashboard(DashboardView view) => Settings.Dashboard = view;

/// <summary>What the content area shows: a page of the chosen layout, Settings, or nothing.</summary>
public DashboardModel? CurrentDashboard { get; }               // recomputed by UpdatePage()
```

`DashboardModel` is a small record hierarchy next to the existing `PageModel`s:

- `StudioDashboard(DevicePageModel Page)`: wraps today's `DevicePage`, so Studio's tab logic is reused as it is.
- `FocusDashboard(DeviceViewModel Device, DevicePageModel? MorePage)`: `MorePage` is null for the hero, and set to a `SoundPageModel` etc. when the user picks something under "More".
- `GlanceDashboard(ObservableCollection<DeviceViewModel> Devices)`.
- `TableDashboard(DeviceTableViewModel Table)`.

`UpdatePage()` already switches on `Section` and `SelectedDevice`. It gains the `Dashboard` axis. `Section` keeps its meaning (Studio tab or Focus "More" page), so `AppSection`, `NavigateCommand` and the tray stay as they are.

`DeviceTableViewModel` is the only new view model, and it holds only presentation state: `SortColumn`, `SortDescending`, `Rows` (a sorted view of `Devices`, refreshed on `CollectionChanged` and on the battery properties it sorts by), and the `SortCommand`. Sorting compares the `BatteryFigureViewModel.Percent` values that already exist. Unreported values always sort last, in both directions.

`IsSidebarCompact` becomes `IsSidebarCompact = width < 900 || Dashboard == Glance`. That is one line in `MainWindow.axaml.cs` plus a re-evaluation when `Dashboard` changes.

### 4.3 XAML structure

```
Shell/MainWindow.axaml          sidebar · empty state · Settings · toasts  (common, unchanged in place)
  └ ContentControl x:Name="DashboardHost" Content="{Binding CurrentDashboard}"
       DataTemplates: StudioDashboard → views:StudioView
                      FocusDashboard  → views:FocusView
                      GlanceDashboard → views:GlanceView
                      TableDashboard  → views:TableView
Shell/Views/StudioView.axaml    today's SplitPanel (rail + tab well + PageHost), moved without changes
Shell/Views/FocusView.axaml
Shell/Views/GlanceView.axaml
Shell/Views/TableView.axaml
Shared/Parts/DeviceStage.axaml      the artwork stage (photo / split parts / unknown pieces / ring) with a StageSize property
Shared/Parts/BatteryFigures.axaml   the ShownFigures grid from Overview (Large / Compact via a class)
Shared/Parts/QuickControls.axaml    noise segmented + connect/disconnect + refresh (Focus hero, Table detail strip)
```

- `DeviceStage` is the biggest win. The stage is about 120 lines of XAML in `MainWindow.axaml` today, and Focus needs it at a different size. It is extracted once, with `x:DataType="overview:DeviceViewModel"`, and Studio's rail uses it too, so the two can't drift apart.
- `BatteryFigures` is extracted from `OverviewPage.axaml` the same way, and Overview uses it.
- Size and density variants are **style classes** on the parts (`Classes="large"`, `Classes="compact"`) in `Styles/Controls.axaml`, using the 4 px grid and the existing type ramp. No per-view copies of colours or sizes, and no new tokens unless a view really needs one. If one is added, it goes in both theme dictionaries with a contrast test.
- `PageHost` (the cross-fade target in `MainWindow.axaml.cs::ApplyMotion`) moves into `StudioView`. `ApplyMotion` finds it through a small `IHasPageTransition` interface on the view, or a named lookup after the template applies, so reduce motion keeps working (there is a test for this).
- Compiled bindings stay on (`x:DataType` on every view and template). The `$parent[ItemsControl].((shell:MainWindowViewModel)DataContext)` pattern used in the sidebar is reused for commands inside item templates.

### 4.4 Coordination with `feature/ui-redesign`

The redesign session owns Overview, Details, Diagnostics, Settings and the sidebar. To avoid conflicts:
- **Phase 0 starts only after the redesign has merged**, or at a point the other session agrees on. Phase 0 rebases onto it, then extracts `StudioView`, `DeviceStage` and `BatteryFigures` from whatever the redesign left.
- Settings › Appearance gets one new group, added after the redesign's Appearance group is final.
- If the redesign changes the Overview battery panel or the rail, the extracted parts follow it. The views reference the parts, not copies.

## 5. Accessibility

- **Targets:** every interactive element is at least 32 × 32 px (buttons, sort headers, card "Open", flyout items, the layout radio cards). Table rows are 40 px. Asserted by a test (§6).
- **Accessible names:**
  - each view root: "Studio layout", "Focus layout", "Glance layout", "Table layout"
  - layout button: "Layout: <current>"
  - radio cards: "<name> layout, <description>"
  - Glance cards: the existing `RowAccessibleText` ("<name>, <battery>, <connection>")
  - Glance figures: "Left 80 percent" from `BatteryFigureViewModel.AccessibleText`
  - Table cells: column + value ("Left battery 80 percent"); sort headers state the direction
  - Focus "More" buttons: "Sound settings for <name>"
  - Back: "Back to <name>"
- **Focus after a switch:** keyboard focus moves to the first heading or selected item of the new view (Studio: the selected tab; Focus: the device name; Glance: the first card; Table: the selected row, or the first header). It is never lost to the window. If Avalonia 11.3's automation peers support a live-region setting, the switch is announced ("Glance layout"). Otherwise moving focus to the named view root does the job.
- **Keyboard:** everything is reachable by Tab in reading order. The Table supports arrow keys between rows and Enter to open the detail strip. `Ctrl+Shift+1–4` switch views. The flyout lists the shortcuts.
- **Reduce motion:** switching views is **instant in every mode**. It is a mode change, not navigation, so it gets no fade. Inside Studio the existing tab cross-fade still follows `ReduceMotion`. The value transitions on rings and bars stay governed by the `reduce-motion` window class, which every new view inherits because it is styled at window level.
- **No decorative motion:** no animated hero, no moving waves, no hover zoom. `SoundWaves` stays static.
- **State not by colour alone:** low and critical always carry an icon and text, connection carries text next to the dot, and Table selection carries a bar as well as a fill.
- **Sentence case:** the existing "no uppercase micro-labels" test is extended to walk every view.

## 6. Tests (written first, per phase)

**Core (Samaat.Core.Tests):** the settings schema and migration tests in §3.2.

**View models (Samaat.App.Tests):**
- `SettingsViewModel`: `Dashboard` persists and loads, and the `Is…View` flags follow it.
- `MainWindowViewModel`:
  - `SetDashboardCommand` changes `Settings.Dashboard` and `CurrentDashboard`'s type
  - switching keeps `SelectedDevice` and `Section`
  - switching while in Settings stays in Settings
  - Glance forces `IsSidebarCompact`, and switching back to Studio at 1180 px restores the full sidebar
  - Focus "More → Sound" sets `MorePage` to a `SoundPageModel` for the selected device, and Back clears it
  - the empty state shows in every view when there are no devices
- `DeviceTableViewModel`:
  - sorts by each column in both directions
  - unreported values sort last in both directions
  - re-sorts when a device's battery changes or a device is added or removed
  - keeps the selection through a re-sort

**Shell and layout (headless):**
- For each view × {1180 px, 700 px, 400 px where the minimum width allows}: the view renders, nothing scrolls sideways (the content `ScrollViewer` extent width is at most the viewport width), and the expected elements are visible or hidden (e.g. Table at 700 px hides Updated and Case; Glance at 1180 px shows two columns).
- Accessibility walker, for each view and each simulated device (Buds Pro, Lite, Headset): every `Button`/`ToggleButton`/`RadioButton` has `Bounds.Height >= 32` and `Bounds.Width >= 32`, and a non-empty `AutomationProperties.Name` or text content. This extends the shell accessibility test from the redesign.
- Reduce motion: with it on, the Studio page host transition has zero duration (existing test, updated for the moved host), and switching views never creates a transition.
- Keyboard: `Ctrl+Shift+3` sets Glance, and focus lands inside the Glance view.

**Screenshots** (`RendersScreenshots`, both themes, same `SAMAAT_SCREENSHOTS` mechanism):
- Studio: the existing file names, unchanged.
- Focus: `focus-<device>.png` for each simulated device, plus `focus-buds-pro-sound.png` (a "More" page).
- Glance: `glance.png` (1180 px, grid) and `glance-narrow.png` (400 px or the minimum width).
- Table: `table.png`, `table-selected.png` (detail strip open) and `table-narrow.png` (700 px).
- Settings: `settings.png` re-rendered with the layout cards.

That adds about 11 files per theme. Each new set goes into `docs/images/` and into the ui-audit entry.

## 7. Phases and effort

The effort figures are rough, for one focused session including tests and screenshots.

| Phase | Scope | Effort |
|---|---|---|
| **0. Foundation** (after the redesign merges) | `DashboardView` + `Version` + lenient enum converter + migration tests; `SettingsViewModel.Dashboard`; `CurrentDashboard` / `SetDashboardCommand`; extract `StudioView`, `DeviceStage`, `BatteryFigures` with **no visual change** (screenshots identical); layout flyout in the sidebar and `Ctrl+Shift+1–4`; Settings radio cards (Focus/Glance/Table disabled with "Coming soon" until their phases land, or hidden; see Q7) | 1–1.5 days |
| **1. Glance** | `GlanceView`, forced compact rail, card template, 2-column grid, minimum-width change (Q2), tests + screenshots | 1 day |
| **2. Focus** | `FocusView`, large stage class, `QuickControls` part, "More" pages with Back, tests + screenshots | 1.5 days |
| **3. Table** | `DeviceTableViewModel` + tests, `TableView` with shared-size columns, sort headers, column dropping, two-line rows, detail strip, keyboard, screenshots | 2 days |
| **4. Polish and docs** | Copy pass, a stress render pass (long names, 6 devices, zero devices, disconnected, headset-only), ui-audit entry "Dashboard layouts (date)", STATUS.md UI section, README screenshot row | 0.5 day |

Total: about 6–6.5 days. Glance comes before Focus because it is the smallest view that is new for users, and it proves the all-devices host and the minimum-width change early. Each phase can merge on its own, because the switcher only offers views that exist.

## 8. Open questions for the user

1. **The four views.** Are Studio, Focus, Glance and Table the right four? Would you drop one or merge two, for example Glance and Table as one "All devices" view with a density toggle?
2. **Small window.** Glance is most useful at about 360–420 px wide, and today the window can't go below 640 px. Should the minimum width drop to 360 px for every view (Studio and Table would then rely on their narrow layouts), or only while Glance is active? Should picking Glance resize the window, or leave the size to you? Recommendation: lower the minimum for all views and never resize automatically.
3. **Focus "More".** Should Sound/Links/Details/Diagnostics open inside Focus (recommended: you stay in your chosen layout), or switch you to Studio on that tab?
4. **"Open" in Glance and Table.** Should "Open" take you to Focus or to Studio, or to "your last single-device layout"?
5. **Shortcut.** Is `Ctrl+Shift+1–4` fine, or would you prefer one key that cycles through the layouts (e.g. `Ctrl+Shift+L`)?
6. **Hardening.** May the lenient enum handling also cover `theme` and `accent`, so a bad value there no longer resets the whole settings file?
7. **Rollout.** Should Settings show unfinished layouts as "Coming soon" while the phases land, or show only the finished ones?
8. **Names.** "Studio", "Focus", "Glance" and "Table" are working names. Do you want plainer labels ("Full", "Simple", "Batteries", "List")?
9. **Later.** Should Glance become the tray flyout afterwards? It is the same template in a popup, and out of scope here.

## 9. Verification (per phase)

- `dotnet build Samaat.slnx` and `dotnet test Samaat.slnx` green, with warnings as errors.
- `SAMAAT_SCREENSHOTS=<dir> dotnet test tests/Samaat.App.Tests --filter "FullyQualifiedName~RendersScreenshots"`; review the new images in dark and light, and confirm Studio's images are byte-identical after Phase 0.
- Run on Windows with real earbuds and with `--simulate`. Switch views with the mouse, the flyout and the keyboard; restart and confirm the choice stuck; try Narrator on each view; turn Windows "Animation effects" off and on and confirm reduce motion follows.
- Docs: STATUS.md (UI), `docs/design/ui-audit.md` entry with before/after images.

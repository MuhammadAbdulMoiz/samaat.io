# UI audit and redesign: battery dashboard v0.1

## Design critique of v0.1

Screens reviewed: `docs/images/v0.1/battery-*.png` (before this redesign). Redesigned screens: `docs/images/*.png`.

### Overall impression
It works and is honest about capabilities, but it looks like a default admin template: grey cards, stock Fluent buttons, a blue selection colour that clashes with the teal accent, and no product identity. Nothing on screen says "earbuds". The battery numbers, which are the whole point of the screen, get the same weight as the surrounding chrome.

### Usability

| Finding | Severity | Recommendation |
|---|---|---|
| The alert banner for *Buds Lite* stays at the top while you look at *Buds Pro*, so it reads as if it belongs to the current device | 🔴 | Show the current device's alert inside its hero. Show other devices' alerts as dismissible toasts that name the device |
| On a single-level headset, three greyed-out tiles take more space than the one real reading | 🟡 | Hide unsupported components behind one compact "not reported" row of chips. Keep them visible (honesty) but small |
| The capability list repeats the battery tiles word for word | 🟡 | Show capabilities as compact chips with an icon and state. Group unavailable ones together |
| "Refresh battery" is a prominent grey button, although most sources push updates on their own | 🟢 | Make it an icon button with a tooltip |
| Nothing hints at the rest of the product (sound, gestures, multipoint) | 🟡 | Add a navigation rail with upcoming sections disabled and marked "Soon" |
| The device list shows only a percentage, with no connection state at a glance | 🟢 | Device chips with a connection dot and a mini battery bar |

### Visual hierarchy
- **What draws the eye first:** the yellow alert banner, then the blue list selection. ❌ It should be the earbuds and their charge.
- **Reading flow:** title → grey button → three equal tiles → a list that repeats them. No focal point.
- **Emphasis:** the support tier and battery source are tiny grey text, though they explain *why* things are greyed out.

### Consistency

| Element | Issue | Recommendation |
|---|---|---|
| Colour | Fluent's blue selection versus the teal brand accent | One accent token applied everywhere: selection, focus, charge |
| Buttons | Stock grey Fluent buttons | Custom subtle and icon button styles |
| Type | One family and weight everywhere, numbers not tabular | A display face for numbers and headings, Inter for body text, tabular figures |
| Surfaces | Pure white cards on light grey, no borders, no depth | Layered surfaces with hairline borders, dark-first |

### Accessibility
- **Contrast:** greyed tiles use 50 % opacity on muted text, about 2:1. ❌ Use explicit "disabled" colour tokens that stay at least 4.5:1 for the reason text.
- **Low battery shown by colour alone:** the percentage is always shown, and the redesign adds an icon and label for low and critical.
- **Targets:** buttons are 32 px or more. Fine.

### What works
- Every unavailable item explains why, and "unknown" is never shown as "unsupported".
- The "updated X ago" and stale states.

## Redesign direction: "Listening room"

References used:
- **Vendor companion apps:** Galaxy Wearable, Sony Headphones Connect and the Nothing X app all centre the *device* and put each bud's battery under its own bud. We take that layout pattern, not their visuals or branding.
- **Dribbble earbud-controller concepts:** dark dashboards with a hero device card and glanceable battery rings.
- **Avalonia theme libraries:** [SukiUI](https://github.com/kikipoulet/SukiUI) and [Semi.Avalonia](https://github.com/irihitech/Semi.Avalonia), for surface layering, hairline borders, and a nav rail plus content shell. We implement our own tokens instead of adopting either: SukiUI's glass look is itself generic, and Semi.Avalonia has moved to Avalonia 12.
- **[Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) (MIT):** the icons, embedded as path geometry.
- **[Space Grotesk](https://github.com/floriankarsten/space-grotesk) (OFL):** a display face for numbers and headings.

Principles:
1. **The device is the hero.** Vector buds and case, each wrapped in a battery ring, sit on a sound-wave motif (Samaat means "hearing").
2. **Dark-first with one signal colour.** Ink surfaces and a "signal lime" accent for charge. Amber means low, coral means critical, and each also gets an icon and a label.
3. **Honest but quiet.** Unsupported components shrink to chips. They don't disappear.
4. **The roadmap is visible.** The nav rail shows Sound, Controls and Connections as "Soon".

## Round 3: polish (2026-11-02)

| Area | Change |
|---|---|
| Hero artwork | Earbuds redrawn with an angled stem, eartip, speaker grille, gradient body and highlights. The case gets a gradient body. A charging badge appears on any ring that's charging. |
| Density | The four stat cards became one compact strip that wraps to 2×2 in narrow windows. |
| Alerts | Toasts hide themselves after 8 s. A top-bar "N low battery" chip keeps the state and jumps to the affected device. |
| Motion | Battery rings and device-card bars glide to new values (0.45 s ease-out), pages cross-fade (0.18 s), and a thin busy bar shows while a command waits for the device. |
| Narrow windows | The hero stacks below about 260 px of text width (`SplitPanel`). Stat and Info columns adapt (`AdaptiveColumns`), the device strip scrolls, and the minimum window width dropped from 860 to 640 px. |
| Accessibility | Automated WCAG AA contrast tests for every text/background token pair in both themes. They caught light-theme amber (4.14:1) and red (4.48:1) on alert pills, both fixed. Screen-reader names added to icon-only buttons and battery gauges. |

![Overview](../images/buds-pro.png) ![Narrow](../images/buds-pro-narrow.png)

## OpenMouse-style redesign (2026-11-03)

Benchmark: OpenMouse's design notes (ideas only; no code, assets or colours copied, as OpenMouse is AGPL). Plan: `docs/plans/2026-11-03-ui-redesign.md`.

| Area | Change |
|---|---|
| Navigation | A 240 px labelled sidebar (devices, alert counter, refresh, settings) replaces the icon rail and top strip. It collapses to a 68 px rail below 900 px. |
| Device page | A 260 px rail (name, artwork on the canvas inside the level ring, battery figure, status line) beside a workspace with a segmented tab well: Overview, Sound, Links, Details, Diagnostics. The rail stacks above the workspace below 760 px. |
| Content | Numbers first (label, value, unit, meta, tabular figures), "–" for unreported values, sentence case everywhere, flat unnested panels, switch rows. Capability chips live only in Diagnostics. |
| Shape | 4 px grid, 16 px panel padding and gaps, rows 40 px or more, buttons 32 px, switches 36x20, radii 6 / 10 / 14. |
| Theme | Accent presets Lime, Emerald, Ice and Mono, all checked against the WCAG AA tests in both themes. |
| Motion | New "Reduce motion" setting (follows Windows until changed): no page fade and no gliding values. |

## Links page rework (2026-11-04)

Goal: make the Links tab feel as considered as the Share audio card. It should work for earbuds with no multipoint protocol (most real devices, for example Quantum Zbuds) and for the simulator's multipoint Buds Pro. Reviewed with the design-critique, accessibility-review and ux-copy passes, plus stress renders (long names, six hosts, zero hosts, disconnected, busy with an error, no PC-link feature).

| Before | After | Why |
|---|---|---|
| "Disconnect" was a borderless text button floating top right. It read as a label | A `Button.subtle` with the disconnect icon, inside the state row. Connect is `Button.primary` | An action needs an affordance. Connect is the one thing a disconnected user came to do, so it gets the fill. Disconnect is reversible but not the main path, so it stays quiet |
| State, action and explanation sat in one flat block with no hierarchy | One row on the `Surface2` strip, like Share's "Playing on": a laptop tile that lights when connected, then a bold state ("Connected", "Connecting…"), one line of meaning, and the action | You read the state first, then what it means, then what you can do. The tile repeats the state without relying on colour alone |
| A long grey paragraph about other devices sat inside "This computer" | A separate "Devices" panel with an honest empty-state row: "Other devices aren't shown for these earbuds", plus "Manage them on each device" | Keeps "this computer" about this computer. The empty state says what's missing and where to go, in two short lines |
| A "Good to know" card of three grey sentences | A "How links work" disclosure (`ToggleButton.disclosure`), the same pattern as "How sharing works" | The limits are still always stated (PLAN §8) but no longer compete with live state. The "disconnected means no control" fact moved into the not-connected detail line, where it matters |
| Multipoint was its own card, then a "Connected devices" card | One "Devices" panel: a "2 of 2" count chip, the multipoint switch row, a divider, then host rows | One concept, one panel. The count chip mirrors Share's "1 of 3" |
| Host rows had `Button.primary` "Switch audio here" and boxed "Disconnect" on every row | `Border.shareRow` anatomy. The active host gets the accent row. Actions are `Button.ghost` with icons ("Switch here", "Connect", "Disconnect"), like Share | Several bright fills in a list shouted. Quiet actions keep the playing device as the focal point |
| A busy bar pushed the title down by 12 px (negative margins don't shrink a StackPanel slot) | The bar is overlaid on the panel's top edge | No layout jump while a request runs |
| Disabled Connect/Disconnect looked enabled | `Button.subtle:disabled` and `Button.primary:disabled` dim to 0.45, like a disabled switch. The headline reads "Connecting…" and the tooltip says "Wait for the current request to finish" | A disabled control says why |
| Zero hosts showed only a header and a divider | "No other devices yet. Devices you pair these earbuds with appear here." | An empty list needs an empty state |

**Accessibility.** Every button is 32 px or taller. The multipoint switch uses the `ToggleSwitch.share` template's 32 px hit area. Host buttons name their host ("Switch audio to Pixel phone", "Disconnect Pixel phone", "Connect Work laptop"). The locked multipoint switch carries `HelpText` and a tooltip ("These earbuds don't let Samaat change this"). Hidden host actions are explained in text when this computer is disconnected. Long host names trim with an ellipsis and keep the full name in a tooltip. No new colours: every text pair is already covered by the WCAG contrast tests.

**Copy.** Sentence case and verbs first. "Switch here" matches Share. A not-yet-connected host gets "Connect". The busy states are "Connecting…" and "Disconnecting…". The fallback button is "Bluetooth settings", and its row says "Connect or disconnect in Windows Bluetooth settings".

![Links, multipoint](../images/buds-pro-connections.png) ![Links, no multipoint](../images/buds-lite-connections-light.png)

## Overview, Details, Diagnostics, Settings and sidebar (2026-11-04)

Follow-up to the Links rework, using the same row anatomy throughout: a 32 px icon tile, a label over one line of meaning, and the control on the right.

| Before | After | Why |
|---|---|---|
| Sidebar brand: a small S square and the word "Samaat" | The S, drawn larger with round caps, on the accent key inside a soft accent halo. "Earbud companion" under the name, and a burger button that collapses or expands the sidebar | The mark reads as a logo, not a placeholder. The toggle makes the rail a choice, not only something narrow windows do |
| Collapsed rail only below 900 px | Burger toggle in both states ("Collapse sidebar" / "Expand sidebar"). The choice holds until the window crosses 900 px, then the width takes over again | A manual collapse never fights the narrow-window layout |
| Device rows were text only; the rail showed initials | Each row has a 36 px thumbnail: the device's own photo, a pair of bud glyphs, or headphones for a headset. The current device gets an accent edge and accent glyphs. The percentage moved onto the status line, so names get the full width | You recognise a device by its shape faster than by its name, and long names like "realme Buds T200 Lite" no longer cut off early |
| Overview battery: bare numbers in columns, a text Refresh button | One tile per component, with a bud or case glyph and a charge icon. Unreported tiles are outline only. A lone Overall tile stays tile-sized. "Updated …" sits next to an icon refresh button | Components read as objects. A single figure no longer stretches across the panel |
| Overview noise control and links were grey sentences | An icon-tile empty state for noise control. Links is one state row with "Manage ›", which opens the Links tab | Every card says its state, then where to act |
| Details was one ten-row table with "Support" in the middle | A header with an identification tile, then "Device" and "Connection" groups, selectable values, and copy buttons for address and IDs. Support became a shield row with the adapter note | Grouping matches how people look things up, and copying an address is a common need |
| Diagnostics: a header line, then a 16-row single-column list | A recognition header with Copy report and a "features available" meter. Working features show in a two-column grid. Technical details and the event log are separate disclosure panels with icons | Half the scroll length, and the summary is visible at a glance |
| Settings: plain rows, accent as a tab well of words | Icon-tile rows in Appearance, Battery alerts and Background. Accent is four 32 px round swatches with a ring and a check. Thresholds show as chips. A new About panel shows the mark and version | Settings scan by icon, and colour is picked by colour |

**Accessibility.** All new buttons are 32 px (icon buttons, swatches, copy). The burger, swatches and copy buttons have accessible names that match their tooltips. Battery tiles carry each component's accessible text. No new motion. Colours are tokens, except the fixed preset swatches, which have an outline so Mono stays visible on light backgrounds.

![Overview](../images/buds-pro.png) ![Collapsed sidebar](../images/buds-pro-sidebar-collapsed.png) ![Diagnostics](../images/buds-pro-info.png) ![Settings](../images/settings.png)

## Dashboard layouts and the device filter (2026-11-04)

Plan: `docs/plans/2026-11-04-dashboard-views.md`. There are three layouts, with Studio (the existing one) as the default. All three share the same view models, tokens and the shared `DeviceStage` and `BatteryFigures` parts. Original designs, no third-party templates.

| Layout | What it is for | What it shows |
|---|---|---|
| **Studio** (default) | Every control, one device at a time | Unchanged: device rail plus the Overview · Sound · Links · Details · Diagnostics tab well |
| **Focus** | One device, big and calm | A centred column with the name and status line, the artwork stage scaled up (it shrinks with the window), the battery tiles, and "Quick controls" (noise mode, Connect or Disconnect, Refresh). Sound, Links, Details and Diagnostics open in place under "More", with a short header and Back |
| **Glance** | Every listed device's battery, in a small window | One card per device: thumbnail, name, connection, low-battery chip, and each reading with a level bar ("–" when unreported). The sidebar becomes the icon rail. Cards go up to 3 columns when wide, and the window can shrink to 400 px |

| Before | After | Why |
|---|---|---|
| The sidebar listed every paired device, so a list of grey "Disconnected" rows hid the one in use | The list shows **connected devices** by default. A filter button beside Refresh offers Connected, Recent (connected in the last 7 days) and All paired. The heading names the filter ("Connected devices") | The common question is "what am I using now?". Paired-but-away devices are one click away, not gone |
| — | A selected device that disconnects stays listed until you pick another device or change the filter | Pressing Disconnect shouldn't make the page vanish under the cursor |
| — | When the filter hides everything, the page says so ("No earbuds are connected right now. 3 paired devices are hidden.") and offers "Show all paired devices" and Refresh | An empty list needs an empty state with a way out |
| Settings › Appearance had no layout choice | "Layout" row with three cards. Each has a small sketch of the layout drawn in the theme's own brushes, a name and one line of description. A "Device list" segmented row repeats the filter | One place to discover the options. The sketches follow theme and accent |
| — | A "Layout" button in the sidebar footer (current layout on the right) with a menu that lists the shortcuts. Ctrl+Shift+1, 2 and 3 switch from anywhere | Fast switching without leaving the page |

**Accessibility.** A test walks every visible button in each layout, for every simulated device: each one is at least 32 × 32 px and has a name. This found that the toast's Dismiss button was 28 px wide, and it is now 32. The layout roots are named ("Studio layout", "Focus layout", "Glance layout"). Cards read as "<name>, <battery>, <connection>", and each reading has its own name ("Left bud 90 percent"). "Open" says what it does ("Open Simulated Buds Pro with every control"). The filter button names its state ("Show devices: Connected"). After a switch, keyboard focus moves into the new layout if the old focus was hidden, without drawing a focus ring after a mouse click. Switching layouts never animates. The existing reduce-motion handling still covers the Studio tab fade and the value transitions, and no decorative motion was added.

**Settings.** `AppSettings` gains `Version` (2), `Dashboard`, `DeviceFilter` and `LastConnected`. Files without them load as Studio and Connected, and keep all their other values. Enums now read leniently: an unknown name, such as a newer build's layout or a typo in `theme`, falls back to that setting's default instead of setting the whole file aside as `settings.json.bad`.

![Focus](../images/focus-buds-pro.png) ![Glance](../images/glance.png) ![Glance, small window](../images/glance-narrow.png) ![No connected devices](../images/no-connected-devices.png)

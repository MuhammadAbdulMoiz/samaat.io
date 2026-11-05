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

## Phone app critique and accessibility audit (2026-11-05)

Benchmark: the desktop app itself. Run with the `design-critique` and `accessibility-review` skills against the first phone screens.

### Critique: what changed

| Finding | Severity | Change |
|---|---|---|
| The device page had no hero, breaking principle 1 | 🔴 | The desktop's stage: earbuds inside the battery ring on the sound-wave canvas, then the figure and status line |
| Low battery (17 %) was plain white | 🔴 | Amber or coral number, ring and bar, a warning icon, and "Right bud low · 17%" in words (principle 2) |
| Android 15+ forces edge to edge, so content would sit under the system bars | 🔴 | Safe-area insets: the content keeps clear and the navigation bar's surface reaches under the gesture bar |
| Two lime primaries on one screen, one of which would fail | 🟡 | One primary per screen: Connect hides while the computer holds the earbuds |
| Battery read "93%" in grey cells | 🟡 | The desktop's figure: label, value and muted unit, level bar, meta ("In use", "Low") |
| Options lacked icons, check marks and the status line | 🟡 | The desktop's option rows; EQ presets as rows ("Presets stored on the earbuds") |
| Device rows filled the tile with lime for "connected" | 🟡 | Neutral tiles: lime means charge or selection, and the dot carries connection |
| The nearby computer showed its IP address | 🟢 | "Ready to pair" |
| The paired Computer page was nearly empty | 🟢 | "Earbuds on {computer}" with where each pair is |
| No brand mark | 🟢 | The lime "S" tile beside "Samaat", as in the desktop sidebar |

**One design system.** Tokens, icons, fonts, `BatteryRing` and `SoundWaves` moved into `Samaat.UI`, and both apps include them from there, so the phone can't drift from the desktop.

### Accessibility (WCAG 2.1 AA)

| # | Issue | Criterion | Severity | Fix |
|---|---|---|---|---|
| 1 | A selected option's description (muted on accent-soft) was 4.45:1 in dark mode, **on the desktop too** | 1.4.3 | 🟡 | Selected rows use the text colour for the description, in both apps; tested |
| 2 | The phone's message dismiss button was 38 px wide | 2.5.5 | 🟡 | Ghost buttons have a 44 px minimum width |
| 3 | Low battery was shown by colour alone | 1.4.1 | 🔴 | Words, an icon and an accessible name ("Right bud, 17 percent, low") |

Measured in tests (`Samaat.Mobile.Tests`, now in [samaat-mobile](https://github.com/MuhammadAbdulMoiz/samaat-mobile)): every visible button on every phone screen is at least 44×44 and has a name, and 15 phone colour pairs pass in both themes (low and critical on the page, selected rows and tabs, alert pills, status bars on their track, the ring). The shared token pairs stay covered by the desktop's `ContrastTests`. Still to do by hand: TalkBack on a real phone, and 200 % font scale.

| Earbuds | Device | Pairing | Paired |
|---|---|---|---|
| ![](https://raw.githubusercontent.com/MuhammadAbdulMoiz/samaat-mobile/main/docs/images/mobile-devices.png) | ![](https://raw.githubusercontent.com/MuhammadAbdulMoiz/samaat-mobile/main/docs/images/mobile-device.png) | ![](https://raw.githubusercontent.com/MuhammadAbdulMoiz/samaat-mobile/main/docs/images/mobile-computer-code.png) | ![](https://raw.githubusercontent.com/MuhammadAbdulMoiz/samaat-mobile/main/docs/images/mobile-computer-paired.png) |

## One logo, one artwork (2026-11-05)

- **Logo:** the lime tile with a dark "S" made for the Android launcher is now the only logo: the desktop sidebar, the window and tray icon, the phone header and the launcher. It's defined once (`Samaat.UI.Logo`, `docs/images/logo.svg`) with **fixed colours**, so it no longer changes with the accent preset as the old sidebar box did.
- **Artwork:** the phone draws devices exactly as the desktop does. `ArtworkLook.Resolve` (Core) picks the look on both, the artwork styles live in `Samaat.UI/Styles/Artwork.axaml`, and the photos come from the same `devices/` folder (shipped in the APK and extracted on start). The phone has the desktop's **Change look** menu, saved per device.

| Logo | Split photo | Headset photo |
|---|---|---|
| ![](../images/logo.png) | ![](https://raw.githubusercontent.com/MuhammadAbdulMoiz/samaat-mobile/main/docs/images/mobile-device-artwork.png) | ![](https://raw.githubusercontent.com/MuhammadAbdulMoiz/samaat-mobile/main/docs/images/mobile-device-photo.png) |


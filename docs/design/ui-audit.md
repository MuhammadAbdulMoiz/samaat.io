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

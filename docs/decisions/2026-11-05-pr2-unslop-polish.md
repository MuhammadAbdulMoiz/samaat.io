# PR #2: unslop polish

**Status:** open, into `feature/ui-redesign` · **Date:** 2026-11-05 · **Branch:** `claude/unslop-polish`

## Why this work happened

- **The ask:** "Use AI unslop skill on this project."
- **The audit** (the unslop skill's sign list) found **light slop**: no gradient surfaces, gradient text, emoji in the UI, uppercase tracked labels or looping motion; one icon set; tested contrast. Light slop means polish mode: fix only what the audit found, take the direction from the existing brand, and keep the palette, type, layout and artwork.

## Direction (from the existing "Listening room" brand, written down rather than invented)

- **Product:** a Windows companion that shows battery for each piece, noise control and connections for earbuds from any brand, and says plainly what each pair can't do.
- **Character:** exact, candid, quietly technical.
- **World:** a listening room at night; ink-black gear with one lime signal light.
- **Moments:** the device stage and the battery figures.
- **Signature:** the battery ring around each piece.

## Decisions

| Decision | Why | Rejected alternative |
|---|---|---|
| Replace the sparkle icon (Simulation, Adaptive, Reduce motion) with Fluent `beaker`, `sound_wave_circle` and `slide_transition` | A sparkle reads as "AI magic" and explains nothing; same MIT 24 px set as the rest | Keep it |
| Selection is a neutral surface step plus an accent edge, glyph or check | Lime fills on every selected thing meant nothing stood out; the accent should mark one thing | Lime-tinted fills |
| Headings at weight 600 | Hierarchy comes from size; 700 headings are a slop sign | Keep 700 |
| No full stop on one-sentence captions | Captions are labels, not paragraphs; they were inconsistent | Mixed |
| Shared paddings and gaps moved onto the 4 px grid; alerts are 32 px tall | Rhythm; an alert now matches the height of the buttons beside it | Leave 6 and 10 px values |
| Keep the logo's glow | It is the shipped brand mark. The user didn't choose, and the skill keeps a real logo by default | Flatten it |
| Keep `Icon.Sparkle` defined in `Samaat.UI` | The phone app may still use it; the test only forbids its *use* in this app | Delete the geometry |

## Rules and procedures applied

- **Unslop skill (Damir Sokolovsky, CC BY 4.0):** audit, then a written direction, then the passes in order (spacing, colour, icons, copy, type, layout, details, visuals, motion), then the checklist.
- **Test-first:** guards for no emoji in `Samaat.App`, no sparkle in markup, contrast on the new selection surfaces, and the Diagnostics support line. They failed before the changes.
- **Verified by eye:** before and after screenshots of Overview, Sound, Settings and Diagnostics (light). Full suite: 985 tests.

## Suggestions not done

- Real artwork for the simulated devices; the lead screenshot shows grey "?" silhouettes.
- The overall battery level appears four times on Overview.

## Merge note

PR #4 is stacked on this branch. Merge this PR with **"Create a merge commit"**. Squash or rebase gives this commit a new identity, and #4 then conflicts in `ui-audit.md` and about 50 screenshot PNGs (tested).

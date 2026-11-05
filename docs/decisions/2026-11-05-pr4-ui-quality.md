# PR #4: worst-case data, stress-test fixes, motion review, component drawing

**Status:** open, into `feature/ui-redesign` (stacked on PR #2) · **Date:** 2026-11-05 · **Branch:** `claude/ui-quality`

## Why this work happened

- **The ask:** check that the user's `MuhammadAbdulMoiz/skills` and `dimensional-drawing` repos are installed in Samaat (all 18 skills were, byte-identical), then "run all three on one branch": `break-ui`, `review-animations` and `dimensional-drawing`. Then "fix all", and "All" for the motion findings.
- **The gap:** the UI had only been tested against three tidy demo devices; nothing exercised long, short or right-to-left names, missing fields or extreme readings.

## Decisions: worst-case data (break-ui)

| Decision | Why | Rejected alternative |
|---|---|---|
| Worst-case data enters through the simulator (`SimulatorScenario`: Demo, WorstCase, Empty) | The skill's rule: change the data at the same boundary as the demo data, never the component | Hand-editing views |
| Values are realistic, not random | Bluetooth names are user-editable (up to 248 bytes on Windows); vendor apps name earbuds after their owner; hosts arrive with their own names; many earbuds list a case they never report | `aaaa…` strings, which prove nothing |
| One failure per device | Breaks show side by side, as real data mixes them | Stacking everything on one device |
| The "toggle" is a dev-only CLI flag (`--simulate=worst`, `--simulate=empty`) plus a headless render test | A desktop app has no URL parameter; the test turns the data into a permanent regression check | A web-style toggle |
| Report before fixing; then "fix all" applied every fix that wasn't a decision | The skill's rule: some breaks are design decisions | Fixing silently |

**Fixes:**
- One toast at a time with "+N more", plus room below the last panel to scroll it clear.
- No "Updated" time or source until a level arrives.
- The caption names every piece without a level.
- Tooltips on truncated names in Focus and on audio-output names on Sound.

**Reverted:** capping the rail title at two lines. Avalonia's `MaxLines` drops the overflow without an ellipsis, so a cut name looked complete.

**Open decision:** "Overall" shows the lowest bud, e.g. 1 % beside a bud at 100 %. Labelling it "lowest bud" is undecided.

## Decisions: motion (review-animations)

The review blocked on two findings; all findings were then applied.

| Decision | Why |
|---|---|
| Tabs switch instantly (`PageTransition="{x:Null}"`; the control defaults to a fade) | High-frequency action; the linear 180 ms cross-fade overlapped two pages |
| `BatteryRing` drops its transition while its data context changes | Switching from 81 % to 17 % played a fake drain; a test caught the ring at 38 % two frames in |
| Strong ease-out spline (0.23, 1, 0.32, 1) for value glides, still 260 ms | The built-in cubic ease-out is weak (skill standard) |
| `Gauges.axaml` loads last; under Reduce motion the busy bar holds still | The reduce-motion rule for the level bars had been silently overridden by `Controls.axaml`; reduced motion removes movement and keeps comprehension |
| Press feedback: 0.97 scale over 160 ms, none under Reduce motion | Skill standard for pressable elements |

## Decisions: component drawing (dimensional-drawing)

- **What:** one sheet (`docs/design/drawings/list-row-tile-option.html`, plus a PNG preview) of the device row, figure tile and option row.
- **How it was measured:** from the rendered visual tree with a throwaway headless test, because the skill measures real geometry rather than the styles.
- **House rules followed:** monochrome, unbranded, slots instead of copy, every dimension a variable with formulas. Values not in the source are `[TBD]`: the meta line count and the touch minimum.
- **Facts it records:**
  - Rows are taller than their minimums because border and padding add up (54 against 52, and 50 against 48).
  - The thumbnail's corners (8) are rounder than its row's (6).
  - Tiles flex from 128 px in one to four columns.

## Other changes

- **A flaky test fixed, not skipped:** `RailShowsEachPartWithItsLevel` used the real clock and failed whenever a run took over 2 s; it now uses a fake clock.
- **Screenshots kept as committed after the motion fixes:** regenerating them changed 46 images, but only because the demo simulator drains on a real clock.

## Rules and procedures applied

- Skills: `break-ui` and `review-animations` (Emil Kowalski's standards), and `dimensional-drawing`.
- Test-first throughout. Every fix has a test that failed first. The ring test was first written so that it passed against the buggy code, so it was rewritten to catch the glide before the fix went in.
- Full suite before each push: 1,005 tests at the last commit.

## Open items

- Give `RendersScreenshots` a fake clock so the docs screenshots are identical on every run.
- Decide on the "Overall" label.

## Merge note

Merge PR #2 first, both with **"Create a merge commit"**. After #2 is in, this PR shows only its own three commits.

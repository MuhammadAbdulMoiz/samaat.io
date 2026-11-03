# Plan: compact Diagnostics tab and battery panel

Branch `feature/ui-redesign` (continues the OpenMouse-style redesign). Approved by the user from a critique and mockup.
Problem: the Diagnostics tab lists all 15 capabilities twice (chip cards, then the ledger), 14 of them "Couldn't detect"; Identity repeats the Details tab; Detection says "Not in the catalog" while the device is recognised by our profile DB; raw UUIDs and device ID on screen. The Overview battery panel shows three "– Not reported" columns for single-level devices.

## Changes
1. **Summary header**: "Diagnostics" title, meta line "<n> of <total> features available · <tier> support", Copy report button (unchanged behaviour).
2. **Recognition panel** replaces Identity + Detection cards: title "Recognised as <name>" when master's DeviceCatalog resolved a model (Recognised/Driver) **or** our DeviceProfileDb matched; otherwise "Not recognised". One meta line: how it matched (driver resolution text, or "Matched by name and vendor/product ID" from the profile) and the adapter state ("Samaat adapter: none yet" / driver name). Shows profile/catalog name, never both conflicting.
3. **One capability list** (replaces the three chip cards and the ledger):
   - Available capabilities first, one row each: name + meta "<source> · <access>" (+ "verified" when verified).
   - Unavailable capabilities grouped by module (Battery, Noise control, Connections): one row per module "<module label> · <k> features" with the shared reason (if all share one reason; otherwise "Not available"), expandable (Expander or toggle) to list the individual features with their own reason. Collapsed by default.
   - The Battery module's unavailable left/right/case collapse into one row "Left, right and case battery".
4. **Technical details** (collapsed by default): device ID, class of device, vendor/product ID, services as friendly labels (reuse `BluetoothServices` labels from Core; raw UUIDs only in the copied report). Remove the Identity card (the Details tab has it).
5. **Event log** (collapsed by default), with its count in the header row.
6. **Overview battery panel**: when only the combined level is reported, show just the Overall figure and one line "Left, right and case aren't reported for this device." instead of three dash columns. Devices with buds keep all four figures.
7. `BuildReport()` output unchanged in content (it may gain the profile match line); tests that check it keep passing.

Keep: view-model data (Ledger/Signals/Events) can stay as the source; add grouped view data rather than deleting public members tests rely on, unless a test is rewritten to the new behaviour. Sentence case, existing styles/tokens, accessible names on expanders and rows, reduce-motion (expanders must not animate when reduce-motion is on, or use no animation).

## Review fixes (Codex plan R1)
- `DeviceViewModel` passes its matched `DeviceProfile?` into `DeviceInfoViewModel`. The match explanation is generated from the populated `ProfileMatch` fields only: e.g. "Matched by name" (namePattern only), "Matched by name and vendor/product ID", "Matched by services and name". Catalog resolution keeps using master's `ResolutionText`.
- Raw diagnostic service UUIDs stay in `Signals` (used by `BuildReport()`); the collapsed Technical details uses a separate friendly-label collection. The report keeps every raw value.
- Accessibility: a headless UI test asserts each group expander and Technical details/Event log expander has a meaningful automation name, toggles with the keyboard (Space/Enter), and shows its content when expanded; capability rows have names.

- Tests (Codex plan R2): explanation text for name-only ("Matched by name"), name + VID/PID ("Matched by name and vendor/product ID") and services + name ("Matched by services and name") profiles; `BuildReport()` contains every original service UUID while Technical details shows only the friendly labels.

## Tests (first)
- Grouping: a Basic device yields 1 available row and collapsed groups for Battery (one "Left, right and case battery" row), Noise control (7) and Connections (4); a simulated Buds Pro yields mostly available rows and no collapsed Noise group.
- Summary text "1 of 15 features available · Basic support" for the Quantum-like device.
- Recognition: profile match alone → "Recognised as Zero Lifestyle Quantum Zbuds"; catalog match → its name; neither → "Not recognised"; no "Not in the catalog" text when a profile matched.
- Technical details services show labels (e.g. "HFP", "SPP", "Vendor (fe010000-…)") not raw 16-bit UUID strings.
- Overview: single-level device shows one figure + the reason line; buds device shows four figures.
- Existing uppercase-label, accessible-name and screenshot tests still pass; screenshots re-rendered.

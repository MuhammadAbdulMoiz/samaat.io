# Plan: Share audio card UX and visual pass

Branch `claude/share-card-ux` (from `feature/ui-redesign` at cb37255). The user asked to improve the UI/UX of the Sound page's Share audio card and make it visually appealing, using the design skills (emil-design-eng) and the house design system (`docs/design/ui-audit.md`, `src/Samaat.App/Styles/`). When done, it merges into `feature/ui-redesign` and then into `master`.

## Critique of the current card (emil-design-eng review format)

| Before | After | Why |
| --- | --- | --- |
| Rows are bare text with a default Fluent button and a ToggleSwitch whose On/Off text both say "Share" | Rows reuse the noise-mode row anatomy: a 32 px `optionIcon` tile, name plus a meta state line, actions on the right | It matches the cards above it; the eye learns one row shape per page |
| A sharing row looks the same as an idle one, apart from the slider appearing | A sharing row gets the selected treatment (`AccentSoft` background, accent edge, `optionIcon.on`) | State must be visible at a glance, not inferred from extra controls |
| "Playing on X" is a lone bold line | A source strip: a headphone or speaker icon tile, the label "Playing on" in meta, the name in semibold, and a small accent "Live" dot when anything is shared | It anchors the mental model: one source, copies fan out from it |
| Toggles silently disable at 3 | The header shows a count chip "2 of 3" while sharing; at 3 a meta note reads "Up to 3 devices at once" | A disabled control must say why (the house capability rule) |
| "Switch here" is a full default button on every row and competes with Share | A quiet ghost button (`Icon.Switch` plus "Switch here", muted text, accent on hover) | Primary vs secondary action hierarchy |
| The delay is a plain label above a full-width slider | Inside the sharing row: "Delay" on the left and the value in the number font on the right ("120 ms"), the slider below, and a meta hint "Add delay if this device plays ahead" | Values read best right-aligned in tabular figures; the hint answers "what is this for" |
| Errors and statuses are grey meta text that looks like help | Row and card statuses appear as an inline note with `Icon.Warning` on `WarnSoft` | Errors must look different from explanations |
| A three-sentence help paragraph is always shown | A "How sharing works" disclosure (existing `ToggleButton.disclosure` style), collapsed by default | Progressive disclosure keeps the card scannable |
| Unsupported and no-device states are plain text | An empty state with an icon tile and one line of guidance | Empty states deserve design too |
| No press feedback on row actions | Ghost buttons use the same press feedback as the existing option buttons (no new motion system). Reveals are instant: the delay area appears with the row state, with no height animation | The card is used tens of times a day, so no decorative motion. Honour reduced motion by adding none |

## Scope
1. **ShareTargetViewModel** adds `string IconKey` (from the output name: contains "speaker" → `Icon.Sound`; "headphone"/"headset"/"buds"/"earbuds" → `Icon.Headphones`; "tv"/"display"/"monitor"/"hdmi" → `Icon.Desktop`; else `Icon.Sound`), `string StateText` ("Sharing · {DelayMs} ms delay" when sharing with delay > 0, "Sharing" when sharing, otherwise "Not sharing"), `bool HasStatus`, and the accessible names `ShareLabel` ("Share to {Name}"), `SwitchLabel` ("Switch to {Name}") and `DelayLabel` ("Delay for {Name}"). `StateText` notifies when IsSharing or DelayMs change.
2. **AudioShareViewModel** adds `string DefaultIconKey` (same rule, from DefaultName), `string? SharingSummary` ("{n} of 3", null when 0), `bool IsAtLimit` (SharingCount == 3), and `bool IsLive` (SharingCount > 0). All notify when SharingCount or DefaultName change. `HelpText` stays (it moves into the disclosure).
3. **SoundPage.axaml**: rebuild only the Share audio panel per the table. New styles go in `src/Samaat.App/Styles/Controls.axaml` next to the existing ones: `Border.shareRow` (+ `.sharing`), `Button.ghost`, `Border.note` (warn), `Border.chip`, `Ellipse.liveDot`. Use only existing tokens (`Samaat.*`) and existing icons. Keep a 32 px minimum hit target. No change to the noise or EQ cards.
4. **Screenshots**: `MainWindowTests.RendersScreenshots` also captures `buds-pro-sound-share` (and `-light`): the Sound page scrolled to the Share card, with a fake share factory and switcher (the `FakeAudioShare`/`FakeSwitcher` from `AudioShareViewModelTests` made reusable). The fake has Speakers (sharing, 120 ms), "LG TV" (idle) and a "WH-1000XM5" headset (idle), with the default "Headphones (Quantum)". Refresh `docs/images` with the new captures.

## Tests
- VM: `IconKey_FromName` (Theory over the rule), `StateText_FollowsSharingAndDelay`, `SharingSummary_CountsAndHidesAtZero`, `IsAtLimit_AtThree`, `IsLive_WhenAnySharing`, `AccessibleLabels_IncludeName`.
- UI: the existing App tests and the screenshot test stay green. The new screenshot is reviewed by eye (controller) in dark and light.

## Review fixes (Codex plan R1)
- **Reuse house styles:** the count uses the existing `Border.chip` and statuses use the existing `Border.alert` (`Base.axaml`) with `Icon.Warning`. The only new styles are `Border.shareRow` (+ `.sharing`), `Button.ghost` and `Ellipse.liveDot`.
- **Toggle hit area:** a `ToggleSwitch.share` style sets `MinHeight="32"` and a transparent hit area, padded so the whole 32 px cell toggles. The switch keeps no On/Off content.
- **Why it's disabled:** `ShareTargetViewModel.DisabledReason` is "Up to 3 devices can be shared at once" when `!CanToggle`, else null. It's bound to the toggle's `ToolTip.Tip` and `AutomationProperties.HelpText`, and notifies when `CanToggle` changes. Test: `DisabledReason_AtLimit_ExplainsWhy`.
- **Screenshot harness:** `MainWindowTests.ShowAsync` gains optional `Func<IAudioShare>? audioShareFactory` and `IDefaultOutputSwitcher? outputSwitcher` parameters passed into `MainWindowViewModel`. The share capture uses them, sets the fake outputs, turns Speakers on with a 120 ms delay, settles, scrolls to the card, and captures.
- **Notification tests (optional, accepted):** the aggregate-property tests also assert `PropertyChanged` fires for `SharingSummary`, `IsAtLimit`, `IsLive` and each row's `CanToggle`/`DisabledReason` when slots change.

## Done when
`dotnet build Samaat.slnx` gives 0 warnings, `dotnet test Samaat.slnx` is green, the dark and light share screenshots are reviewed, and Codex has passed the implementation. Then merge into `feature/ui-redesign`, then into `master`.

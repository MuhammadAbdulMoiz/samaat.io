# Plan: retry vendor drivers, and an "unknown" look with a look picker

Branch `claude/buds-unknown` (from `feature/ui-redesign` at 7fbb245).

**User report (2026-11-04):** the realme Buds T200 Lite showed one Windows battery level ("from Windows", "Basic support") in the app, although the Fast Pair driver reads left and right. `samaat-cli` at the same moment showed "Full support, Left 80 %, Right 90 %". The user also asked for an OpenMouse-style "?" look for unknown devices, in two variants: one for devices without per-bud battery, and one for devices that report each piece. Users should also be able to pick an already-registered model's look for a device.

## Part A: retry a failed vendor driver

**Cause:** `WindowsDeviceSource.DiscoverAsync` reuses a cached device whenever it has an `IBatteryFeature`. When the driver's first open fails (buds paired but not connected at startup, channel busy, or BLE probes using up the 8 s budget), the device falls back to `OsBatteryFeature`, and from then on it is reused forever. Discovery only reruns when a paired device is added or removed, or on "Refresh devices", so the driver is never tried again until Samaat restarts.

**Fix:**
1. In Core, `src/Samaat.Core/Drivers/DriverRetry.cs`: `public static bool NeedsDriverRetry(IEarbudDevice device)` returns true when the device's `IDeviceDiagnostics.Resolution` is `Kind == Driver` with `ProtocolFallback == false` (a model whose own driver is declared) and `device.Identity.Tier != SupportTier.Full` (the driver gave nothing). Protocol-fallback probes (the Quantum) are not retried: they are guesses, and retrying them would cost a probe on every discovery.
2. In `WindowsDeviceSource.DiscoverAsync`, reuse a cached device only if it has a battery **and** `!DriverRetry.NeedsDriverRetry(existing)`; otherwise dispose it and rebuild it (the same path the no-battery case already uses).
3. When `CreateDeviceAsync` returns a device that needs a retry, subscribe to its `ConnectionChanged`. When it becomes `Connected`, call `RaiseIfEnumerated()` so the registry rediscovers and the driver gets another try. The rebuilt device replaces the old one, so the subscription goes with the disposed instance.
4. A failed retry must not loop: a rebuild happens only on discovery triggers (add/remove/refresh/connect transition), and a freshly built connected device raises no connect transition.

**Tests (Core):** `NeedsDriverRetry_ModelDriverWithoutFullTier_True`, `NeedsDriverRetry_FullTier_False`, `NeedsDriverRetry_ProtocolFallback_False`, `NeedsDriverRetry_NoDiagnostics_False`, `NeedsDriverRetry_GenericOrKnownModelNoDriver_False`. `WindowsDeviceSource` itself is WinRT glue with no test seam. It is checked on hardware: start Samaat with the T200 Lite in its case, take the buds out, and within a few seconds Left/Right should appear with "Full support".

## Part B: unknown look and look picker

**What the user sees:**
- A device without a photo shows an **unknown look** in the hero instead of the drawn buds/headset. The silhouettes use the existing glyphs (`Glyph.Bud`, `Glyph.Case`, `Icon.Headphones`) in the faint token colour, each with a small "?" badge, like OpenMouse's placeholder.
  - **Separate pieces** (the device reports left/right): left bud, right bud and case silhouettes in the same three-part layout as `ArtworkParts`, each with its own battery figure.
  - **Single** (combined battery only): one silhouette (the bud pair if it looks like earbuds, otherwise headphones) with a "?" badge and the overall figure.
- Under the hero artwork, a quiet **"Change look"** ghost button opens a menu:
  - **Automatic** (default): the matched profile's photo, otherwise the unknown look for the device's battery shape.
  - **Unknown · single** and **Unknown · separate pieces**.
  - Every registered model with a photo, by display name (e.g. "realme Buds T200 Lite", "ParadoX-E"). Choosing a model uses its photo, and its part photos when it has them and the device reports per-bud battery.
  - The current choice is checked. The choice affects artwork only, never the driver, name or capabilities.
- The choice is remembered per device id in `settings.json` as `artworkChoices` (`deviceId → "auto" | "unknown.single" | "unknown.pieces" | profileId`; "auto" is stored as absence).

**Code:**
1. `AppSettings.ArtworkChoices` (`IReadOnlyDictionary<string,string>`, default empty, null-safe in `Normalize`). `SettingsViewModel.GetArtworkChoice(id) → string?` and `SetArtworkChoice(id, string? choice)` (null removes the entry), saved through `Changed()`, the same pattern as the share delays.
2. `src/Samaat.App/Features/Overview/LookOptionViewModel.cs`: `record LookOptionViewModel(string Key, string Label, bool IsSelected)` plus a select command, or an equivalent the menu can bind to.
3. `DeviceViewModel`: the artwork becomes observable. It is resolved from (choice, matched profile, profiles list, battery shape) by one pure static method, `static ResolvedLook ResolveLook(string? choice, DeviceProfile? matched, IReadOnlyList<DeviceProfile> profiles, bool reportsBuds, bool looksLikeEarbuds)`, returning `{ Kind: Photo | PartsPhoto | UnknownSingle | UnknownPieces, Profile? }`. Properties: `ArtworkImage`, `ArtworkParts`, `ShowArtworkParts`, `ShowUnknownSingle`, `ShowUnknownPieces`, `LookOptions`, `ChangeLookCommand`. It re-resolves when the battery shape changes (existing update path) and when a look is chosen. The constructor takes the profiles list and the `SettingsViewModel` (optional for existing tests).
4. `MainWindowViewModel` passes `_profiles.Profiles` and `Settings` into each `DeviceViewModel`.
5. `MainWindow.axaml`: hero artwork area adds the two unknown layouts and the "Change look" button with a `MenuFlyout`. Existing tokens, glyphs and `Button.ghost` only. The menu items need accessible names.
6. Screenshots: the simulated Headset (no photo, combined battery) shows Unknown · single, and the simulated Buds Pro (per-bud) shows Unknown · separate pieces in the regenerated `docs/images`. Check that the simulated devices have no photo first; if they do, add one capture with the choice forced.

**Tests (App):** `ResolveLook_Auto_WithPhoto_UsesPhoto`, `ResolveLook_Auto_NoPhoto_BudsReported_UnknownPieces`, `ResolveLook_Auto_NoPhoto_Combined_UnknownSingle`, `ResolveLook_ChosenProfile_UsesThatPhoto`, `ResolveLook_ChosenProfileWithParts_AndBuds_UsesParts`, `ResolveLook_UnknownProfileId_FallsBackToAuto` (a stale choice for a removed profile), `ChangeLook_SavesChoice_AndUpdatesArtwork`, `LookOptions_ListAutoUnknownsAndPhotoProfiles_WithCurrentChecked`, and settings round-trip/null-safety for `artworkChoices`.

## Review fixes (Codex plan R1)
- **Keep the selection on rebuild:** `MainWindowViewModel.SyncDevices` records the selected device's `Identity.Id` before reconciling. Afterwards it selects the new VM with that id if one exists, and falls back to the first only when the id is gone. Test `SyncDevices_RebuiltSelectedDevice_KeepsSelectionById` (three devices, the second selected, the second replaced by a new instance with the same id).
- **Testable retry trigger:** in Core, `src/Samaat.Core/Drivers/DriverRetry.cs` also has `public static IDisposable WatchForReconnect(IEarbudDevice device, Action retry)`. It subscribes to `ConnectionChanged` and calls `retry` once per transition into `Connected` (tracking the last seen state, so repeated Connected events don't fire twice). Disposing it unsubscribes. `WindowsDeviceSource` keeps the returned handle per device id and disposes it when the device is rebuilt or removed. Tests (fake device): `WatchForReconnect_OneTransition_OneRetry`, `WatchForReconnect_RepeatedConnectedEvents_NoExtraRetry`, `WatchForReconnect_Disposed_NoRetry`, `WatchForReconnect_StartsConnected_NoRetryUntilNextTransition`.
- **Reuse decision:** `public static bool CanReuse(IEarbudDevice device)` (= has a battery && !NeedsDriverRetry) lives in `DriverRetry`, and `WindowsDeviceSource` calls it. Tests: `CanReuse_DriverFailed_False`, `CanReuse_DriverWorked_True`, `CanReuse_NoBattery_False`.
- **Picker eligibility:** only profiles with `Image != null` are listed. Part photos are used only together with a full photo.

## Review fixes (Codex plan R2)
- `WindowsDeviceSource.DisposeAsync` disposes and clears every retry-watch handle before disposing the cached devices.
- Test seam for selection: `MainWindowViewModel` gains an optional `IReadOnlyList<IDeviceSource>? sources = null` ctor parameter (default `options.CreateSources(...)`, as today). The test passes a small fake `IDeviceSource` whose `DiscoverAsync` result the test changes (replacing the second device with a new instance of the same id) and whose `DevicesChanged` it raises, then asserts the selected VM is the replacement.

## Done when
`dotnet build Samaat.slnx` gives 0 warnings, `dotnet test Samaat.slnx` is green, the screenshots are reviewed in dark and light, Codex has passed the implementation, and the user has checked Part A on hardware. Merge into `feature/ui-redesign`.

# Share Audio Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Play whatever Windows is playing on the default output on a second output at the same time, for example a second pair of Bluetooth earbuds.

**Architecture:** Samaat copies the audio itself. A WASAPI loopback capture of the default render endpoint (role `Multimedia`) is converted to float, channel-mapped to the target's layout, and fed to a playout buffer. That buffer feeds a resampler and a WASAPI shared-mode output on the chosen second endpoint. The buffer's fill target sets the extra delay, and it corrects clock drift by dropping or repeating single frames. Everything that can be platform-free lives in Core: the sample conversion, the buffer, and the share state machine `AudioShare`, which runs against an `IAudioBackend` seam. `Samaat.Platform.Windows` only supplies `WasapiBackend`. The UI is one card on the Sound page.

**Tech Stack:** .NET 10, Avalonia 11.3, CommunityToolkit.Mvvm, NAudio.Wasapi 2.2.1 + NAudio.Core 2.2.1 (MIT, Windows TFM only), xUnit.

**Spec:** this file (from the conversation on 2026-11-03: "can we stream to both?" → "add this feature and broadcast the audio").

## Global Constraints

- Windows only. The `net10.0` (simulator) build gets `NoAudioShare`, and the card shows "Sharing audio needs Windows."
- NAudio is referenced only by `Samaat.Platform.Windows`. Core and App don't reference it.
- "Default output" always means `GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)`. Capture, target exclusion and default-change handling all use that one role. Default-changed notifications for other roles or for capture flow are ignored.
- Never offer or accept the current default output as the target. Copying it into itself would loop.
- The delay is 0–500 ms in 10 ms steps, default 0, and is saved in `AppSettings`.
- Sharing never starts on its own at launch. Only the target and the delay are remembered.
- Warnings are errors (`Directory.Build.props`). `dotnet build Samaat.slnx` and `dotnet test Samaat.slnx` must be green before each commit.
- Out of scope: LE Audio/Auracast (Windows 11 "Shared audio" already covers that), more than one extra target, per-app capture, lowering the latency of the default output.

## Known limits (shown in the card's help text, not fixed)

- The copy always lags the source by about the capture period plus the buffer target plus the output latency. The delay slider can only add delay to the copy. If the second pair is behind, make the *other* pair the default output and share to this one.
- Apps that use exclusive mode, and some DRM-protected playback, are not captured. The copy is silent for them.
- If either headset switches to its hands-free profile (mic in use), its audio quality drops for both.

## Review Focus

1. **The default output changes while sharing.** If the new default is the target → `Stopped`, status "Stopped: <name> became the default output." Otherwise restart the pipe from the new default and keep `Sharing`. (Task 3 tests.)
2. **The target disconnects or turns off mid-share** → `Stopped`, status "Stopped: <name> disconnected", no crash. (Task 3 + Task 4 tests.)
3. **The formats differ** (PCM16/24/32 or float capture; 44.1 kHz vs 48 kHz; 6-channel source to a stereo or mono hands-free target) → correct float samples, the first two channels taken, the rate resampled. (Task 1 converter tests; resampling is NAudio's `WdlResamplingSampleProvider`, checked on hardware in Task 6.)
4. **Nothing is playing**: loopback sends no packets, so the buffer must not count silence as drift, and playback resumes in sync when sound starts again. (Task 1 `Underrun_PlaysSilence_AndRefillsToTarget`.)
5. **Late callbacks and races**: a `Stopped` callback from an old pipe arrives after a restart; Start/Stop/Dispose are called concurrently. Stale callbacks are ignored, and the newest session survives. (Task 3 tests.)

---

### Task 1: Sample conversion and playout buffer (Core)

**Files:**
- Create: `src/Samaat.Core/Audio/SampleConvert.cs`
- Create: `src/Samaat.Core/Audio/PlayoutBuffer.cs`
- Test: `tests/Samaat.Core.Tests/SampleConvertTests.cs`, `tests/Samaat.Core.Tests/PlayoutBufferTests.cs`

**Interfaces:**
- Produces:
  - `public enum SampleEncoding { Pcm, Float }`
  - `public static class SampleConvert`
    - `int ToFloat(ReadOnlySpan<byte> raw, SampleEncoding encoding, int bitsPerSample, Span<float> dest)`: supports Pcm 16/24/32 and Float 32. It processes only whole samples and returns the count written. Anything else → `NotSupportedException`. (The backend resolves `WAVE_FORMAT_EXTENSIBLE` to its subformat before calling.)
    - `int MapChannels(ReadOnlySpan<float> src, int srcChannels, Span<float> dest, int destChannels)`: dest 1 → average of the first two source channels (or the only one); dest 2 → the first two (mono duplicated). Only `destChannels` 1 or 2 are allowed. Returns the samples written.
  - `public sealed class PlayoutBuffer(int sampleRate, int channels, int baseLatencyMs = 60)` with
    - `int DelayMs { get; set; }` (clamped 0–500; fill target = `baseLatencyMs + DelayMs`)
    - `void Write(ReadOnlySpan<float> interleaved)` (from the capture thread)
    - `int Read(Span<float> interleaved)` (from the output thread; always fills the whole span and returns its length)
    - `int FillMs { get; }`, `long DroppedFrames { get; }`, `long RepeatedFrames { get; }`
    - Thread-safe through one `lock`. Capacity is 2 s; on overflow it drops the oldest frames.

Buffer rules: while priming (after creation, after an underrun, or after `DelayMs` changes), `Read` returns silence until the fill reaches the target. When the fill is above target + 20 ms, drop one frame per 1000 frames read. When it's below target − 20 ms (and not priming), repeat one frame per 1000. An underrun (fill 0) → silence, then priming again.
`// shortcut: drop/repeat single frames, can click on pure tones; upgrade to adaptive resampling if users hear it`

- [ ] **Step 1: Failing tests**
  - Convert: `Pcm16_ToFloat` (`0x7FFF` → ≈1.0, `0x8000` → −1.0, `0` → 0); `Pcm24_ToFloat` (3-byte little-endian, `0x7FFFFF` → ≈1.0); `Pcm32_ToFloat`; `Float32_PassesThrough`; `PartialTrailingSample_Ignored` (5 bytes of PCM16 → 2 samples); `Pcm8_Throws`; `Map_SixToStereo_TakesFirstTwo`; `Map_StereoToMono_Averages`; `Map_MonoToStereo_Duplicates`.
  - Buffer: `Primes_UntilTargetReached` (48 kHz stereo, base 60: writing 50 ms then reading gives zeros; after 20 ms more, reads return the written samples in order); `Underrun_PlaysSilence_AndRefillsToTarget`; `Overfull_DropsFrames` (fill held at target + 100 ms for 10 s of reads → `DroppedFrames > 0`, `FillMs` ends within ±20 of target); `Underfull_RepeatsFrames`; `DelayMs_IsClamped` (−5 → 0, 900 → 500); `Overflow_DropsOldest` (write 3 s, then read: the first sample read is from second 1).
- [ ] **Step 2: Run them, expect a compile failure.** `dotnet test tests/Samaat.Core.Tests --filter "SampleConvert|PlayoutBuffer"`
- [ ] **Step 3: Implement both classes.**
- [ ] **Step 4: Run them, expect PASS.**
- [ ] **Step 5: Commit** `feat(audio): sample conversion and playout buffer`

### Task 2: Contract, backend seam, settings (Core)

**Files:**
- Create: `src/Samaat.Core/Audio/AudioShareContracts.cs`
- Modify: `src/Samaat.Core/Settings/AppSettings.cs` (add `ShareTargetId` and `ShareDelayMs`, clamped in `Normalize`)
- Test: `tests/Samaat.Core.Tests/SettingsTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record AudioOutput(string Id, string Name, bool IsDefault);
  public enum AudioShareState { Off, Starting, Sharing, Stopped }

  public interface IAudioShare : IAsyncDisposable
  {
      bool IsSupported { get; }
      IReadOnlyList<AudioOutput> Outputs { get; }   // active render endpoints, the default flagged
      AudioShareState State { get; }
      string? TargetId { get; }
      string? StatusText { get; }                     // why it stopped or failed; null while fine
      int DelayMs { get; set; }
      event EventHandler? Changed;                    // may fire on any thread
      Task StartAsync(string targetId, CancellationToken ct = default);
      Task StopAsync();
  }
  public sealed class NoAudioShare : IAudioShare  // IsSupported false, empty Outputs, StartAsync throws NotSupportedException

  // What the platform supplies. AudioShare (Task 3) is the only caller.
  public interface IAudioBackend : IDisposable
  {
      IReadOnlyList<AudioOutput> GetOutputs();        // default = Render/Multimedia
      event EventHandler? OutputsChanged;             // added/removed/state/default (Render+Multimedia only)
      IAudioPipe Open(string sourceId, string targetId, int delayMs); // throws on failure
  }
  public interface IAudioPipe : IDisposable
  {
      int DelayMs { set; }
      event EventHandler<Exception?>? Stopped;        // capture or output ended; null = normal stop
  }
  ```
  The pipe creates its own `PlayoutBuffer`, because only the backend knows the capture rate and target channels. `AudioShare` keeps `DelayMs` and passes it on each Open.
- `AppSettings`: `string? ShareTargetId` (default null), `int ShareDelayMs` (default 0, clamped 0–500).

- [ ] **Step 1: Failing tests**: `NoAudioShare_IsUnsupported_AndEmpty` (new `AudioShareTests.cs`); `Settings_ShareDelay_ClampedOnNormalize` (−1 → 0, 501 → 500); `Settings_ShareFields_RoundTrip` through `SettingsStore` in a temp file.
- [ ] **Step 2: Run them, expect failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them, expect PASS.**
- [ ] **Step 5: Commit** `feat(audio): audio share contract, backend seam and settings`

### Task 3: Share state machine (Core)

**Files:**
- Create: `src/Samaat.Core/Audio/AudioShare.cs`
- Test: `tests/Samaat.Core.Tests/AudioShareTests.cs` with `FakeAudioBackend`/`FakeAudioPipe` in `tests/Samaat.Core.Tests/Fakes.cs`

**Interfaces:**
- Consumes: `IAudioBackend`, `IAudioPipe`, `IAudioShare` (Task 2).
- Produces: `public sealed class AudioShare(IAudioBackend backend) : IAudioShare` (`IsSupported` = true).

Lifecycle rules (the R1 race fix):
- Every state change runs under one `SemaphoreSlim(1,1)`: Start, Stop, Dispose, `OutputsChanged`, and pipe `Stopped`. Event handlers do `_ = HandleAsync(...)`, which waits on the semaphore. They never touch state directly.
- Each opened pipe gets an incrementing `generation`. Its `Stopped` handler captures that number, and when the generation no longer matches it is ignored.
- **Retiring a pipe** (Stop, restart, replace, dispose) always increments `generation` and unsubscribes `Stopped` *before* calling `pipe.Dispose()`, so a callback raised by the disposal itself is stale.
- `StartAsync` while a pipe is open retires the open pipe first, then opens the new one, so there is never more than one pipe.
- Start caches the target's `Name` (`_targetName`) for status text, because a removed endpoint no longer appears in `GetOutputs()`.
- `backend.Open` runs inside `Task.Run` (thread pool = MTA, which WASAPI/COM needs; the caller may be the STA UI thread).
- `StartAsync(target)`: the target is missing or is the default → `Stopped` + status, no Open. Open throws → `Stopped` + "Couldn't start: {message}". Otherwise `Starting` → `Sharing`.
- `OutputsChanged` while sharing: the target is gone or inactive → dispose the pipe, `Stopped`, "Stopped: {name} disconnected". The target became the default → `Stopped`, "Stopped: {name} became the default output". The default changed to something else → dispose and reopen from the new default (generation++), and stay `Sharing`.
- Pipe `Stopped(ex)` (current generation) → `Stopped`, "Stopped: {ex.Message}" (or "Stopped." if null).
- `DelayMs` set → stored and pushed to the current pipe.
- `DisposeAsync` is idempotent: it stops the pipe, unsubscribes and disposes the backend. Calls after dispose are no-ops.

- [ ] **Step 1: Failing tests**: `Start_OpensFromDefaultToTarget`; `Start_RejectsDefaultAsTarget_WithoutOpening`; `Start_OpenThrows_StoppedWithStatus`; `TargetRemoved_Stops_WithDisconnectedStatus`; `TargetBecomesDefault_Stops`; `DefaultChanged_ReopensFromNewDefault_StaysSharing`; `StalePipeStopped_AfterRestart_IsIgnored`; `StoppedRaisedByDispose_OnStop_StaysOff` (the fake pipe raises `Stopped` inside `Dispose`); `StoppedRaisedByDispose_OnReopen_StaysSharing`; `StartWhileSharing_ReplacesPipe_OneOpen`; `TargetRemoved_StatusUsesCachedName`; `ConcurrentStartStop_EndsConsistent` (100 interleaved Start/Stop tasks → state is `Sharing` with exactly one open pipe, or `Off` with none); `Delay_PushedToPipe_AndKeptAcrossReopen`; `Dispose_IsIdempotent_AndDisposesBackend`.
- [ ] **Step 2: Run them, expect failure.** `dotnet test tests/Samaat.Core.Tests --filter AudioShare`
- [ ] **Step 3: Implement `AudioShare`.**
- [ ] **Step 4: Run them, expect PASS.**
- [ ] **Step 5: Commit** `feat(audio): audio share state machine`

### Task 4: WASAPI backend (Platform.Windows)

**Files:**
- Modify: `Directory.Packages.props` (`NAudio.Wasapi` and `NAudio.Core` at `2.2.1`)
- Modify: `src/Samaat.Platform.Windows/Samaat.Platform.Windows.csproj` (package refs)
- Create: `src/Samaat.Platform.Windows/WasapiBackend.cs`
- Modify: `THIRD_PARTY_NOTICES.md` (NAudio, MIT)

**Interfaces:**
- Consumes: `IAudioBackend`, `IAudioPipe`, `AudioOutput` (Task 2); `SampleConvert`, `PlayoutBuffer` (Task 1).
- Produces: `public sealed class WasapiBackend : IAudioBackend`.

Behaviour:
- `GetOutputs`: `MMDeviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)`, `Name = FriendlyName`, `IsDefault` from `GetDefaultAudioEndpoint(Render, Multimedia).ID`.
- An `IMMNotificationClient` registered once raises `OutputsChanged` for add/remove/state change, and for `OnDefaultDeviceChanged` only when `flow == Render && role == Multimedia`.
- `Open(source, target, delayMs)`: `WasapiLoopbackCapture(sourceDevice)`. The encoding comes from `WaveFormat.Encoding`; for `Extensible` it comes from `WaveFormatExtensible.SubFormat` (`MEDIASUBTYPE_IEEE_FLOAT` → Float, `MEDIASUBTYPE_PCM` → Pcm); anything else throws `NotSupportedException("Unsupported capture format")`. `DataAvailable` → `SampleConvert.ToFloat` → `MapChannels` to `min(targetMixChannels, 2)` → `PlayoutBuffer(captureRate, outChannels, 60) { DelayMs = delayMs }`. Output: a private `ISampleProvider` that reads from the buffer → `WdlResamplingSampleProvider` to the target mix rate when the rates differ → `WasapiOut(targetDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 50)`. `RecordingStopped`/`PlaybackStopped` → the pipe's `Stopped(e.Exception)`, raised once.
- If the target mix format has more than 2 channels, the output is still stereo (WASAPI shared mode up-mixes).
- Pipe `Dispose` stops and disposes the capture and the output and unsubscribes, and is idempotent. Backend `Dispose` unregisters the notifier.
- The scratch byte/float arrays are reused per callback. `// shortcut: Trace logs Dropped/RepeatedFrames every 60 s; upgrade to a diagnostics view if drift reports come in`

- [ ] **Step 1: Run `dotnet build Samaat.slnx`.** No automated test here: the logic is covered by Task 1/3, and this class is only NAudio glue, checked on hardware in Task 6.
- [ ] **Step 2: Implement `WasapiBackend`.**
- [ ] **Step 3: Run `dotnet build Samaat.slnx` and `dotnet test Samaat.slnx`, expect green.**
- [ ] **Step 4: Commit** `feat(audio): WASAPI loopback backend`

### Task 5: View model, wiring, UI (App)

**Files:**
- Modify: `src/Samaat.App/AppOptions.cs` (`IAudioShare CreateAudioShare()`: Windows → `new AudioShare(new WasapiBackend())`, else `new NoAudioShare()`, same `#if SAMAAT_WINDOWS` pattern as `CreateStartupRegistration`)
- Create: `src/Samaat.App/Features/Sound/AudioShareViewModel.cs`
- Modify: `src/Samaat.App/Shell/MainWindowViewModel.cs` (owns one `AudioShareViewModel`, takes an optional `IAudioShare` ctor arg for tests like the other platform args, sets it on each `DeviceViewModel.Share`, disposes it in `DisposeAsync`)
- Modify: `src/Samaat.App/Features/Overview/DeviceViewModel.cs` (a `Share` property it references but doesn't own or dispose)
- Modify: `src/Samaat.App/Features/Sound/SoundPage.axaml` (a "Share audio" panel after Equalizer, same panel classes)
- Test: `tests/Samaat.App.Tests/AudioShareViewModelTests.cs` with a `FakeAudioShare : IAudioShare`

**Interfaces:**
- Consumes: `IAudioShare` (Task 2), `SettingsStore`/`AppSettings` (existing).
- Produces: `public sealed partial class AudioShareViewModel(IAudioShare share, SettingsStore store) : ObservableObject, IAsyncDisposable` with
  - `IReadOnlyList<AudioOutput> Targets` (= `Outputs` minus the default, refreshed on `Changed`)
  - `AudioOutput? SelectedTarget`: the saved `ShareTargetId` if present, else the first target. When the selected one vanishes, it reselects the same way. Choosing a target saves `ShareTargetId`.
  - `bool IsSharing` (two-way; true → `StartAsync(SelectedTarget.Id)`, false → `StopAsync`; reflects `State == Sharing || Starting`)
  - `int DelayMs` (two-way; pushes to `share.DelayMs` live, saves `ShareDelayMs`)
  - `string? Status`, `bool IsSupported`, `bool HasTargets`
  - Marshals `Changed` to the UI thread with `Dispatcher.UIThread.Post`.

Copy: title "Share audio". Toggle "Also play on". Slider "Delay the copy · {n} ms". Help: "Plays everything from {default name} on a second output too. If the copy is ahead, add delay. If it's behind, make the other device the default and share to this one." No targets: "Connect a second audio device to share to it." Unsupported: "Sharing audio needs Windows."

- [ ] **Step 1: Failing tests**: `Targets_ExcludeDefault`; `Toggle_On_StartsWithSelectedTarget`; `Start_Failure_ShowsStatus_AndTurnsToggleOff`; `Share_StoppedByDevice_TurnsToggleOff`; `Delay_Change_PushedAndSaved`; `SavedTarget_Missing_FallsBackToFirst`; `SelectedTarget_Removed_Reselects`; `Unsupported_ShowsWindowsOnlyText`; `Dispose_StopsSharing`.
- [ ] **Step 2: Run them, expect failure.** `dotnet test tests/Samaat.App.Tests --filter AudioShare`
- [ ] **Step 3: Implement the VM, the wiring and the panel.**
- [ ] **Step 4: Run `dotnet build Samaat.slnx` and `dotnet test Samaat.slnx`, expect all green.**
- [ ] **Step 5: Commit** `feat(audio): Share audio card on the Sound page`

### Task 6: Hardware check + docs

**Files:**
- Modify: `docs/STATUS.md` (a Share audio line)
- Create: `docs/modules/audio-share.md` (what it does, the known limits above, the hardware result)

- [ ] **Step 1: Hardware check with the user** (two headsets connected, for example the ParadoX-E as the default and the T200 Lite as the target): music plays on both; changing the delay is audible; turning the target off → the card says it stopped; switching the default in Windows → it follows or stops per Review Focus 1; a 44.1 kHz source with a 48 kHz target sounds right (pitch unchanged); 10 minutes of playback → there is no audible drift (check the Trace counters).
- [ ] **Step 2: Record the results in `docs/modules/audio-share.md`, update STATUS.**
- [ ] **Step 3: Commit** `docs: share audio module and hardware result`

## Review fixes (Codex plan R1)
- Capture bytes are decoded by format (`SampleConvert.ToFloat`, PCM 16/24/32 + float, Extensible resolved by subformat), with tests.
- Channel mapping is a Core function that takes the first two of N channels; NAudio's stereo/mono providers are no longer used.
- Lifecycle is serialized through one semaphore in `AudioShare`, and stale pipe callbacks are dropped by generation.
- The state machine moved to Core behind `IAudioBackend`/`IAudioPipe`, so default-switch, disconnect, start-failure, race and dispose paths are unit-tested with fakes.
- "Default" = Render/Multimedia everywhere.
- Optional (accepted): the name-match target fallback is removed; the saved target, else the first.

## Review fixes (Codex plan R2)
- A pipe is retired by bumping the generation and unsubscribing before `Dispose`, so callbacks raised by the disposal are stale. Start while sharing replaces the pipe. Tests added for both.
- The target name is cached at Start for the disconnect and default-change status.
- The hardware reference in Review Focus 3 now points to Task 6.

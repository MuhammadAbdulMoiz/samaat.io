# Module: Share audio and Switch here

Status: **implemented**, Windows only. Checked on one PC with a Bluetooth headset (Zero Lifestyle Quantum) and the laptop speakers.

## What it does

- The **Share audio** card on the Sound page shows "Playing on {default output}". It has one row for every other active output.
- **Share** plays everything from the default output on that row's device too, on up to **3** devices at once. The other toggles are disabled while 3 are on. Each device has its own **delay** slider (0–500 ms, 10 ms steps), remembered per device in `settings.json` (`shareDelays`). Sharing never starts by itself at launch.
- **Switch here** makes that output the Windows default (console, multimedia and communications), with no sharing. Shares that are running follow the new default. A device that is being shared to and becomes the default drops out of the list and its share stops, because copying a device into itself would loop.
- A share stops with a message when its device disconnects ("Stopped sharing to {name}: disconnected"), and when the device becomes the default.

## How it works

The default output (Render, Multimedia role) is captured with WASAPI loopback. The capture is converted to float and mapped to at most 2 channels, then goes into a playout buffer per target. The buffer's fill target is 60 ms plus that device's delay, and it corrects clock drift by dropping or repeating one frame per 1000. From the buffer the audio is resampled to the target's mix rate and played in shared mode. Each target has its own loopback capture and its own `AudioShare` state machine. Start, stop and device changes are serialized there, and stale callbacks are dropped by generation. Switch here uses the undocumented `IPolicyConfig` COM interface, which other sound-switcher tools also use.

## Known limits

- The copy always lags the source a little. The delay slider can only delay the copy. If a copy is behind, make that device the default and share to the other one.
- Apps using exclusive mode, and some DRM-protected playback, aren't captured.
- If a headset's mic is in use (hands-free profile), its audio quality drops for everyone listening.
- Most Bluetooth adapters can't play to more than two headsets at once.
- `IPolicyConfig` is undocumented. If a Windows update breaks it, Switch here shows "Couldn't switch: …".

## Code

| Piece | Where |
|---|---|
| Sample conversion, playout buffer | `src/Samaat.Core/Audio/SampleConvert.cs`, `PlayoutBuffer.cs` |
| Contracts, state machine (one target) | `src/Samaat.Core/Audio/AudioShareContracts.cs`, `AudioShare.cs` |
| WASAPI loopback backend (NAudio) | `src/Samaat.Platform.Windows/WasapiBackend.cs` |
| Default output switcher | `src/Samaat.Core/Platform/IDefaultOutputSwitcher.cs`, `src/Samaat.Platform.Windows/WindowsDefaultOutputSwitcher.cs` |
| Card and rows | `src/Samaat.App/Features/Sound/AudioShareViewModel.cs`, `ShareTargetViewModel.cs`, `SoundPage.axaml` |
| Plan and review history | `docs/plans/2026-11-03-audio-share.md` |

## Tests

`SampleConvertTests`, `PlayoutBufferTests` and `AudioShareTests` in Core cover format conversion, delay and drift, start/stop races, default changes, disconnects and disposal. `AudioShareViewModelTests` covers the 3-device limit, quick toggles, Switch here, rows leaving the list, and disposal. The WASAPI backend and the COM switcher are glue, checked by hand.

## Hardware result (2026-11-04)

With the Quantum as default and the laptop speakers as the target, audio played on both. There was no audible delay between them and no pitch change. Switch here moved audio between the Quantum and the speakers in both directions, and Share kept working alongside it. Not tried yet: two Bluetooth headsets at once, three targets, and a long drift run.

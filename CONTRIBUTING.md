# Contributing to Samaat

## Workflow

We work test-first (see [docs/STATUS.md §4](docs/STATUS.md#4-working-on-samaat-tdd)): write a failing test, make it pass, then refactor. Run `dotnet test Samaat.slnx` before every commit. CI runs it on Ubuntu and Windows.

## Adding or changing device support

Device support is only as good as its evidence. A catalog entry may set `"verified": true` only when the pull request includes:

1. **The model and firmware**: exact retail name, the firmware version shown by the vendor app, the region if it matters.
2. **A Samaat report**: Info page → **Copy report** (or `samaat-cli --watch` output), with the device connected.
3. **For a driver**: recorded byte traces of each command the driver sends and the device's replies, committed under `traces/<vendor>/` and replayed in tests.
4. **A manual checklist**, completed on real hardware:
   - [ ] Device is listed and recognised as the right model
   - [ ] Battery matches the vendor app (each component the model reports)
   - [ ] Every exposed control changes the device, and the change survives a reconnect
   - [ ] Changes made on the earbuds show up in Samaat
   - [ ] Unsupported features show the right reason
   - [ ] Disconnect / reconnect doesn't crash or leave stale state

Without hardware evidence, entries stay `"verified": false` and the UI marks them "recognised, not yet verified on hardware".

## Protocol work and licences

- Learn protocols from **your own captures** (Android HCI snoop log + Wireshark, Windows Bluetooth ETW traces).
- Projects such as Gadgetbridge (AGPL), GalaxyBudsClient (GPL) and OpenMouse (AGPL) may be read for **behaviour and ideas only**. Don't copy their code into Samaat.
- Never bypass encryption or authentication, and never redistribute vendor firmware.
- Use "works with <model>" wording, with no vendor logos.

## Code style

- Match the surrounding code. Warnings are errors.
- New capabilities start as a feature interface in `Samaat.Core`, then the simulator, then the UI.
- Every write returns a `CommandResult`, and the UI must handle refused and timed-out commands.
- Never hide an unsupported feature: show it with a reason.
- UI values come from the design system: colours from `Samaat.UI/Styles/Tokens.axaml`, radii, type sizes, hit targets and shadows from `Samaat.UI/Styles/Metrics.axaml`, and repeated looks from a style class. `DesignSystemTests` fails the build on a raw hex colour, font size, shadow or tokenised radius in `.axaml`.

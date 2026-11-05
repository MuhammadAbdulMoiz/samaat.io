# Plan: move earbuds between this computer and a phone (and the Android app)

Branch `claude/phone-handoff` (from `master` at 43de354).

**User request (2026-11-04):** "What if buds don't support multi channel and switch, we want it to be a general switch: we send a request to the phone to ask for connection, disconnect it from the PC and then mobile connects with it. If yes then go ahead, plan and develop the whole mobile app project."

## The idea in one paragraph

Samaat runs on both devices. The two copies are paired once and talk over the local network. To move the earbuds, the computer asks the phone whether it can take them, disconnects them here (the existing `IPcLinkFeature`), and asks the phone to connect them. If the phone doesn't confirm in time, the computer connects them back, so a failed move never leaves you with no audio. The earbuds need nothing special: it only uses each OS's own connect and disconnect, so it works for single-connection earbuds with no vendor protocol.

## Flows

The **computer coordinates every move**, whichever side the button was pressed on. A button on the phone sends `StartHandoff` to the computer, which runs the same steps and streams progress back.

### To the phone
1. **Check** (`Prepare`, 8 s): the phone answers Ready, or Refused with a reason (Bluetooth off, earbuds not paired with this phone, permission missing). Nothing has changed yet.
2. **Disconnect here** (`IPcLinkFeature.DisconnectAsync`, 20 s). On failure: stop, nothing changed.
3. **Connect there** (`ConnectAudio`, 60 s, long because the phone may need one tap from you). The phone confirms only once Android reports the A2DP or headset profile as connected.
4. **Roll back** when 3 fails: connect here again. The result says whether audio came back.

### To this computer
1. **Check** (`Prepare`).
2. **Release there** (`ReleaseAudio`, 60 s). On failure: stop, nothing changed.
3. **Connect here** (`IPcLinkFeature.ConnectAsync`, 20 s).
4. **Roll back** when 3 fails: ask the phone to connect them again.

If the earbuds are connected to neither side, step 2 is skipped.

Outcomes: `Moved`, `NotStarted` (refused before anything changed), `RolledBack` (failed, audio is back where it was), `Stranded` (failed and the rollback failed too: the message says which device to use).

## Android limits (honest)

- Android doesn't give ordinary apps a public "connect this headset" call. `BluetoothA2dp.connect`/`BluetoothHeadset.connect` are hidden (system API from Android 13). The phone tries, in order:
  1. The hidden `connect` through reflection. Works on older Android and some phones; refused elsewhere.
  2. **Ask you, once:** a notification (or, with the app open, a dialog) that opens Android's own output switcher (`MediaRouter2.showSystemOutputSwitcher()` on Android 14+) or Bluetooth settings. You tap the earbuds.
  Either way it waits for the real profile connection before confirming.
- Disconnecting from the phone has the same limit: reflection `disconnect`, then the same prompt.
- Each attempt is recorded and shown, so we learn which phones need the tap.
- iOS can't connect or disconnect Bluetooth audio from an app at all, so no iOS app is planned.

## Pairing the two apps (no account, no server)

Like Bluetooth's numeric comparison:
1. On the computer: Settings → Phone → **Pair a phone**. The computer starts listening and announces itself on the local network for 2 minutes.
2. On the phone: **Pair with a computer** lists computers that are announcing. Tap yours.
3. Both screens show the **same 6-digit code**. Tap **Pair** on both.

Protocol (`Samaat.Handoff/Pairing`):
- Phone → `PairStart { phoneId, phoneName, commitment = SHA-256(pubP ‖ nonceP) }`
- Computer → `PairKey { pcId, pcName, pubC, nonceC }`
- Phone → `PairReveal { pubP, nonceP }`; computer checks the commitment.
- Both: `shared = ECDH-P256`, `secret = HKDF-SHA256(shared, salt = nonceP ‖ nonceC, "samaat-pair-v1")`, `code = HKDF(…, "samaat-sas-v1") mod 10^6`.
- Each side's **Pair** sends an encrypted `PairConfirm`. Both sides store `{peerId, name, secret}` only when both confirmed.

The commitment stops a man in the middle from grinding keys to make both codes match; an active attacker's chance is one in a million per attempt, and the computer only accepts pairing while the window is open.

## Session security

Each TCP connection (phone is the client):
- `ClientHello { phoneId, nonceC }` → `ServerHello { pcId, nonceS }` (plain).
- `keys = HKDF-SHA256(secret, salt = nonceC ‖ nonceS, "samaat-session-v1")` → one AES-256-GCM key per direction.
- Every frame after that: 4-byte length, AES-GCM with a per-direction counter nonce. The first encrypted frame each way is `Hello`; a wrong secret fails decryption and the connection closes.
- Fresh nonces per session, so old frames can't be replayed.
- Secrets: the computer keeps them in `%APPDATA%/Samaat/phones.json`, encrypted with Windows DPAPI; the phone in the app's private storage.

## Finding each other

- The computer broadcasts a UDP beacon every 3 s on port 47820 while it has a paired phone or pairing is open: `{ v, id, name, port, pairing }`. No secrets.
- TCP server on 47821 (falls back to any free port; the beacon says which).
- The phone listens for its computer's id (with a Wi-Fi multicast lock on Android) and also retries the last address it used.
- Windows asks once to allow Samaat through the firewall.

## Messages (JSON, source-generated serializer)

`Hello`, `Ping`, `DeviceList { devices: [{ address, name, connected, canConnect, canDisconnect }] }` (both directions, on change), `StartHandoff { address, direction }`, `Prepare`, `ConnectAudio`, `ReleaseAudio`, `HandoffProgress { address, stage, text }`, `Reply { replyTo, status, message }`. Earbuds are matched across devices by Bluetooth address.

## Projects

| Project | TFM | What |
|---|---|---|
| `Samaat.Handoff` | net10.0 | Messages, framing, pairing, secure session, `PhoneLinkHost` (computer), `PhoneLinkClient` (phone), `HandoffCoordinator`, `PhoneAgent`, UDP beacon |
| `Samaat.Mobile` | net10.0 | Avalonia phone UI (devices, device page with battery / noise control / EQ, move card, pairing). Runs headless in tests |
| `Samaat.Platform.Android` | net10.0-android | Bonded devices as `IEarbudDevice`, audio connect/disconnect strategies, RFCOMM and GATT transports (so the Jieli and Fast Pair drivers work on the phone), multicast lock |
| `Samaat.Android` | net10.0-android | App head: activity, foreground service (keeps the computer link alive), manifest and permissions |

**Moved (2026-11-05):** the phone projects (`Samaat.Mobile`, `Samaat.Mobile.Preview`, `Samaat.Platform.Android`, `Samaat.Android`, `Samaat.Mobile.Tests`) now live in **[samaat-mobile](https://github.com/MuhammadAbdulMoiz/samaat-mobile)**, which builds this repo's shared projects from a submodule. Originally: the Android projects were not in `Samaat.slnx` (they need the Android workload). `Samaat.Mobile.slnx` builds them, and a separate CI job builds the APK on Ubuntu.

## Desktop UI

- **Settings → Phone:** paired phones (online / offline), **Pair a phone**, the code prompt (Pair / Cancel), **Forget**.
- **Links page → Your phone:** per online phone, whether it knows these earbuds and whether they're connected to it, **Move to {phone}** / **Bring back here**, live progress and the result.

## Tests

- Handoff: framing, AES-GCM session (tamper, wrong key, counters), pairing (codes match, commitment mismatch, decline), coordinator with fakes for every step and rollback path (FakeTimeProvider for timeouts), and loopback-TCP end to end (pair → connect → move both ways → rollback).
- Desktop: phone settings and Links card view models, screenshots.
- Mobile: view models plus headless screenshots of each page.
- Android: compile in CI only. Hardware check below.

## Hardware check (not doable in CI)

With the Quantum Zbuds (single connection) and an Android phone on the same Wi-Fi:
1. Pair; codes match on both screens.
2. Buds on the PC → **Move to phone** on the PC: PC disconnects, phone connects (or asks for one tap), audio plays on the phone.
3. **Bring back here** on the PC and **Move to computer** on the phone.
4. Phone Bluetooth off → move refused before the PC disconnects.
5. Ignore the phone's prompt → after 60 s the PC reconnects.
Record which connect strategy each phone used (Android version, maker).

## Done when

Build with 0 warnings, all tests green, the APK builds in CI, and the user runs the hardware check.

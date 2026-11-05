<h1 align="center">Samaat</h1>

<p align="center">
  <b>One app for Bluetooth earbuds from any brand.</b><br />
  Battery for each bud, noise control, EQ, connections and shared audio, on Windows, plus an Android app to move your earbuds between phone and computer.
</p>

<p align="center">
  <a href="docs/STATUS.md">Status</a> &nbsp;·&nbsp;
  <a href="#run-it">Run it</a> &nbsp;·&nbsp;
  <a href="#supported-earbuds">Supported earbuds</a> &nbsp;·&nbsp;
  <a href="docs/ARCHITECTURE.md">Architecture</a> &nbsp;·&nbsp;
  <a href="CONTRIBUTING.md">Contributing</a>
</p>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/buds-pro.png" />
  <source media="(prefers-color-scheme: light)" srcset="docs/images/buds-pro-light.png" />
  <img src="docs/images/buds-pro.png" width="100%" alt="Samaat showing a pair of earbuds with separate left, right and case battery levels" />
</picture>

Every earbud brand ships its own app, and most only run on a phone. Samaat works out which earbuds are connected, what they can actually do, and shows only the controls that work. Anything it can't do is greyed out with the reason, never just missing.

## What it does

<table>
  <tr>
    <td width="50%" valign="top">
      <b>Battery for each piece</b><br />
      <sub>Left bud, right bud and case, read from the earbuds' own protocol. When a model only reports one number, you get the Windows level, labelled as such.</sub>
    </td>
    <td width="50%" valign="top">
      <b>Noise control and EQ</b><br />
      <sub>Noise cancelling, transparency and the earbuds' EQ presets, on models whose protocol Samaat speaks. Every change is confirmed by reading it back.</sub>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <b>Connect and disconnect</b><br />
      <sub>Connect or disconnect your earbuds from the app or the tray, the same way Windows Settings does.</sub>
    </td>
    <td width="50%" valign="top">
      <b>Share audio and Switch here</b><br />
      <sub>Play what Windows is playing on up to three more outputs at once, each with its own delay, or move all audio to another output in one click.</sub>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <b>Move to your phone and back</b><br />
      <sub>With Samaat on your Android phone, move the earbuds between the phone and the computer from either one, even earbuds that connect to one device at a time. If the other side can't connect them, they go back where they were.</sub>
    </td>
    <td width="50%" valign="top">
      <b>Paired by a code</b><br />
      <sub>Pair the phone and the computer once by comparing a 6-digit code. They talk over your Wi-Fi with an encrypted link: no account, no server.</sub>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <b>Works with unknown earbuds</b><br />
      <sub>Earbuds are matched by the protocol they speak (Google Fast Pair, Jieli RCSP), not by name. Unknown models get a neutral look you can swap for any known model's photo.</sub>
    </td>
    <td width="50%" valign="top">
      <b>Stays out of the way</b><br />
      <sub>Tray icon with battery levels, low-battery notifications, start with Windows, dark and light themes, and reduced motion.</sub>
    </td>
  </tr>
</table>

<p>
  <img src="docs/images/buds-pro-sound-share.png" width="49%" alt="Share audio card: one source, a sharing row with its delay slider, and idle rows with Switch here" />
  <img src="docs/images/buds-pro-connections.png" width="49%" alt="Links page: connect or disconnect this computer and see the earbuds' other devices" />
</p>

## Supported earbuds

| Earbuds | How Samaat talks to them | What works |
|---|---|---|
| **Audionic ParadoX-E** | Jieli RCSP over BLE | Battery per bud, noise control, EQ presets |
| **realme Buds T200 Lite** | Google Fast Pair message stream | Battery per bud (case when it reports one), firmware version |
| **Any Fast Pair earbuds** | Google Fast Pair message stream | Battery, when the earbuds report it |
| **Any Jieli earbuds** | Jieli RCSP over BLE | What the earbuds list when asked |
| **Zero Lifestyle Quantum Zbuds** | Windows only (no vendor protocol found) | Overall battery, connect, share audio |
| **Microsoft Modern Wireless Headset** | Windows only | Overall battery, connect, share audio |
| **Anything else paired** | Windows | Overall battery, connect, share audio |

Each model's evidence lives in [`docs/hardware-tests`](docs/hardware-tests) and its protocol notes in [`docs/protocols`](docs/protocols). Adding a model is a JSON file in [`devices/`](devices/README.md).

## Run it

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/Samaat.App -f net10.0-windows10.0.19041.0
```

That's the Windows app with real Bluetooth. The Android app builds with the Android workload ([how](docs/modules/phone-handoff.md#building-the-android-app)); CI uploads an APK. Two more ways to run the desktop app:

```bash
dotnet run --project src/Samaat.App -f net10.0                          # simulated earbuds, any OS
dotnet run --project src/Samaat.Cli -f net10.0-windows10.0.19041.0      # print what Samaat detects
```

Build and test everything:

```bash
dotnet build Samaat.slnx
dotnet test Samaat.slnx
```

The tests also render every page as dark and light screenshots ([`docs/images`](docs/images)).

## How it's built

```
src/
  Samaat.Core/                 capability model, battery, noise control, EQ, audio sharing, drivers
  Samaat.Transport/            GATT and RFCOMM abstractions
  Samaat.Protocols.Standard/   BLE Battery Service
  Samaat.Protocols.Jieli/      Jieli RCSP driver
  Samaat.Protocols.FastPair/   Google Fast Pair message stream driver
  Samaat.Platform.Windows/     WinRT Bluetooth, WASAPI audio sharing, connect and disconnect
  Samaat.Handoff/              phone link: pairing, encrypted sessions, moving earbuds with rollback
  Samaat.Simulator/            simulated earbuds for UI work and tests
  Samaat.UI/                   shared design system: tokens, icons, fonts, battery ring
  Samaat.App/                  Avalonia desktop app
  Samaat.Mobile/               Avalonia phone UI
  Samaat.Platform.Android/     Android Bluetooth audio, devices, RFCOMM and GATT
  Samaat.Android/              Android app
  Samaat.Cli/                  console tool for checking real hardware
tests/                         xUnit, including headless UI screenshots (desktop and phone)
```

Every feature is described as *what the earbuds support × what Samaat supports × whether it's available right now*, so every disabled control can say why. More in [ARCHITECTURE.md](docs/ARCHITECTURE.md), the [design notes](docs/design/ui-audit.md), and the module pages: [battery](docs/modules/battery.md), [noise control](docs/modules/noise-control.md), [connections](docs/modules/connections.md), [audio sharing](docs/modules/audio-share.md), [phone handoff](docs/modules/phone-handoff.md).

## Contributing

Bug reports with the Diagnostics tab's **Copy report** are the most useful thing you can send. To add earbuds, read [CONTRIBUTING.md](CONTRIBUTING.md): a model is only marked verified with hardware evidence.

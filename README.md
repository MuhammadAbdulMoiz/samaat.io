# Samaat

One app to monitor and control Bluetooth earbuds from any brand. Samaat identifies the connected device, works out what it can do, and shows only the controls that actually work. Everything else is greyed out with the reason.

![Samaat dashboard](docs/images/buds-pro.png)

- **Status, remaining work and how to start the app: [docs/STATUS.md](docs/STATUS.md)**
- Plan: [docs/PLAN.md](docs/PLAN.md) · Architecture: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) · [Contributing](CONTRIBUTING.md) · [Device catalog](devices/README.md)
- UI audit and design direction: [docs/design/ui-audit.md](docs/design/ui-audit.md)
- Modules: [battery](docs/modules/battery.md) · [noise control](docs/modules/noise-control.md) · [connections](docs/modules/connections.md)

## Status

| Module | State |
|---|---|
| Capability model, device registry, command layer | ✅ |
| 1. Battery monitoring | ✅ simulator, BLE Battery Service, Windows-reported level (Windows not yet run on hardware) |
| 2. Noise control (ANC / transparency / adaptive, levels, extras) | ✅ simulator; real devices need a vendor adapter |
| 3. Multipoint and connected devices | ✅ simulator; real devices need a vendor adapter |
| Driver registry, device catalog, RFCOMM transport, Info/diagnostics page | ✅ (RFCOMM unverified on hardware) |
| First vendor adapter | waiting on the choice of target earbuds |

## Build

Requires the .NET 10 SDK.

```bash
dotnet build Samaat.slnx
dotnet test Samaat.slnx

dotnet run --project src/Samaat.App -f net10.0                          # simulator, any OS
dotnet run --project src/Samaat.App -f net10.0-windows10.0.19041.0      # real Bluetooth, Windows
dotnet run --project src/Samaat.Cli -f net10.0 -- --simulate --watch    # CLI
```

The Windows-specific projects also *build* on Linux and macOS (`EnableWindowsTargeting`), but only run on Windows.

## Layout

```
src/
  Samaat.Core/                 capability model, battery model, low-battery alerts, device registry
  Samaat.Transport/            GATT abstractions (RFCOMM to come with the first vendor adapter)
  Samaat.Protocols.Standard/   BLE Battery Service
  Samaat.Platform.Windows/     WinRT device discovery, OS battery, GATT client
  Samaat.Simulator/            fake devices for UI work and tests
  Samaat.App/                  Avalonia desktop app
  Samaat.Cli/                  console tool for checking real hardware
tests/                         xUnit; App tests render headless screenshots
```

# Module 3: Multipoint and connected devices

Status: **implemented (v0.2)**. Tested end to end with the simulator. Real devices show only "this computer" until a vendor adapter exposes multipoint.

![Connections page](../images/buds-pro-connections.png)

## What it does

- Lists every host the earbuds know: **playing audio, connected on standby, not connected, out of range**. The active host is listed first and "This computer" is marked.
- **Switch audio here / Connect**: makes a host the audio source, connecting it first if needed. When the connection limit is reached, the standby host is dropped (never this computer, if there's a choice).
- **Disconnect** another host. Disconnecting *this* computer is refused with guidance to use system Bluetooth settings, because Samaat would lose its control channel.
- A **multipoint switch**. Turning it off drops standby hosts.
- The same confirm-or-roll-back behaviour as noise control.
- When the device has no multipoint support in Samaat, the page shows only this computer's link and says why other devices can't be seen.
- The limits from PLAN.md §8 are always stated on the page.

## Code

| Piece | Where |
|---|---|
| Hosts, options, state, `IMultipointFeature`, capabilities | `src/Samaat.Core/Connections/Multipoint.cs` |
| `MultipointRules`: reference model of switching, disconnecting and toggling | same file |
| Simulated device | `src/Samaat.Simulator/SimulatedMultipoint.cs` |
| View models | `src/Samaat.App/ViewModels/ConnectionsViewModel.cs` |
| Page | `src/Samaat.App/Views/ConnectionsPage.axaml` |

## Tests

`MultipointTests.cs` in Core.Tests (rules, capabilities, simulator) and `ConnectionsViewModelTests.cs` in App.Tests.

## Not yet

- Any real device (needs a vendor adapter).
- Host-side connect and disconnect for *this* computer. WinRT has no public API to start or drop a classic audio connection, so this stays with system settings for now.
- A companion app on other devices (a "later" idea in the plan, not a promise).

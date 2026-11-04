# Device catalog

One JSON file per model: `devices/<vendor>/<model>.json`. The app and the CLI load every file at startup, except files starting with `_`. See [`_example.json`](_example.json) for the schema.

The catalog decides how a paired device is presented:

| Situation | Result |
|---|---|
| No entry matches | **Generic**: standard Bluetooth and OS information only |
| An entry matches, but its `driver` isn't implemented | **Recognised**: listed features show as "supported by your device, not yet by Samaat" |
| An entry matches and the driver exists | **Full**: the driver provides the features |

Rules:
- Only add **observed** values (device name, SDP service UUIDs from the Info page's "Copy report"). Never guess.
- Set `verified: true` only with hardware evidence, as described in [CONTRIBUTING.md](../CONTRIBUTING.md).

## Matching

Every field given in `match` must agree. The highest score wins: vendor + product ID 100, an advertised service (`serviceUuid`) 50, vendor ID alone 30, name 10, summed.

## Generic entries (`generic/`)

`generic/fast-pair.json` (any device advertising the Fast Pair Message Stream `df21fe2c-…`) and `generic/jieli.json` (any device with Jieli's vendor ID `0x05D6`) give unknown earbuds a driver by protocol. They carry `"generic": true`, which means:

- **Any matching model entry wins**, whatever its score. A model keeps its own name, photos and profile.
- **A model with no driver of its own still gets a matching protocol driver.** If its `driver` is `none` or not registered, the best-scoring generic entry whose driver is registered supplies the driver; the model entry keeps the identity. No match stays "Recognised".
- `DeviceProfileDb` **skips them on purpose** (no error): they describe a protocol, not a model, so they have no display name, artwork or notes to show. Loading them as profiles could also tie a name-only model on specificity and take its place.
- The driver decides what is real. The Jieli driver gives nothing without the BLE `AE00` service, and offers noise control or EQ only when the buds list them when read back. The Fast Pair driver gives nothing when the battery is unknown. Either way the device falls back to the Windows battery.

A device with both the Jieli vendor ID and the Fast Pair stream gets the Fast Pair driver: an advertised service outranks a chip vendor's ID.

Models: `audionic/paradox-e.json`, `realme/buds-t200-lite.json`, `zero-lifestyle/quantum-zbuds.json`, `microsoft/modern-wireless-headset.json`.

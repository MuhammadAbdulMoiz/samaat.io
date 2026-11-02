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

The catalog is empty for now: entries come with the first hardware reports.

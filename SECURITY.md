# Security

## Reporting a problem

Please report security problems privately, not in a public issue: open a [private security advisory](https://github.com/MuhammadAbdulMoiz/samaat.io/security/advisories/new) on GitHub. Say which version you ran, what you did and what happened. If the Diagnostics tab's **Copy report** is relevant, check it for device names and Bluetooth addresses before you paste it.

Fixes go to the latest release only.

## What Samaat exposes

- **Bluetooth:** Samaat talks only to devices already paired with Windows. Bytes from a device are treated as untrusted and are length-checked before they are parsed.
- **Local network:** nothing, until you pair a phone. While pairing is open the computer broadcasts its name on the local network; once a phone is paired it listens on one TCP port. A device that isn't paired can only reach the pairing handshake, which is limited in size, time and number of attempts. Everything after pairing is encrypted (AES-256-GCM) with a key only the two devices hold. Details: [docs/modules/phone-handoff.md](docs/modules/phone-handoff.md).
- **On disk:** settings, a small log and the paired-phone keys, in `%APPDATA%\Samaat`. The keys are encrypted with Windows DPAPI for your user account.
- **Internet:** Samaat makes no internet connections. See [PRIVACY.md](PRIVACY.md).

Known gaps are listed in [docs/STATUS.md](docs/STATUS.md#known-limitations-today).

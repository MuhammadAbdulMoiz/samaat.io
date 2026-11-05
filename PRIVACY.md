# Samaat privacy policy

_Last updated: 5 November 2026_

Samaat is a Windows app that shows battery, noise control and other settings for your wireless earbuds and headphones.

## What Samaat collects

Nothing. Samaat has no accounts, ads, analytics or telemetry, and it does not send any data over the internet. Phone handoff, described below, only talks to your own phone on your local network.

## What stays on your computer

- **Bluetooth**: Samaat talks to your earbuds over Bluetooth to read their battery and change their settings. That information is only shown in the app.
- **Settings**: your preferences (theme, notifications, start with Windows and so on) are saved in a file on your computer and never leave it.
- **Diagnostics log**: Samaat writes errors it handles (for example a device that didn't answer) to a small log file next to your settings, capped at about half a megabyte. It may contain device names and Bluetooth addresses. It never leaves your computer unless you choose Diagnostics → Copy report and paste the report somewhere yourself.

## Local network (phone handoff)

Samaat can move your earbuds between this computer and your Android phone. This uses your local network (your Wi-Fi), never the internet, and only once you pair a phone:

- **While you're pairing a phone**, the computer announces itself on the local network every few seconds (a UDP broadcast with its name, Samaat's protocol version and a port number), so the phone can find it. Other devices on the same network can see that announcement. It stops when pairing ends.
- **Once a phone is paired**, the computer waits for that phone to connect on one TCP port. Everything sent between them (the earbuds each one can see, and move requests) is encrypted with a key only the two of them share.
- The pairing keys stay on the two devices.

If you never pair a phone, Samaat sends nothing on your network. In the Microsoft Store version, phone handoff is not available yet.

Uninstalling Samaat removes the app and its saved settings.

## Children

Samaat collects no personal information from anyone, including children.

## Changes

If this policy changes, the new version will be published at this address with a new date.

## Contact

Questions: open an issue at https://github.com/MuhammadAbdulMoiz/samaat.io/issues

# Try Samaat on your laptop and phone

Branch: `claude/phone-handoff` until it's merged.

## 1. Get the code and the SDK

- Install the [.NET 10 SDK](https://dotnet.microsoft.com/download) and [Git](https://git-scm.com/).
- Then:

```bash
git clone https://github.com/MuhammadAbdulMoiz/samaat.io.git
cd samaat.io
git checkout claude/phone-handoff
```

## 2. The desktop app (Windows laptop)

```bash
# Your real paired earbuds
dotnet run --project src/Samaat.App -f net10.0-windows10.0.19041.0

# Real earbuds plus three simulated ones (best for the phone preview below)
dotnet run --project src/Samaat.App -f net10.0-windows10.0.19041.0 -- --simulate
```

On a Mac or Linux laptop, `dotnet run --project src/Samaat.App -f net10.0` runs it with simulated earbuds only.

## 3. The phone app on your laptop (no phone needed)

`Samaat.Mobile.Preview` opens the phone app in a phone-sized window. It has the simulator's earbuds (the same ones as the desktop's `--simulate`) and uses the real network link, so it pairs with the desktop app like a phone would.

1. Start the desktop app with `--simulate` (above).
2. In a second terminal: `dotnet run --project src/Samaat.Mobile.Preview`
3. On the desktop: **Settings → Phone → Pair a phone**. When Windows asks, **allow Samaat on private networks**.
4. In the phone window: **Computer** tab → **Pair** next to your computer. Check that the codes match, then tap **Pair** in both windows.
5. Open **Simulated Buds Pro** in either app and press **Move to …** / **Bring … here**. Watch them disconnect on one side and connect on the other.

To start over, **Forget** the phone in the desktop's Settings, and **Forget this computer** on the phone. The preview keeps its pairing in `%LOCALAPPDATA%\Samaat\phone-preview`.

## 4. An Android phone

You need the APK. Either:

- **From CI:** open a pull request for this branch (or merge it). The `android` job builds the APK; download `samaat-android-apk` from the run's **Artifacts**. After merging, **Actions → CI → Run workflow** builds it again whenever you like.
- **Build it yourself:** install the Android workload, and let it fetch the Android SDK:

  ```bash
  dotnet workload install android
  dotnet build src/Samaat.Android -t:InstallAndroidDependencies -p:AndroidSdkDirectory="%LOCALAPPDATA%\Android\Sdk" -p:AcceptAndroidSDKLicenses=True
  dotnet publish src/Samaat.Android -c Release -p:AndroidSdkDirectory="%LOCALAPPDATA%\Android\Sdk" -o artifacts/apk
  ```

Install it, either way:

- **Without a cable:** copy `io.samaat.app-Signed.apk` to the phone, open it, and allow "Install unknown apps" for your file manager when asked.
- **With a cable:** turn on **Developer options → USB debugging**, plug the phone in, then run `adb install artifacts/apk/io.samaat.app-Signed.apk`. `dotnet build src/Samaat.Android -t:Run` also builds, installs and starts it.

Then:

1. Pair your earbuds with the phone in Android's Bluetooth settings first (once).
2. Open Samaat on the phone and allow **Nearby devices** and **notifications**.
3. Put the phone on the **same Wi-Fi** as the laptop. Guest and office networks often block devices from seeing each other; a home network or the laptop's hotspot works.
4. Pair: desktop **Settings → Phone → Pair a phone**, then phone **Computer → Pair**, and compare the codes.
5. Move your real earbuds from either side. If Android shows the audio output picker or a notification, tap your earbuds: Android doesn't let apps connect headsets silently on most phones.

Please note what happened, using the checklist in [the plan](plans/2026-11-04-phone-handoff.md#hardware-check-not-doable-in-ci).

## 5. iPhone

There's no iPhone app, and there can't be one that does this. iOS doesn't let apps connect or disconnect Bluetooth headphones, and it doesn't let apps reach most earbuds' control channels. On an iPhone, use the phone preview on your laptop (step 3) to try the phone side.

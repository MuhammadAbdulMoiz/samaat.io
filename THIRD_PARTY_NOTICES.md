# Third-party notices

## JieLi RCSP authentication cipher

`src/Samaat.Protocols.Jieli/JlAuthCipher.cs` is a C# port of the JieLi (Jieli Technology) auth cipher, derived from these MIT-licensed projects:

- Neisvestney [buds-sniffer](https://github.com/Neisvestney/buds-sniffer) `JlAuthCipher.kt`
- ElectronicCats [jieli-ble-badge-research](https://github.com/ElectronicCats/jieli-ble-badge-research) `auth.py`
- hybridherbst [web-bluetooth-e87](https://github.com/hybridherbst/web-bluetooth-e87) `jl_auth_v3.py`

```
MIT License

Copyright (c) the authors of buds-sniffer, ElectronicCats/jieli-ble-badge-research and hybridherbst/web-bluetooth-e87

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Jieli RCSP noise control and EQ commands

The command and attribute layouts used by `src/Samaat.Protocols.Jieli` for noise control and EQ (GetSysInfo/SetSysInfo, the noise-mode and EQ attributes) were learned from Jieli Technology's official SDKs, [Android-JL_Bluetooth](https://github.com/Jieli-Tech/Android-JL_Bluetooth) and [iOS-JL_Bluetooth](https://github.com/Jieli-Tech/iOS-JL_Bluetooth), both licensed under the Apache License 2.0 (https://www.apache.org/licenses/LICENSE-2.0). No SDK code is included; Samaat's implementation is its own. Details: [docs/protocols/jieli-rcsp.md](docs/protocols/jieli-rcsp.md).

## NAudio

`src/Samaat.Platform.Windows/WasapiBackend.cs` uses [NAudio](https://github.com/naudio/NAudio) (`NAudio.Wasapi`, `NAudio.Core`) for WASAPI loopback capture and playback. NAudio is licensed under the MIT License, Copyright (c) Mark Heath & contributors.

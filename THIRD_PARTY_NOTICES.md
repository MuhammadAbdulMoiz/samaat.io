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

## Ported protocol code

Code ported from other open-source projects under the porting rule in [CONTRIBUTING.md](CONTRIBUTING.md#protocol-work-and-licences). Each ported file carries a provenance header naming its source; `ProvenanceTests` checks that every source in a header is listed here. Ported files are licensed GPL-3.0-or-later as part of Samaat; the original licence and copyright notice are kept below.

| Project | URL | Commit | Licence | Files |
|---|---|---|---|---|
| GalaxyBudsClient | https://github.com/timschneeb/GalaxyBudsClient | `754b6fdfb1c151b022cece775c8aaad4eb185afa` | GPL-3.0 (Copyright (c) 2019-2026 Tim Schneeberger and contributors) | `src/Samaat.Protocols.Samsung/*` (SPP framing, CRC, status parsing, model table); the test vectors in `tests/Samaat.Protocols.Tests/SamsungTests.cs` come from its `GalaxyBudsClient.Tests/TestData/ExtendedStatusUpdate` |
| OpenSCQ30 | https://github.com/Oppzippy/OpenSCQ30 | `e75a68b70de8eaf2cf0ca364e132085a097557c5` | GPL-3.0 (Copyright (c) 2023-2026 Kyle Scheuing and contributors) | `src/Samaat.Protocols.Soundcore/*` (frame and checksum, state parsing, model table, service selection); the test vectors in `tests/Samaat.Protocols.Tests/SoundcoreTests.cs` come from its `lib/src/devices/soundcore/a3028*` tests and `common/packet` tests |
| BudsLink | https://github.com/maniacx/BudsLink | `0eb4d07dcd7eafabf425d98c607e02fc9519c94b` | GPL-3.0 (BudsLink credits radiance-project/ear-web) | `src/Samaat.Protocols.Nothing/*` (frame, commands, ANC and EQ bytes, model table); the test vectors in `tests/Samaat.Protocols.Tests/NothingTests.cs` are Samaat's own captures |
| SonyHeadphonesClient | https://github.com/Plutoberth/SonyHeadphonesClient | `5620e8ed5deccb957338b54e371b215146080819` | MIT (Copyright (c) 2020 Nir Harel, Mor Gal, Sem Visscher, jimzrt, guilhermealbm, and other contributors) | `src/Samaat.Protocols.Sony/SonyFrame.cs` and `SonyNcAsmCommand.cs` (framing, escaping, checksum, the noise and ambient set command); the set-command vectors in `tests/Samaat.Protocols.Tests/SonyTests.cs` come from its serializer |

GalaxyBudsClient is licensed under the GNU General Public License v3 ("GPLv3", no "or later" stated). Its ported code is combined into Samaat under the same licence text as Samaat's own [LICENSE](LICENSE); while that code is in Samaat, the combined program is distributed under GPL-3.0.

OpenSCQ30 is licensed under the GNU General Public License v3 (GPL-3.0). Its ported code is combined into Samaat under the same licence text as Samaat's own [LICENSE](LICENSE); while that code is in Samaat, the combined program is distributed under GPL-3.0.

SonyHeadphonesClient is licensed under the MIT License. Its ported code is combined into Samaat, and the MIT licence text below travels with it.

### SonyHeadphonesClient licence (MIT)

```
MIT License

Copyright (c) 2020 Nir Harel, Mor Gal, Sem Visscher, jimzrt, guilhermealbm, and other contributors

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

The JieLi authentication cipher (above) was ported before this convention and is credited in its own section.

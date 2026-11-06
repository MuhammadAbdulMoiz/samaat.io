# Decisions settled on 2026-11-06

**Status:** accepted by the owner.

| Decision | Outcome |
|---|---|
| Licence | **GPL-3.0-or-later for both repos** (`samaat.io` and `samaat-mobile`). Already in place on desktop; see [the licence record](2026-11-05-licence-gpl.md). |
| Phone handoff in the Store build | Off until the phone app is on Google Play (1.0.0 and 1.1.0). Turns on in the release that adds the QR code. |
| Version | 1.0.0 is on the Microsoft Store; the next version is 1.1.0. |
| `.claude/` keep-list | Keep only what is used and needed: 10 skills, plugins `ecc`, `ponytail` and `superpowers`, and `rules/`. Everything else removed. See [.claude/README.md](../../.claude/README.md). |
| Battery label | The tile that shows the lowest bud's level is labelled **"Lowest bud"**, not "Overall" (it showed 1 % next to a bud at 100 %). Desktop and phone. The capability name "Overall battery" on the Info page is unchanged: it also describes headsets that report one combined level. |

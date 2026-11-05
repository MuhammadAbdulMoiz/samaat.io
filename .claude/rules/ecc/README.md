# ECC rules

Rule packs copied from [affaan-m/ECC](https://github.com/affaan-m/ECC) at commit `b38be6a` (ECC 2.2.3):
`common/` (language-agnostic) and `csharp/` (loaded for `*.cs` files).

The ECC plugin itself (agents, skills, commands, hooks) is enabled for everyone who opens this repo in
Claude Code through `.claude/settings.json` (`ecc@ecc`). Plugins can't ship rules, which is why these live here.

Where a rule conflicts with this repository's own conventions (`CONTRIBUTING.md`, `docs/STATUS.md`), the
repository wins. To update, copy the same folders from a newer ECC checkout.

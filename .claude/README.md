# `.claude/`: what's here and what it's for

Everything in this folder loads for **everyone** who opens the repository in Claude Code: the skills in `skills/`,
the plugins enabled in `settings.json`, and the rules in `rules/`.

**Decision (owner, 2026-11-06):** keep only what Samaat uses and needs. The repository audit (2026-11-05, issue 8)
had found 50 skills and 7 plugins, most unrelated to a C#/.NET 10 Avalonia app. The rest were removed.

Anything personal belongs in your own user folder (`~/.claude/skills/`, `~/.claude/settings.json`), where it loads for
you in every project but not for anyone else.

## Skills (`skills/`, 10)

| Skill | Use |
|---|---|
| `animate` | Designing motion; used for the app's transitions. Written for the web, but the decisions carry over to Avalonia |
| `apple-design` | Apple-style motion, materials and typography; taste, not code |
| `break-ui` | Worst-case data stress tests; used in PR #4 (`SimulatorScenario.WorstCase`) |
| `dimensional-drawing` | Used for `docs/design/drawings/` in PR #4 |
| `emil-design-eng` | UI polish principles behind break-ui and review-animations |
| `find-animation-opportunities` | Read-only motion audit of a UI |
| `improve-animations` | Motion audit and plan for a whole codebase |
| `remove-animations` | Removing motion that doesn't earn its place |
| `review-animations` | Used in PR #4; type `/review-animations` (`disable-model-invocation`) |
| `unslop` | Used in PR #2 |

## Plugins (`settings.json`, 3)

| Plugin | Marketplace | Use |
|---|---|---|
| `ecc@ecc` | `affaan-m/ECC` | Agents, skills, commands and hooks; its rules are vendored in `rules/ecc/` ([README](rules/ecc/README.md)) |
| `ponytail@ponytail` | `DietrichGebert/ponytail` | Smallest-change coding mode |
| `superpowers@claude-plugins-official` | Anthropic's official directory | Planning, TDD and debugging workflow skills |

`ecc` and `ponytail` come from third-party repositories (`extraKnownMarketplaces`): anyone who trusts the repo gets
them offered, and they run with Claude Code's permissions. `ponytail` has not been audited.

## Rules (`rules/ecc/`)

ECC's `common/` and `csharp/` rule packs, copied at a fixed commit. Where a rule conflicts with `CONTRIBUTING.md`, the
repository wins.

## Removed on 2026-11-06

- **Skills:** all 28 `lark-*` skills and `meegle` (Feishu/Lark), `ian-xiaohei-illustrations`, `animate-expo`,
  `ask-sonner`, `mobile-native`, `pick-ui-library`, `write-swift`, `wrap-up`, `prototype`, `animation-vocabulary`,
  `animation-performance`, `dimensional-drawing-motion`.
- **Plugins:** `mattpocock-skills`, `andrej-karpathy-skills`, `watch`, `savvy-progress` and their marketplaces.

Removing files doesn't shrink git history (about 64 MB; the old PNGs stay in past commits). Only a history rewrite
would, which isn't recommended.

# `.claude/`: what's here and what it's for

Everything in this folder loads for **everyone** who opens the repository in Claude Code: the skills in `skills/`,
the plugins enabled in `settings.json`, and the rules in `rules/`. The repository audit (2026-11-05, issue 8) found
that much of it has nothing to do with Samaat. This page lists it all so the owner can decide what stays. **Nothing
has been removed**: skills and plugins are personal choices.

Anything personal can move to your own user folder, where it still loads for you in every project but not for anyone
else: skills to `~/.claude/skills/`, plugins and marketplaces to `~/.claude/settings.json`.

## Skills (`skills/`, 50, about 17 MB)

Samaat is a C#/.NET 10 Avalonia desktop app for Windows (plus a shared phone layer). "Relevant" means a skill applies to
work in this repository; "not relevant" means it targets a different stack or product; "unsure" means it might help but
hasn't been used here.

| Skill | Relevant here? | Why |
|---|---|---|
| `animate` | Relevant | Designing motion; used for the app's transitions. Written for the web, but the decisions carry over to Avalonia |
| `animation-vocabulary` | Unsure | Naming a motion effect; harmless, rarely needed |
| `animation-performance` | Unsure | Web frame-budget advice (CSS, compositor); only partly applies to Avalonia |
| `animate-expo` | Not relevant | React Native and Expo; there is no Expo code (checked 2026-11-05) |
| `apple-design` | Unsure | Apple-style motion and materials for the web; useful as taste, not as code |
| `ask-sonner` | Not relevant | Sonner is a React toast library |
| `break-ui` | Relevant | Worst-case data stress tests; used in PR #4 (`SimulatorScenario.WorstCase`) |
| `dimensional-drawing` | Relevant | Used for `docs/design/drawings/` in PR #4 |
| `dimensional-drawing-motion` | Unsure | Animates a dimensional drawing; only if a motion version is wanted |
| `emil-design-eng` | Relevant | UI polish principles behind break-ui and review-animations |
| `find-animation-opportunities` | Relevant | Read-only motion audit of a UI |
| `improve-animations` | Relevant | Motion audit and plan for a whole codebase |
| `remove-animations` | Relevant | Removing motion that doesn't earn its place |
| `review-animations` | Relevant | Used in PR #4; the user has to type `/review-animations` (`disable-model-invocation`) |
| `unslop` | Relevant | Used in PR #2. About 1.5 MB, mostly reference material |
| `mobile-native` | Not relevant | Making a web app feel native on a phone; the phone app is Avalonia, not web |
| `pick-ui-library` | Not relevant | Curated frontend (web) libraries; user-typed only |
| `prototype` | Unsure | Several UI variants behind a picker; written for web UIs; user-typed only |
| `write-swift` | Not relevant | Swift; there is no iOS app |
| `ian-xiaohei-illustrations` | Not relevant | Hand-drawn article illustrations. The largest skill: 7.7 MB, almost all of it the 8 example PNGs (about 8 MB of PNGs in `.claude/` overall) |
| `wrap-up` | Not relevant | End-of-session notes to a personal memory vault: a personal workflow, not this project's; user-typed only |
| `meegle` | Not relevant | Feishu/Lark project management (Meego) |
| `lark-approval`, `lark-apps`, `lark-attendance`, `lark-base`, `lark-calendar`, `lark-contact`, `lark-doc`, `lark-drive`, `lark-event`, `lark-im`, `lark-mail`, `lark-markdown`, `lark-meeting`, `lark-minutes`, `lark-note`, `lark-okr`, `lark-openapi-explorer`, `lark-shared`, `lark-sheets`, `lark-skill-maker`, `lark-slides`, `lark-task`, `lark-vc`, `lark-vc-agent`, `lark-whiteboard`, `lark-wiki`, `lark-workflow-meeting-summary`, `lark-workflow-standup-report` (28) | Not relevant | Feishu/Lark workplace tools (chat, docs, calendar, approvals). Together with `meegle`, 29 skills. They add to every session's skill list without relating to Samaat |

Totals: 9 relevant, 5 unsure, 36 not relevant (29 of them Lark/Feishu).

## Plugins (`settings.json`, 7 enabled for everyone)

| Plugin | Marketplace (source) | Relevant here? | Notes |
|---|---|---|---|
| `ecc@ecc` | `affaan-m/ECC` (third party, GitHub) | Relevant | Agents, skills, commands and hooks; its rules are vendored in `rules/ecc/` (see [rules/ecc/README.md](rules/ecc/README.md)). Its GateGuard hook asks for facts before first edits. The one plugin installed in the cloud sessions so far |
| `superpowers@claude-plugins-official` | Anthropic's official plugin directory | Unsure | General development workflow skills (planning, TDD, debugging); overlaps with ECC |
| `mattpocock-skills@claude-plugins-official` | Anthropic's official plugin directory | Unsure | Skills from Matt Pocock, mostly TypeScript-oriented; Samaat has no TypeScript |
| `andrej-karpathy-skills@karpathy-skills` | `forrestchang/andrej-karpathy-skills` (third party, GitHub) | Unsure | Coding-behaviour guidelines; overlaps with ECC's rules |
| `watch@claude-video` | `bradautomates/claude-video` (third party, git) | Not relevant | Working with video; nothing in this repo needs it |
| `savvy-progress@claude-kit` | `JohnnyVizz/claude-kit` (third party, GitHub) | Unsure | Not installed or reviewed in any session so far |
| `ponytail@ponytail` | `DietrichGebert/ponytail` (third party, GitHub) | Unsure | Installing it was blocked by the cloud session's safety check ("code from external"), so it has never been installed or audited |

**Third-party plugins enabled for everyone:** `ecc`, `andrej-karpathy-skills`, `watch`, `savvy-progress` and
`ponytail` come from third-party repositories (`extraKnownMarketplaces`). Anyone who opens the repo and trusts it gets
them offered, so each is code from outside the project that runs with Claude Code's permissions. `superpowers` and
`mattpocock-skills` come through Anthropic's official directory.

## Rules (`rules/ecc/`)

ECC's `common/` and `csharp/` rule packs, copied at a fixed commit. Where a rule conflicts with `CONTRIBUTING.md`, the
repository wins. Keep them.

## Recommended keep-list (for the owner to decide)

Keep in the repository, because they apply to Samaat's code and design work and were used in its pull requests:

- **Skills:** `animate`, `break-ui`, `dimensional-drawing`, `emil-design-eng`, `find-animation-opportunities`,
  `improve-animations`, `remove-animations`, `review-animations`, `unslop`.
- **Plugins:** `ecc@ecc`.
- **Rules:** `rules/ecc/`.

Move to your user folder if you want them everywhere, otherwise remove from the repo: the 29 Lark/Feishu skills,
`ian-xiaohei-illustrations` (7.7 MB), `animate-expo`, `ask-sonner`, `mobile-native`, `pick-ui-library`, `write-swift`,
`wrap-up`, and the `watch` plugin.

Decide case by case: `animation-vocabulary`, `animation-performance`, `apple-design`, `dimensional-drawing-motion`,
`prototype`, and the `superpowers`, `mattpocock-skills`, `andrej-karpathy-skills`, `savvy-progress` and `ponytail`
plugins. `ponytail` in particular has never been reviewed.

Removing files from the repository doesn't shrink its history: the PNGs stay in past commits (the git history is about
64 MB). Only a history rewrite would remove them, which this page doesn't recommend.

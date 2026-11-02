---
name: wrap-up
description: "End-of-session wrap-up the user runs manually: save verified lessons from this session to the auto-memory vault, report stale claims in notes this session touched, refresh the Graphify scout graph, lint + log. Invoke only when the user types /wrap-up."
disable-model-invocation: true
argument-hint: "[--browse] [--no-graph]"
---

# Wrap-up

Run the steps in order. Never commit or push anything; the user commits the memory repo manually.
Arguments: `--browse` also rebuilds the Graphify Obsidian view; `--no-graph` skips step 3.

## 1. Save what this session learned

The memory directory is the auto-memory directory from the system prompt. Read its `CLAUDE.md` contract first.

- Go through this session's work and list candidates: verified fixes, root causes, decisions, constraints,
  user corrections/preferences, and facts about the user or team.
- Keep a candidate only if it is **verified** (file:line, commit, test output, or the user's own statement),
  **non-obvious** (not derivable from code/git/AGENTS.md), and **useful to a later session**. Drop routine
  edits, progress chatter, and anything inferred but unchecked.
- For each keeper: find the existing note that covers it (grep the vault) and update that; create a new note
  only when none fits. Follow the vault's frontmatter, `[[file-stem]]` links, and provenance rules. Mark
  replaced facts `Superseded` instead of deleting them.
- New note → add its one-line pointer to `MEMORY.md` (and to `index.md` for `project`/`reference` notes).
- If nothing qualifies, say so. Do not invent content to fill the step.

## 2. Check notes this session touched for stale claims (report only)

Scope: notes created/edited/read this session plus any the user named. Do not audit the whole vault.

- `git -C <repo> fetch --quiet` first (a stale remote-tracking ref gives wrong answers).
- For each commit SHA a note cites: does it still exist (`git cat-file -e <sha>`)? Is it in `origin/dev`
  (`git merge-base --is-ancestor <sha> origin/dev`)? Squash merges break ancestry: if not an ancestor, compare
  the code in dev before calling it unlanded.
- For each cited branch: does it still exist locally/remotely?
- For status words (`not pushed`, `unlanded`, `OPEN`, `UNFIXED`, `in review`): does current git contradict them?
- List findings as `note → claim → what git shows → suggested update`. Apply only the ones the user approves,
  except purely factual SHA-landed updates where the cited SHA is an ancestor of `origin/dev` and has not been
  reverted since. Anything inferred from a squash merge or code comparison goes on the approval list.

## 3. Refresh the Graphify scout graph

Only if `C:/zixel/graphify-vault/graphify-scout-refresh.sh` exists and `--no-graph` was not given:
`bash C:/zixel/graphify-vault/graphify-scout-refresh.sh` (add `--browse` if requested) and report its
`REBUILT <commit> dirty=<bool> | N nodes, M edges` line. If the script fails, report the failure, mark the
graph as **not refreshed**, and continue to step 4. If this session scouted with Graphify, make sure
`C:/zixel/graphify-vault/weldment-code/USAGE.md` has one line per scout (`date | question | helped? | missed`).

## 4. Lint and log

- `node lint_vault.js` in the memory directory; fix errors this session introduced, report the rest.
- If anything in memory changed, append one entry to `log.md`:
  `## [YYYY-MM-DD] update | <short title>` with 2-4 bullets (what, files, decisions). Never edit old entries.

## 5. Report

A short summary: notes updated/created (with paths), stale-claim findings awaiting approval, Graphify rebuild
line, lint result, and `git -C <memory dir> status --short` so the user sees what to commit.

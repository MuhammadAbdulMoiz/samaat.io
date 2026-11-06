# PR #3: branch model and CI on the integration branch

**Status:** open, into `feature/ui-redesign` · **Date:** 2026-11-05 · **Branch:** `claude/branching-workflow`

## Why this work happened

- **The ask:** "we gonna have master, dev and another branch; everything gets merged to dev and then release happens into master; for now we use feat redesign as dev, but plan this too"; clean up the other branches.
- **What prompted it:**
  - PR #2 was opened against `master` (GitHub's default branch). It showed 13 unrelated commits and took its title from an old Bose commit.
  - CI only ran on pushes to `master`.

## Decisions

| Decision | Why | Rejected alternative |
|---|---|---|
| `master` holds tagged releases only, changed through a release PR from dev | One place that is always shippable | Releasing from dev |
| `feature/ui-redesign` is dev for now; rename it to `dev` later through GitHub's rename | GitHub moves open PRs and the default-branch setting with a rename, so the rename can wait | Create a new `dev` now and migrate |
| Work branches (`feat/*`, `fix/*`, `docs/*`, `claude/*`) branch from dev and are deleted after merge | Short-lived branches keep conflicts small | Long-lived feature branches |
| `hotfix/*` branches from `master`, merges into `master`, then back into dev | Lets an urgent fix ship without unreleased work | Fixing on dev and waiting for a release |
| No `staging` branch | A release PR from dev into `master` already gives a review step (YAGNI) | A third long-lived branch |
| CI runs on pushes to `master`, `feature/ui-redesign` and `dev` | Dev merges get checked; `dev` is listed so the rename doesn't silently stop CI | Pushes to `master` only |

## Rules and procedures applied

- ECC `git-workflow` and `development-workflow` rules: conventional commits, PRs into the integration branch.
- Repository rule: run the checks before pushing.

## What couldn't be done from the session

- **Deleting the five merged branches failed.** The session's git proxy refuses to delete branches the session didn't create (HTTP 403). They were all verified fully merged into both `master` and `feature/ui-redesign`, so the owner can delete them safely: `chore/claude-plugins-skills`, `claude/dashboard-views-plan`, `claude/funny-cerf-0f3kyg`, `claude/phone-handoff`, `feature/remaining-modules`.
- **Repository settings for the owner to change:** the default branch to `feature/ui-redesign`, "Automatically delete head branches", and protection rules (PR plus green CI) on `master` and `feature/ui-redesign`.

## Merge guidance for the open PRs (tested with `git merge-tree`)

- **Order:** #3, then #2, then #4 all merge without conflicts, in any order.
- **Merge method:** use **"Create a merge commit"** for #2 and #4, because #4 contains #2's commit. Squashing or rebasing #2 makes #4 conflict.
- **Syncing `master` into dev already conflicts,** before any of these PRs:
  - `ConnectionsPage.axaml` and `SettingsPage.axaml`: `master`'s `e9917c7` shows phone handoff as "coming soon" while dev has the full feature. Whether the first release ships the phone feature is an open decision.
  - `AppOptions.cs`, added by #4: keep both sides.
  - Do the sync on its own branch, with its own PR.

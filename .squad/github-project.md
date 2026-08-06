# GitHub Project 2 — Coordination Dashboard

**Board:** `https://github.com/users/elbruno/projects/2/views/1`  
**Owner:** 🔄 **Ralph** (Work Monitor) — keeps the dashboard aligned with the real delivery flow. Escalates to 🏗️ **Mark** for field/view/workflow design changes.

## Position in the workflow

GitHub Project 2 is a **secondary dashboard**.

| Surface | What it owns |
|---------|---------------|
| GitHub Issues | Work intake, scope, backlog state |
| Pull Requests | Review state, merge state, shipped change history |
| `.squad/decisions.md` | Team rules and architecture/process decisions |
| GitHub Project 2 | Cross-cutting visibility across work, PRs, deploy sync, and manual validation |

**Rule:** never update the board as a substitute for updating the issue, PR, or decision log that actually owns the truth.

## What belongs on the board

Use Project 2 to give Bruno a single view of:

1. **Feature streams** — larger efforts that span multiple issues/PRs.
2. **Issue progress** — triaged, in progress, merged to `main`, waiting manual validation, done.
3. **PR/merge visibility** — whether code is still in branch/PR or already in `main`.
4. **Deploy/public-site sync work** — when plan repo, public repo, dashboard, or docs need coordinated publishing.
5. **Cross-repo coordination** — work that spans this repo and `elbruno/openclawnet`.

## Who handles it

- **Ralph** updates the board during normal backlog/merge monitoring.
- **Mark** decides whether the board structure itself needs changes.
- **No new dedicated agent is needed.** The existing team already maps cleanly to the workflow.

## When Ralph updates the board

Ralph should reflect the real state when any of these happen:

1. An issue is split/triaged into concrete work.
2. A branch/PR is opened for the work.
3. The PR is merged into `main`.
4. The work is waiting for Bruno's manual validation on `main`.
5. A related deploy/public-site sync/dashboard update is pending or complete.

## Minimal status model

Use the board to distinguish these states even if the exact field names differ:

| Intended meaning | Real source |
|------------------|-------------|
| Planned / Triaged | Issue state + labels |
| In progress | Branch/worktree/PR exists |
| In review | PR state |
| In main / waiting validation | Merged PR, issue intentionally left open |
| Done | Bruno closes the issue / related release or sync is complete |

## Anti-drift rules

1. If issue/PR state and Project 2 disagree, **fix the board** — not the source-of-truth surface.
2. Never close an issue just because the board says done.
3. Never mark a board item as shipped if the PR is not merged to `main`.
4. For issue splits like `#148` → `#177`–`#181`, track the child issues/items individually; the parent stays as the umbrella tracker.

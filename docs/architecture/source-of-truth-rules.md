# Source of Truth Rules

> **TL;DR:** Plan repo is canonical. Public repo is read-only mirror. Never write to public directly.

---

## The Rule

| Repository | Role | Write Access |
|------------|------|--------------|
| `elbruno/openclawnet-plan` (private) | **Canonical source of truth** | ✅ All work happens here |
| `elbruno/openclawnet` (public) | Read-only mirror | ❌ Sync workflow only |

---

## What This Means for You

1. **All code, tests, scripts, and docs** — Edit in plan repo
2. **All branches and PRs** — Create in plan repo
3. **All commits** — Land in plan repo first
4. **Public repo updates** — Handled automatically by the sync workflow

---

## Before You Start Working

Run this 2-line check to verify you're in the right place:

```powershell
# Check which repo you're in
git remote get-url origin
# Should show: https://github.com/elbruno/openclawnet-plan.git
#         or: git@github.com:elbruno/openclawnet-plan.git

# If you see "openclawnet" (without -plan), STOP and switch repos
```

---

## What Happens If I Commit to Public?

1. Your work will be **overwritten** on the next sync run
2. You'll need to recreate it in plan repo anyway
3. The sync workflow will flag the divergence

**Just don't do it.** Always work in plan repo.

---

## How Content Reaches Public

```
┌─────────────────────┐     sync workflow      ┌─────────────────────┐
│   Plan Repo         │ ─────────────────────► │   Public Repo       │
│   (your edits)      │    (automatic PR)      │   (read-only)       │
└─────────────────────┘                        └─────────────────────┘
        ▲
        │
   You work here
```

The sync workflow:
- Triggers on push to `main` in plan repo
- Creates a PR on public repo
- Requires human review before merge
- Preserves authorship via `Co-authored-by:` trailers

---

## Excluded From Sync

These paths stay private (never reach public):

- `.squad/` — Agent configs
- `.copilot/` — Copilot configs
- `skills/` — Skill samples
- `docs/analysis/` — Private analysis
- `docs/security/` — Security audits
- `squad-*.yml` workflows — Squad orchestration

See `.github/sync-config.yml` for the full exclusion list.

---

## Questions?

- **Architecture:** Mark (Lead Architect)
- **Process:** Bruno Capuano
- **Security:** Drummond

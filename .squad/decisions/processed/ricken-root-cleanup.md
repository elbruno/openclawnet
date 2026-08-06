# Decision: Repository Root Cleanup

**Date:** 2026-05-09  
**Agent:** Ricken (DevRel/Writer)  
**Requested by:** Mark (Lead Architect) on behalf of Bruno Capuano  
**Status:** Completed

## Context

The repository root contained various documentation artifacts, planning files, and media assets that accumulated during development. This violated the team's root cleanliness policy: root should contain only README, LICENSE, solution/build/config files, and minimal supporting files.

## Decision

Cleaned root by relocating all non-essential files while preserving git history:

### Files Moved (6 renames via `git mv`)

1. **Planning Documentation → `docs/planning/`**
   - `PHASE2_FEATURE1_DECOMPOSITION.md` → `docs/planning/PHASE2_FEATURE1_DECOMPOSITION.md`
   - `phase2b-plan-summary.txt` → `docs/planning/phase2b-plan-summary.txt`

2. **Archive Documentation → `docs/archive/`**
   - `pr-body.md` → `docs/archive/pr-body-reconciliation.md` (renamed for clarity)

3. **Media Assets → `video-production/`**
   - `slides-en-3.png` → `video-production/slides-en-3.png`
   - `slides-es-3.png` → `video-production/slides-es-3.png`

4. **Acknowledgments → `docs/`**
   - `ACKNOWLEDGMENTS.md` → `docs/ACKNOWLEDGMENTS.md`

### Files Removed (1 deletion)

- `gitleaks-s5.json` — disposable scan output (empty array)

### Links Updated

- `docs/manuals/20-tools.md`: Updated ACKNOWLEDGMENTS.md path (../../ → ../)
- `docs/demos/tools/02-github-issue-triage/README.md`: Updated ACKNOWLEDGMENTS.md path (../../../../ → ../../../)

## Final Root State

**Files remaining at root (12):**
- `.env` (untracked, secrets; left untouched)
- `.gitattributes`
- `.gitignore`
- `.gitleaks.toml`
- `.mcp.json`
- `appsettings.example.json`
- `LICENSE`
- `OpenClawNet.slnx`
- `package-lock.json`
- `package.json`
- `README.md`
- `squad.config.ts`

**Directories at root:** .copilot, .git, .github, .playwright-mcp, .squad, docs, node_modules, scripts, skills, src, TestResults, tests, video-production

## Verification

- ✅ `git diff --check` — no whitespace errors
- ✅ All moved files tracked via `git mv` (history preserved)
- ✅ All documentation links updated
- ✅ Root policy achieved: only essential build/config files remain

## Rationale

**Why docs/planning vs docs/archive?**
- `PHASE2_FEATURE1_DECOMPOSITION.md` and `phase2b-plan-summary.txt` are planning artifacts that may be referenced in future work → `docs/planning/`
- `pr-body.md` was a one-time reconciliation PR description → `docs/archive/` (renamed to clarify context)

**Why docs/ for ACKNOWLEDGMENTS?**
- Acknowledgments are documentation, not marketing/user-facing material
- Keeps root minimal while maintaining discoverability via relative links

**Why video-production/ for slides?**
- Slides are video production assets (frame overlays or reference images)
- Centralizes all video materials in one location

## Impact

- Root clutter eliminated while preserving all durable documentation
- Git history intact for all moved files (renames, not copy+delete)
- No broken links
- Aligns with team's root cleanliness policy

---

**Executed by:** Ricken (DevRel/Writer)  
**Scope:** Documentation organization, root hygiene

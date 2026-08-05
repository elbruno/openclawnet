# CI Decision: Daily Public Sync + Landing Page Updates

**Date:** 2026-05-09  
**Author:** Drummond (Platform Hardening / DevOps)  
**Status:** RECONCILED (2026-05-09)  
**Request:** Bruno Capuano (via Mark)

---

## Context

Bruno asked whether workflow run https://github.com/elbruno/openclawnet-plan/actions/runs/25457266955 is the workflow that syncs code/docs from plan repo to public repo. Confirmed: it's the `Sync to Public Repo` workflow (`.github/workflows/sync-to-public.yml`).

Bruno's requirements:
1. Must run **daily** (not just on push)
2. Must update the main product page (`docs/landing/index.html` in public) with:
   - Date of code sync
   - Top 3-5 latest changes/improvements

---

## Original Implementation Issues

**PROBLEM IDENTIFIED (2026-05-09):**
- Ricken (DevRel / Writer) added markers in main `Latest Updates` section:
  - `<!-- SYNC_METADATA_START -->` / `<!-- SYNC_METADATA_END -->`
  - `<!-- LATEST_CHANGES_START -->` / `<!-- LATEST_CHANGES_END -->`
- But my workflow implementation used different markers:
  - `<!-- SYNC_DATE_START -->` / `<!-- SYNC_DATE_END -->`
  - `<!-- RECENT_CHANGES_START -->` / `<!-- RECENT_CHANGES_END -->`
- This created duplicate update zones in the footer that didn't satisfy the intended product-page Top 5 section.

---

## Reconciliation (2026-05-09)

### Changes Made

1. **Updated `.github/workflows/sync-to-public.yml`:**
   - Changed workflow to target Ricken's markers: `SYNC_METADATA_START/END` and `LATEST_CHANGES_START/END`
   - Updated metadata format to include both date and source SHA
   - Updated changes format to use tile-based layout (matching landing page design) instead of plain list
   - Top 5 latest improvements from public-safe commits

2. **Updated `docs/landing/index.html`:**
   - Removed duplicate footer markers (`SYNC_DATE_START/END` and `RECENT_CHANGES_START/END`)
   - Footer now clean, only references source on GitHub
   - Main `Latest Updates` section (lines 352-401) is the single source of sync metadata

3. **Updated `docs/architecture/sync-plan-to-public.md`:**
   - Documented correct marker names: `SYNC_METADATA_START/END` and `LATEST_CHANGES_START/END`
   - Updated description to include source SHA in metadata

### Marker Specification

**SYNC_METADATA section:**
- Location: Main product page, `🔄 Latest Updates` section
- Format: Date + short SHA in styled box
- Example: `Last synced: 2026-05-09 · abc123`

**LATEST_CHANGES section:**
- Location: Main product page, `🔄 Latest Updates` section
- Format: Up to 5 tile components with icons
- Content: Top 5 user-facing improvements from recent commits
- Filters out: `sync:`, `chore:`, `docs:` commits, `[skip ci]` markers

---

## Decision

### 1. Daily Schedule Trigger

Added `schedule` trigger to `.github/workflows/sync-to-public.yml`:

```yaml
schedule:
  # Daily at 2:00 AM UTC
  - cron: '0 2 * * *'
```

**Rationale:**
- Ensures public repo stays current even when no pushes occur
- 2 AM UTC avoids peak hours, minimizes resource contention
- Preserves existing `push` and `workflow_dispatch` triggers

### 2. Landing Page Update Mechanism

**Implementation:**
- Marker-based replacement using HTML comments:
  - `<!-- SYNC_METADATA_START -->` ... `<!-- SYNC_METADATA_END -->`
  - `<!-- LATEST_CHANGES_START -->` ... `<!-- LATEST_CHANGES_END -->`
- Sync date: formatted as `YYYY-MM-DD` from `steps.config.outputs.date`
- Source SHA: short SHA from `steps.config.outputs.source_sha_short`
- Recent changes: extracted from last 20 commits touching synced paths, filtered to Top 5
- Filters out `sync:`, `chore:`, `docs:` commits and `[skip ci]` commits to avoid noise
- Formatted as HTML tile components to match existing landing page design

**Security:**
- Only uses commit messages from public-safe paths (already filtered by sync-config)
- No secrets or private paths exposed
- Fails gracefully if markers not present (logs notice, continues)
- **HTML Escaping (2026-05-09):** Commit subjects are escaped before insertion into HTML to prevent malformed HTML or potential injection from special characters (`&`, `<`, `>`, `"`, `'`)

**Output Format:**
```html
<!-- SYNC_METADATA_START -->
<div style="...">
  <span>Last synced:</span> 2026-05-09 ·
  <span>abc123</span>
</div>
<!-- SYNC_METADATA_END -->

<!-- LATEST_CHANGES_START -->
<div class="row">
  <div class="tile">...</div>
  <div class="tile">...</div>
  ...
</div>
<!-- LATEST_CHANGES_END -->
```

### 3. Landing Page Changes

Modified `docs/landing/index.html`:
- Main `Latest Updates` section includes both sync metadata and changes (wrapped in markers)
- Default content shows until first automated sync
- **Removed duplicate footer markers** that were added in initial implementation

**Handoff to Ricken:**
- Markers are now in place and functional
- Ricken can adjust styling, positioning, or layout as needed
- Markers must remain for automated updates to work

---

## Validation

### YAML Syntax
✅ GitHub Actions syntax valid (schedule cron format correct)  
✅ Workflow step structure follows existing pattern  
✅ Bash script uses proper error handling (`set -e`)

### Daily Schedule
✅ Cron expression `0 2 * * *` = daily at 2:00 AM UTC  
✅ Trigger does not conflict with `push` or `workflow_dispatch`

### Sync Exclusions
✅ `sync-to-public.yml` still excluded via filtered_mirror rule in `.github/sync-config.yml`  
✅ Workflow removes itself from staging tree (line 180: `rm -f staging/.github/workflows/sync-to-public.yml`)

### Content Safety
✅ Only uses commit messages from synced paths (src, tests, scripts, docs/sessions, docs/manuals, docs/landing)  
✅ Filters out internal sync commits, chore commits, and skip-ci commits  
✅ No access to private paths (.squad, docs/analysis, docs/inbox, skills)

### Marker Reconciliation (2026-05-09)
✅ Workflow now targets correct markers: `SYNC_METADATA_START/END` and `LATEST_CHANGES_START/END`  
✅ Duplicate footer markers removed from landing page  
✅ Architecture doc updated with correct marker names  
✅ Workflow formats changes as tiles to match product page design

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| **Commit messages leak internal details** | Only extract from public-safe paths; filtered by existing sync-config |
| **Marker removal breaks automation** | Workflow logs notice but continues; sync PR still created |
| **Daily sync creates noise** | Only creates PR if changes detected; no-op if public already in sync |
| **Landing page grows stale** | Daily schedule ensures freshness within 24 hours |
| **Duplicate markers cause confusion** | ✅ RESOLVED: Removed duplicate footer markers, single source in main section |

---

## Documentation Updates

- ✅ `.github/workflows/sync-to-public.yml` — added schedule trigger + landing page update step, reconciled to use correct markers
- ✅ `docs/architecture/sync-plan-to-public.md` — documented daily schedule, landing page mechanism, and correct marker names
- ✅ `docs/landing/index.html` — added main sync markers in Latest Updates section, removed duplicate footer markers

---

## Testing Plan

1. **Dry-run test** — Run `workflow_dispatch` with `dry_run: true`
2. **Verify staging tree** — Check that landing page markers are updated
3. **First real sync** — Monitor PR creation and landing page content
4. **Wait for scheduled run** — Verify cron trigger fires at 2 AM UTC

---

## Handoff Notes

**For Bruno:**
- Daily sync now enabled at 2 AM UTC
- Landing page will auto-update with sync date + source SHA + Top 5 latest changes
- No additional secrets required beyond existing `PUBLIC_REPO_TOKEN`
- **Marker mismatch resolved** — workflow now updates Ricken's main Latest Updates section correctly

**For Ricken (Content/Documentation):**
- Main `Latest Updates` section is the single authoritative sync zone
- Duplicate footer markers removed
- Feel free to adjust styling, layout, or positioning of the main section
- **Do not remove** `<!-- SYNC_METADATA_START/END -->` and `<!-- LATEST_CHANGES_START/END -->` comments
- If you want sync metadata in a different location, move the entire marker blocks together

**For Mark (Lead Architect):**
- Workflow documentation updated in `sync-plan-to-public.md`
- No changes to sync-config schema or path mappings
- Landing page update is non-blocking (continues if markers missing)
- **Marker reconciliation complete** — no more duplicate zones

---

## Status

**RECONCILED (2026-05-09)** — Marker mismatch resolved. Ready for testing via workflow_dispatch before first scheduled run.

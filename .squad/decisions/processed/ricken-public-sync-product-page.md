# Public Sync Product Page — Content Decision

**Status:** IMPLEMENTED  
**Date:** 2026-05-09  
**Author:** Ricken (DevRel / Writer)  
**Context:** Bruno's request via Mark to add daily sync metadata and latest changes to the landing page

---

## The Ask

Bruno confirmed the `Sync to Public Repo` workflow should run daily and asked that it update the main product page with:
1. Date of the code sync
2. Top 3 or Top 5 latest changes/improvements added to OpenClawNet

---

## Decision: Top 5 Latest Improvements

**VERDICT: Recommend TOP 5 over Top 3**

### Reasoning

1. **Layout supports it** — The landing page uses a grid-based design with responsive tiles. Five items fill nicely at all breakpoints (3 on large screens, 2-3 on tablets, 1-2 on mobile).

2. **Visual balance** — A 3-item list would leave odd spacing in the existing design. Five items feel more complete and substantial.

3. **Better value for readers** — With daily syncs, Top 3 might feel sparse. Top 5 gives a richer picture of recent activity without overwhelming.

4. **Precedent in design system** — Existing sections show 3-4 items in grids; 5 items follows this pattern well.

---

## Implementation Details

### Location

Added a new `<section>` between the existing "🛠️ Live Resources" section and the footer. This placement:
- Appears after primary navigation content
- Remains above-the-fold on most screens
- Doesn't compete with session slides or primary CTAs

### Marker Comments

```html
<!-- SYNC_METADATA_START -->
<!-- AUTO-UPDATED BY sync-to-public.yml WORKFLOW -->
<!-- DO NOT EDIT MANUALLY — CHANGES WILL BE OVERWRITTEN -->
...sync metadata content...
<!-- SYNC_METADATA_END -->

<!-- LATEST_CHANGES_START -->
<!-- AUTO-UPDATED BY sync-to-public.yml WORKFLOW -->
<!-- DO NOT EDIT MANUALLY — CHANGES WILL BE OVERWRITTEN -->
...latest changes content...
<!-- LATEST_CHANGES_END -->
```

These marker names align with standard CI/CD placeholder patterns and are clear for Drummond (workflow engineer) to implement.

### Public-Safe Copy

All content is crafted for external consumption:
- No references to private planning repo
- No internal code names or agent identifiers
- No sprint/velocity/roadmap details
- Generic improvement descriptions that could come from public commit history
- Focuses on user-visible features and fixes

### Seed Content

Provided initial seed content for both sections:
- **Sync metadata:** Shows placeholder date/SHA that workflow will replace
- **Latest changes:** Five realistic improvement examples that demonstrate the expected tone and format

---

## Workflow Integration Notes for Drummond

The workflow (`sync-to-public.yml`) should:

1. **Extract sync metadata** (already available in workflow)
   - Date: `${{ steps.config.outputs.date }}`
   - SHA: `${{ steps.config.outputs.source_sha_short }}`
   - Timestamp: `${{ steps.config.outputs.timestamp }}`

2. **Generate latest changes list**
   - Parse last 15-20 commits from plan repo
   - Filter to public-relevant changes (exclude docs/analysis/, .squad/, etc.)
   - Extract commit subjects or PR titles
   - Format as HTML list items (Top 5)
   - Use generic descriptions if commit messages are too internal

3. **Update landing page** in staging tree
   - Use sed/perl to replace content between markers
   - Preserve HTML structure and styling
   - Keep marker comments intact for next sync

4. **Verification**
   - Validate HTML doesn't break (basic syntax check)
   - Ensure markers are still present after replacement
   - Test page loads in CI if possible

---

## Alternatives Considered

### Top 3 vs Top 5
- **Top 3:** Simpler, less work to curate — but visually sparse and potentially underwhelming
- **Top 10:** Too many, risks looking like a changelog dump
- **CHOSEN: Top 5** — Goldilocks zone for content/visual balance

### Location on Page
- **Hero section:** Too prominent, competes with main CTAs
- **After sessions:** Feels disconnected from live content
- **CHOSEN: Above footer** — Natural "what's new" spot without disrupting primary navigation

### Content Format
- **Plain text list:** Works, but bland
- **Timeline component:** Over-engineered for daily updates
- **CHOSEN: Tile grid** — Matches existing design system, scalable

---

## Success Criteria

✅ Landing page has stable markers for workflow to update  
✅ Initial seed content is public-safe and reads naturally  
✅ Layout integrates smoothly with existing page design  
✅ Runbook updated with daily sync behavior explanation  
✅ Clear handoff notes for Drummond to implement workflow side

---

## Future Enhancements (Out of Scope)

- Interactive "show more" to expand beyond Top 5
- Link each change to GitHub commit/PR (requires careful filtering)
- RSS feed of changes for subscribers
- Bilingual content (English + Español) to match session slides

---

## References

- Landing page: `docs/landing/index.html`
- Sync workflow: `.github/workflows/sync-to-public.yml`
- Sync plan doc: `docs/architecture/sync-plan-to-public.md`
- Bruno's request: Conveyed via Mark (Lead Architect)

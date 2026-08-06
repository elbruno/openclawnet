# Mark's History - Archived Sessions

## 2026-04-22 — Blazor Tables Upgrade Research & Proposal

**Task:** Research and propose options for upgrading all tables in the Blazor app with modern features (sort, filter, page, export, column visibility, density, responsive, multi-select, a11y).

**Key Finding:** 10 data tables using Bootstrap `<table>` markup across 9 pages. Evaluated 5 grid libraries in 2026 landscape: QuickGrid (simple/fast), MudBlazor (batteries-included/Material), Radzen (feature-rich), Syncfusion (commercial/free for <$1M revenue), Telerik/DevExpress (enterprise/overkill).

**Recommendation:** Path B — MudBlazor MudDataGrid (MIT, 95% features built-in, largest community, future-proof 70+ components). Trade-off: requires theme decision (replace Bootstrap app-wide, hybrid, or customize MudBlazor to Bootstrap). Reference: `docs/proposals/blazor-tables-upgrade.md` (38 KB).

**Learnings:**
- Shared wrapper components (`AppDataGrid.razor`) for consistent styling/states
- Server-side data for large tables (Sessions, Tool Log, Job Run Events)
- Standard `ColumnMetadata` record for reusable column config
- Empty/loading/error state handling in every wrapper
- Theme consistency matters — decide early, affects all UI components

---

## 2026-04-23 — Documentation Updates for Recent Shipped Changes

**Task:** Update project docs to reflect recent code changes (Demo Templates → Draft, `/jobs/templates` auto-nav, JobRun 5-min timeout, `Trigger Now` vs `Start` distinction).

**Docs Updated:**
1. `docs/manuals/30-jobs.md` — Clarified Draft state, Job Run Lifecycle section, Run Now vs Start
2. `docs/demos/real-world/01-document-pipeline/README.md` — Updated button labels + job state flow
3. `docs/architecture/jobs.md` — Added JobRun Lifecycle & Timeout Handling section

**Learning:** When shipping behavioral changes, docs stale predictably (quick-start demos, lifecycle docs, UI action tables, demo walkthroughs). Recommended pattern: ship behavioral changes with doc-sync checklist to avoid post-hoc scanning.

---

## 2025-01-23 — Job Output Dashboard Evaluation

**Task:** Evaluate options for new dashboard/console to display job outputs. Bruno identified gap: users can't easily check progress/output of scheduled jobs (GitHub issue summarizer, website watcher, folder health reports).

**Research Summary:**
- Current job output stored as plain string in `JobRun.Result` — no structured types
- `JobRunEvent` table already logs tool calls in queryable format (underutilized)
- Existing UI (JobDetail.razor) shows result truncated to 50 chars in tooltip

**7 Options Evaluated:**
1. Enhance JobDetail page ⭐⭐⭐
2. New Outputs Blazor page `/outputs` ⭐⭐⭐⭐
3. Standalone Dashboard service ⭐⭐⭐
4. `PostToDashboard` tool (agent-driven) ⭐⭐⭐⭐⭐
5. Artifacts model (`JobRunArtifact`) ⭐⭐⭐⭐
6. External integration (GitHub, Notion) ⭐⭐
7. SignalR live updates ⭐⭐⭐⭐ (enhancement)

**Recommendation:** Phased rollout:
- **Phase 1 (2-3 weeks):** Options 2+5 (Outputs page + Artifacts with auto-detect, markdown rendering)
- **Phase 2 (1-2 weeks):** Options 4+7 lite (Dashboard tool + SignalR for new-run notifications)
- **Phase 3 (Future):** Option 3 (Standalone Dashboard service) only if multi-user SaaS

**Open Questions for Bruno:** Artifact inline threshold (50 KB)? Retention policy (100 runs + 30 days)? Multi-user filtering? File download allowlist? Phase 2 priority?

**Reference:** `docs/proposals/job-output-dashboard.md` (850 lines)

---

## 2025-01-24 — Job Output Dashboard Implementation Plan

**Task:** Create detailed implementation plan based on Bruno's hybrid decision (Home widget + separate Channels site).

**Bruno's Decision:** Hybrid of Helly's Concepts B + C:
- **A) Home Dashboard Widgets** — New landing page at `/` in `OpenClawNet.Web` with recent job cards
- **B) Output Channels** — Brand-new Blazor Server app (`OpenClawNet.Channels`) as separate Aspire resource

**Key Learnings:**
- New Aspire service requires coordinating csproj, AppHost registration, port allocation, service discovery
- Aspire service naming must be unique; existing `OpenClawNet.Services.Channels` (Teams bot) conflicts with new `OpenClawNet.Channels`
- Adapter seam pattern: define interface NOW (e.g., `IChannelDeliveryAdapter`) even if implementations Phase 2
- Schema migration: use existing `SchemaMigrator` pattern with denormalized columns + indexes for efficient queries
- Inline vs disk threshold: 64 KB balances DB size vs I/O

**Decisions Locked:**
1. Project name: `OpenClawNet.Channels` (Aspire: `"channels"`)
2. Storage: 64 KB inline, larger to disk
3. Retention: 100 runs + 30 days
4. v1 transport: Polling (10s); SignalR Phase 1.2
5. Routes: Chat `/` → `/chat`; new Home at `/`

**Blocking Questions for Bruno:**
1. Rename Teams bot `"channels"` → `"teams-bot"`?
2. Chat → `/chat` move acceptable?
3. Confirm 64 KB threshold + 100/30d retention?
4. Confirm 10s polling interval?

**Reference:** `docs/proposals/job-output-dashboard-plan.md` (900 lines)

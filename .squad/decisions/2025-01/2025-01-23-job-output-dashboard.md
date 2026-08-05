# Decision: Job Output Dashboard Architecture

**Date:** 2025-01-23  
**Author:** Mark (Lead Architect)  
**Status:** Awaiting Bruno's Review  
**Context:** `docs/proposals/job-output-dashboard.md`

---

## Summary

Evaluated 7 options for displaying job outputs (recurring background tasks: GitHub issue summarizer, website watcher, folder health reports). Current state: `JobRun.Result` is a plain string blob — no markdown rendering, no cross-job aggregation, no file artifact handling.

**Recommended approach:** Phased rollout

- **Phase 1 (2-3 weeks):** New `/outputs` Blazor page (aggregates JobRun results across all jobs) + `JobRunArtifact` entity (typed artifacts: markdown/JSON/file). Auto-detect artifacts from tool calls. Markdown rendering via Markdig.
  
- **Phase 2 (1-2 weeks):** `dashboard.post_to_dashboard` tool (agents explicitly post summaries) + SignalR lite (new-run notifications only, no progress streaming yet).

- **Phase 3 (Future):** Standalone Dashboard service (Option 3) only if multi-user SaaS. External integrations (GitHub Issues, webhooks) as optional tools.

**Key insight:** `JobRunEvent` table already logs all tool calls (structured). UI doesn't expose it yet. Surfacing these events enables progress timelines and real-time updates without new storage model.

---

## Open Questions for Bruno

Need answers before implementation:

1. **Artifact storage threshold:** 50 KB inline (DB) vs. disk? Or hybrid with configurable threshold?
   
2. **Retention policy:** Keep last 100 runs + 30 days? Or different limits?

3. **Multi-user filtering:** Defer (assume single-user) or plan for team deployments now?

4. **File download allowlist:** Which folders are safe to serve downloads from? (`data/`, `docs/`, other?)

5. **Phase 2 priority:** Tool-driven posts (explicit agent control) or SignalR live updates (impressive demos)? Both are 1-2 weeks effort — which ships first?

---

## Why This Matters

- **Demo-friendliness:** Bruno's conference talks showcase recurring jobs (GitHub summarizer, folder health). Need a clean "outputs feed" surface, not raw SQL tables.
  
- **Agent philosophy alignment:** Option 4 (tool-driven posts) matches "agents who do things" — agents decide what to surface, not system auto-posting everything.

- **Scalability:** Phase 1 reuses existing infra (Web app, EF Core, MudBlazor). Phase 2 adds SignalR only if needed. No premature standalone service.

---

## Next Step

Bruno reads `docs/proposals/job-output-dashboard.md` (850 lines, 7 options evaluated, user scenarios, data flow diagram). Answers 5 open questions. Approves Phase 1 scope → Mark implements (EF migration, Outputs page, artifact detection, markdown rendering).

**Estimated delivery:** 2-3 weeks for Phase 1 MVP.

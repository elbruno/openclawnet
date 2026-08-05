# 2026-04-24 — Scheduler Channels Link + Tool Error Diagnostics (3 commits shipped)

**User Request (job-detail page, port 5020, job ID 209cae22):**
1. Add link to Channels app (port 7030) for easier log access
2. Fix URL Markdown Summary job failing silently — runs show as Completed despite "Error: markdown_convert tool failed: n..." (truncated)

**Helly Shipped (3 commits, main):**

1. **Scheduler job-detail: "Open in Channels" button**
   - Links to `https://localhost:7030/channels/{jobId}`
   - Base URL wired via Aspire `Channels__BaseUrl` env var with localhost fallback
   - UX: Users can navigate directly from Scheduler run to Channels Failure Details card

2. **MarkItDownTool error reporting**
   - Now logs `ex.ToString()` for full stack traces
   - Returns useful error messages with URL + exception type for HTTP/MarkItDotNet/empty/timeout cases
   - Solves the "truncated" error problem; tools can now surface real diagnostics

3. **JobExecutor intelligent run status**
   - Inspects `result.ToolResults` after agent execution
   - If any tool returned `Success=false`, flips `JobRun.Status` to "failed" with joined diagnostics in `JobRun.Error`
   - Channels Failure Details card now auto-activates when tool failures occur
   - Bonus: Scheduler Run History cell no longer truncates at 60 chars — uses `pre-wrap` + `max-height` + hover title

**Decision Document:**
`.squad/decisions/inbox/helly-channel-link-and-tool-errors.md`

**Agents:** Helly (all 3 commits)

**Impact:**
- Scheduler job-detail now bridges to Channels for full diagnostics
- Tool failures are visible and debuggable (no more silent truncation)
- Run History display clarity improved (longer results visible on hover)

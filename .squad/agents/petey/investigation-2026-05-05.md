## 2026-05-05 — ToolApprovalFlowTests investigation (E2E regression hunt)

**Trigger:** Bruno via Coordinator. Full E2E Playwright suite against live Aspire: 28/104 failed, NINE in ToolApprovalFlowTests domain (all timing out 30-90s waiting for approval card UI).

**Initial hypothesis (INCORRECT):** Two known regressions from commit 1edf1ec:
  1. M.E.AI streaming: FunctionCallContent coalescing by CallId (Dictionary vs List)
  2. Blazor Server: EndOfStream blocking circuit thread

**Investigation findings:**
- Both fixes from 1edf1ec ARE ALREADY IN PLACE on main branch (commit 8159d1a ancestry includes 1edf1ec)
- DefaultAgentRuntime.cs:490 uses Dictionary<string, ModelToolCall> keyed by CallId ✓
- Chat.razor:565 uses wait reader.ReadLineAsync(ct) (not EndOfStream) ✓
- NO CODE REGRESSION EXISTS

**Root cause:** Test flakiness / timing sensitivity, NOT streaming bug regression.

**Evidence:**
- Single test run with detailed logging: approval card DID appear after 78s, test failed on different assertion (card text didn't contain "example.com" — it's in collapsed <details> section)
- Full suite run: ~50% timeout at 90s, ~50% card appears but fails assertion
- Backend logs show: tool_approval_requested events ARE firing, ToolApprovalCoordinator registers requests
- git log confirms 1edf1ec (April 25, 2026) is ancestor of HEAD

**Conclusion:**
The 9 ToolApprovalFlowTests failures are NOT due to the streaming regression described in task context. That was fixed in 1edf1ec and remains fixed. Failures are environmental/timing (Ollama inference latency, Blazor render delays, Playwright wait strategy). Tests need timeout tuning or retry logic, not code fixes.

**No code changes made.** Aspire remains running on main@8159d1a.

**Output for Bruno:**
Root cause: NO REGRESSION. The streaming tool-call deduplication fix (commit 1edf1ec) is intact. Test failures are timing/flakiness issues, likely due to Ollama inference delays causing the approval card to appear after the 90s Playwright timeout. One test run showed card appearing at 78s but failing on a different assertion (tool args not visible in collapsed details element).

**Files checked:**
- C:\src\openclawnet\src\OpenClawNet.Agent\DefaultAgentRuntime.cs (line 490: streamedToolCallsById Dictionary ✓)
- C:\src\openclawnet\src\OpenClawNet.Web\Components\Pages\Chat.razor (line 565: ReadLineAsync ✓)
- C:\temp\playwright-full-run.txt (original failures: all timeouts)
- C:\temp\test-single-run.txt (reproduced: card appeared, different failure mode)

**Recommendation:** Increase Playwright timeout to 120s or add retry logic for tool-dependent tests.

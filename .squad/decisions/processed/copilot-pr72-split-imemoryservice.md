### 2026-04-30T23:12Z: PR #72 — Split IAgentMemoryStore from IMemoryService
**By:** Bruno Capuano (via Copilot)
**What:** Approved Mark's recommendation to introduce a new `IAgentMemoryStore` boundary rather than expanding the existing `IMemoryService`. Per-agent vector memory lives behind `IAgentMemoryStore`; today's summary-style service stays on `IMemoryService`.
**Why:** Bruno's verdict on PR #72 open question #2.
**Status:** Decided. Implementation pending vector-store choice (#1) and tool transport (#3).

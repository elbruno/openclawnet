# Tool Approval Events Become First-Class Chat Messages

**Date:** 2026-04-26  
**Author:** Mark (Lead - Architecture & Backend)  
**Status:** Approved (proposal ready for implementation)

---

## Decision

**Tool-approval events will be stored as `ChatMessageEntity` rows with a `MessageType` discriminator (`"chat"` vs. `"tool_approval"`), rendered as persistent muted bubbles in the chat UI, and survive page reloads.**

---

## Context

Bruno requested that tool-approval decisions (approve/deny/timeout) persist in the chat conversation as visible bubbles, not just transient cards. Today:

- The `ToolApprovalCard` component renders inline during approval flow
- Once the user clicks Approve/Deny, the card disappears (`PendingApproval = null`)
- No visual record remains in the chat history
- Approval events **are** audited in `ToolApprovalLog` table, but this is separate from chat messages

**Problem:** There's no in-conversation audit trail. On page reload, users lose all evidence of what was approved.

**Goal:** Make tool approvals first-class messages in the chat stream.

---

## Alternatives Considered

### Option A: Separate `ToolApprovalEvents` table with foreign key to `ChatSession`

**Pros:**
- Clean separation of concerns (approval events != chat messages)
- No discriminator needed

**Cons:**
- **Split loading logic:** Must load `Messages` + load `ToolApprovalEvents` + interleave by timestamp (complexity)
- Two tables to maintain, two queries to paginate
- Harder to get chronological order correct (especially if clocks drift)

### Option B (CHOSEN): Discriminator in `ChatMessageEntity` (`MessageType = "tool_approval"`)

**Pros:**
- **Single ordered timeline:** All conversation events in one `Messages` table
- **Simple loading:** Reload session → reload messages → done (no join, no interleave)
- **Chronological integrity:** SQL `ORDER BY OrderIndex` or `CreatedAt` works naturally
- **Extensible:** Future event types (e.g., "agent_switch", "background_task_complete") can reuse the same pattern

**Cons:**
- `ChatMessageEntity` now has two shapes (chat vs. tool_approval) — minor schema split
- Need to check `MessageType` in rendering loop

**Why B wins:** Simplicity. Single load, single sort, single render loop. The discriminator pattern is well-understood and works for other event types we'll add later (agent switches, errors, system notifications).

---

## Implementation Plan

See `docs/proposals/2026-04-26-tool-approval-bubbles.md` for full details.

**Summary:**

1. **Schema:** Add `MessageType TEXT DEFAULT 'chat'` and `ToolApprovalDataJson TEXT` columns to `Messages` table via `SchemaMigrator`
2. **Backend:** `ToolApprovalCoordinator.TryResolve` creates a message entity on approval/deny
3. **NDJSON stream:** Emit `tool_approval_resolved` event for live updates
4. **Frontend:** Chat.razor consumes event, adds bubble to `_messages`, collapses transient card
5. **Persistence:** Bubbles render from history on page load (no special logic — standard message loop)

**Phases:** A (backend storage), B (NDJSON stream), C (bubble rendering), D (E2E test)

**Owners:** Helly (phases A-C), Dylan (phase D)

---

## Consequences

### Positive

- ✅ **Auditability:** Every approval decision is visible in chat history
- ✅ **Persistence:** Bubbles survive page reload, session export, transcript sharing
- ✅ **Simplicity:** One table, one query, one render loop
- ✅ **Extensibility:** Pattern works for future system events (agent switches, errors, background task completions)

### Negative

- ⚠️ **Schema split:** `ChatMessageEntity` has two shapes (mitigated by discriminator pattern)
- ⚠️ **Render complexity:** Message loop must check `MessageType` (minor — simple `if` statement)
- ⚠️ **Migration:** Existing DBs need column additions (mitigated by `SchemaMigrator` with defaults)

### Neutral

- 📊 **Storage growth:** Each approval adds one message row (~500 bytes). For a typical session with 5 tool calls, this is +2.5KB. Negligible.
- 📊 **Query performance:** No join, no pagination complexity — same SELECT as before. No impact.

---

## Related Decisions

- **2026-04-26: 10/10 Tool E2E Milestone** — This builds on the approval flow infrastructure that passed all tests
- **Tool Approval UX Proposal** (`docs/proposals/tool-approval-ux-proposal.md`) — Future friction-reduction (auto-approval rules) will layer on top of this
- **Forbid-alternatives playbook** (`docs/testing/tool-test-prompt-playbook.md`) — New E2E test uses this pattern

---

## Open Questions

None — proposal is ready for implementation.

**Next step:** Helly to start Phase A (backend storage), Dylan to prepare E2E test scaffold.

---

## Rationale for Inbox vs. Main Decisions Log

This decision is being filed in `.squad/decisions/inbox/` (not merged into `.squad/decisions.md`) because:

1. **Proposal is complete but not yet implemented** — storing here allows team to review before Helly starts coding
2. **Mark prefers decision-first workflow** — log the architectural choice BEFORE implementation begins, not after
3. **Bruno may have questions** — inbox format allows easy discussion without polluting the canonical decisions log

Once Helly ships Phase C (bubble rendering works end-to-end), this decision will be promoted to `decisions.md` under "Tool Approval & Security".

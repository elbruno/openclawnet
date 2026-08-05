# Channels & Scheduled Jobs — multi-instance demo templates + DTO repair

## What this PR does

Repairs DTO field-name drift that left the Channels homepage blank and Run-Job detail surfaced errors. Unlocks unlimited demo template instances with auto-suffix naming (e.g. "Website Watcher (2)"), inline rename on the Jobs page, and template-lineage tracking via the new `Jobs.SourceTemplateName` column. Fixes enum default ordering to restore Markdown artifact round-trip integrity.

## User-visible changes

- ✅ Demo templates now create unlimited instances with auto-suffixed names ("Website Watcher (2)", "(3)", ...) instead of silently failing with 409 Conflict
- ✅ Inline rename available on the Scheduled Jobs page — click the pencil icon next to the job name, edit in place, press Enter to save or Esc to cancel; duplicate names rejected
- ✅ Auto-suffixed names can be freely renamed to any unique name after creation
- ✅ Channels homepage displays all message history and activity (DTO field-name parity restored)
- ✅ Run-Job detail page now shows complete agent execution context (AgentProfileName, InputSnapshotJson, ExecutedByAgentProfile)

## Bug fixes

- 🐛 **Channels homepage was blank** — Web/Razor components bind to ChannelSummaryDto properties that drifted from Gateway DTO definitions. Restored field parity for LastActivityUtc, TotalArtifacts.
- 🐛 **Run-Job detail surfaced missing-field errors** — Added AgentProfileName, InputSnapshotJson, ExecutedByAgentProfile to JobRunDto; now round-trips cleanly.
- 🐛 **Demo templates allowed only 1 instance per template** — Demo-setup endpoints (Website Watcher, Doc Pipeline, Folder Health) short-circuited with 409 Conflict when a canonical-named job existed. Replaced gate with GenerateUniqueJobNameAsync so each click creates a fresh, fully editable ScheduledJob with auto-suffixed name.
- 🐛 **JobRunArtifactKind.Markdown silently stored as Text** — Enum default (position 0) was Markdown; C# default value 0 made EF Core's change tracker treat Markdown assignments as "unchanged". Reordered enum so Text=0, Markdown=1, etc., aligning C# default with intended storage.
- 🐛 **POST /api/channels/{jobId}/artifacts returned anonymous type** — Changed Created<{anonymous Guid id}> to Created<object> for Swagger/client-gen compatibility.

## New features

- ✨ **Inline rename for Scheduled Jobs** — Pencil icon next to job name opens a MudTextField; Enter saves with snackbar confirmation, Esc cancels. Reuses existing PUT endpoint; SourceTemplateName stays read-only.
- ✨ **Jobs.SourceTemplateName column** — Tracks template lineage for audit purposes (read-only on edit). Null for jobs created from scratch.

## Schema changes

- **Added column:** `Jobs.SourceTemplateName` (nullable text) — migrated via SchemaMigrator (NOT EF migrations; see ADR in decisions.md for rationale).

## Tests

- **Baseline:** 555 pass / 4 pre-existing failures (unrelated Markdown enum round-trip + endpoint signature before this PR).
- **New coverage this PR:** 24 additional tests — schema parity, multi-instance template stores, channels-home smoke checks, multi-instance auto-suffix, SourceTemplateName immutability on rename, JobRunArtifactKind=Text=0 enum-ordering guard, plus a bUnit scaffold for inline-rename component tests.
- **Final:** 579 pass / 0 fail / 10 skipped (1 DPAPI Windows-only + 7 inline-rename bUnit tests pending MudPopoverProvider scaffolding (follow-up) + 2 channel/schema tests pending ChannelStore.GetAllAsync).

## Known follow-ups (not in this PR)

- **ChannelDetail.razor shape mismatch** — Full investigation report at `.squad/decisions/mark-channeldetail-investigation.md`. Three ranked options pending Bruno's decision (Option A: shape fix, Option B: add ChannelSummaryChannelDto, Option C: ChannelDetailViewDto). Mark recommends Option C.

## Commits

- **d010f33:** fix(channels,jobs): repair DTO contracts & allow multi-instance demo templates
- **e170ccc:** fix: rename UX, Markdown enum round-trip, Created<object> contract
- **1f30536:** test(channels,jobs): add regression coverage + docs for multi-instance + rename

## Squad attribution

**Severance cast:** Mark (lead/triage/docs), Helly (frontend), Irving (backend+schema), Dylan (tests), Scribe (logs/decisions).  
**Coordinated by:** Squad v0.9.1

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>

# OpenClawNet Glossary

> Plain-language definitions of the core concepts. Pinned terms; everything
> else in the codebase should compose from these.

| Term | Definition |
|---|---|
| **Agent** | A runnable persona produced by combining a *profile*, a *provider*, a *model*, and a set of bound *tools*. The thing the user actually talks to. |
| **Agent profile** | A persisted configuration row (`AgentProfile`) describing instructions, default provider/model, and approval defaults. Multiple agents can share a profile. |
| **Provider** | A backend that speaks a model API: `Ollama`, `AzureOpenAI`, `Foundry`, `FoundryLocal`, `GitHubCopilot`. Each provider implements `IAgentProvider`. |
| **Model** | A specific named model on a provider (`gpt-4o`, `llama3:8b`, …). Resolved at runtime via `RuntimeModelSettings` + `ProviderResolver`. |
| **Tool** | A callable function exposed to the LLM, usually backed by an *MCP server*. Tools may require human approval before execution. |
| **MCP server** | A Model Context Protocol server (in-process or external) that exposes one or more tools. |
| **Job** | A persisted, schedulable unit of work (`JobDefinition`). Has a state machine: `Draft → Active → Paused → Completed/Cancelled → Archived`. |
| **Run** | One execution of a *job* (`JobRun`). Produces *artifacts*. |
| **Artifact** | An output produced during a *run* (`JobRunArtifact`). Mirrors `ChatSessionArtifact` for chats. |
| **Channel** | The user-visible stream of *artifacts* for a *job*. Real-time updates are pushed via NDJSON (see `channels-concept.md`). |
| **Chat session** | An interactive conversation with an *agent*. Each turn writes one row into `AgentInvocationLog`. |
| **Invocation** | A single call into an agent — either a chat turn or a job run step. Logged uniformly in `AgentInvocationLog` (Option B from §4c). |
| **Approval** | A human decision to allow or deny a tool call. Persisted in `ToolApprovalLog` with timeout and *remember-this-decision* support. |
| **Sanitizer** | `IToolResultSanitizer` — runs over tool output before it is injected into LLM context. Strips HTML/scripts, clamps length, removes control chars. |

## Naming rules

- Code uses *AgentProfile*, never *persona*.
- Code uses *Job* for the definition and *JobRun* for the execution; never
  *task* (taken by .NET) or *workflow*.
- *Channel* is purely a UX concept on top of artifacts; there is no
  `Channel` entity.

## Related documents

- [Components overview](components.md)
- [Runtime flow](runtime-flow.md)
- [Storage](storage.md)
- [Provider model](provider-model.md)
- [April 2026 concept review](20260425-concept-review.md)

# NemoClaw: Reserved Architecture for Multi-Agent Workflows

## Overview

**NemoClaw** is a conceptual extension of the OpenClaw agent platform architecture designed for coordinating multiple specialized agents working together on complex, multi-step workflows. While OpenClaw .NET currently implements a single-agent orchestrator, this document reserves the space and architectural patterns needed to evolve toward NemoClaw-style multi-agent coordination.

---

## Vision: From Single Agent to Agent Orchestration

| Aspect | OpenClawNet (Single Agent) | NemoClaw Pattern (Future) |
|---|---|---|
| **Scope** | One agent per session | Multiple specialized agents per workflow |
| **Coordination** | Sequential tool calls within one agent | Agent-to-agent delegation and consensus |
| **State** | Session-level context | Workflow-level distributed state |
| **Example** | "Read file, summarize, schedule reminder" | "Research task" → Fact-Checker → Writer → Publisher |
| **Decision Making** | Single agent responds to user | Agents vote/debate before output |

---

## Conceptual NemoClaw Architecture

### 1. **Workflow Orchestrator** (Future)

Extends `IAgentOrchestrator` to manage multiple agents:

```csharp
// Future NemoClaw interface
public interface IWorkflowOrchestrator
{
    Task<WorkflowResult> ExecuteAsync(WorkflowDefinition workflow, WorkflowContext context, CancellationToken ct);
    IAsyncEnumerable<WorkflowEvent> StreamAsync(WorkflowDefinition workflow, WorkflowContext context, CancellationToken ct);
}
```

### 2. **Agent Swarm** (Future)

Replace single `IAgentOrchestrator` with a pool of specialized agents:

```csharp
// Future: Multiple agents with different roles
public interface IAgentSwarm
{
    IReadOnlyList<SpecializedAgent> Agents { get; }
    Task<AgentResponse> DelegateAsync(WorkflowTask task, CancellationToken ct);
    Task<ConsensusResult> RequestConsensusAsync(IEnumerable<AgentResponse> responses, CancellationToken ct);
}
```

### 3. **Workflow Definitions** (Future)

YAML-based workflow specifications (similar to skills):

```yaml
---
name: research-and-publish
agents:
  - researcher
  - fact-checker
  - writer
  - publisher
steps:
  - agent: researcher
    prompt: "Research {topic} and find key facts"
  - agent: fact-checker
    input: researcher.output
    prompt: "Verify these facts"
  - agent: writer
    input: fact-checker.output
    prompt: "Write an article from these facts"
  - agent: publisher
    input: writer.output
    approval_required: true
output: published_article
---
```

### 4. **Distributed State Management** (Future)

Track workflow progress across agents:

```csharp
public interface IWorkflowStateStore
{
    Task<WorkflowState> GetStateAsync(Guid workflowId, CancellationToken ct);
    Task SetStateAsync(Guid workflowId, WorkflowState state, CancellationToken ct);
    Task<IAsyncEnumerable<WorkflowEvent>> GetEventsAsync(Guid workflowId, CancellationToken ct);
}
```

### 5. **Inter-Agent Communication** (Future)

Agents share context and results:

```csharp
public interface IAgentMessageBus
{
    Task PublishAsync(AgentMessage message, CancellationToken ct);
    IAsyncEnumerable<AgentMessage> SubscribeAsync(string topic, CancellationToken ct);
}
```

---

## Mapping OpenClawNet to NemoClaw

### Current OpenClawNet → Future NemoClaw

| Current Component | NemoClaw Evolution | Purpose |
|---|---|---|
| `IAgentOrchestrator` | Becomes one of many `ISpecializedAgent` | Enables agent swapping in workflows |
| `IAgentRuntime` | Wrapped by `IWorkflowOrchestrator` | Manages multi-agent execution |
| Single `IAgentProvider` | Multiple models, one per agent type | Domain-specific model optimization |
| `IToolRegistry` | Shared + agent-specific tool sets | Some tools global, some agent-only |
| `IPromptComposer` | Becomes agent-specific composer | Each agent has its own system prompt |
| `SchedulerTool` | Upgraded to `IWorkflowScheduler` | Schedule entire workflows, not just jobs |
| `ChatSession` | Becomes `WorkflowSession` | Groups multiple agent interactions |
| `ToolCallRecord` | Extended to `AgentInteractionRecord` | Track which agent called which tool |

---

## Building Blocks Already in Place

OpenClawNet's current design supports eventual NemoClaw evolution:

### ✅ Interface Isolation

- `IAgentOrchestrator` is a stable boundary
- Can be composed into larger systems
- Easy to create multiple implementations

### ✅ Dependency Injection

- All services registered in DI container
- Can swap multiple implementations simultaneously
- Easy to provide agent-specific instances

### ✅ Async Streaming

- `IAsyncEnumerable<AgentStreamEvent>` supports real-time coordination
- Works across multiple agents updating simultaneously

### ✅ Stateful Storage

- EF Core entities support workflow-level state
- Can extend schema for multi-agent records

### ✅ Event-Driven Design

- Aspire Dashboard can visualize agent coordination
- HTTP SSE/NDJSON can stream workflow progress to UI

---

## Prototype: Single-Step NemoClaw Composition

While full NemoClaw is future work, you can today compose multiple OpenClawNet agents:

```csharp
// Example: Use two agents sequentially
var researchAgent = serviceProvider.GetRequiredService<IAgentOrchestrator>();

var researchResult = await researchAgent.ProcessAsync(
    new AgentRequest { Message = "Research .NET 10 features" });

// Use first agent's output as context for second
var writerAgent = serviceProvider.GetRequiredService<IAgentOrchestrator>();
var article = await writerAgent.ProcessAsync(
    new AgentRequest { Message = $"Write an article: {researchResult.Response}" });
```

This pattern can be formalized into a `IWorkflowOrchestrator` in a future session.

---

## Future Session: NemoClaw Workflows (Hypothetical Session 5)

If OpenClawNet becomes a multi-session series, Session 5 could introduce:

1. **Workflow Definitions** — YAML format for multi-agent tasks
2. **Agent Swarm** — Spawn specialized agents for specific roles
3. **Consensus Patterns** — Multiple agents vote on decisions
4. **Workflow Monitoring** — Track progress across agents in real-time
5. **Error Recovery** — One agent failure doesn't block the workflow

---

## Conclusion

**NemoClaw** is reserved conceptual space within OpenClawNet's architecture for future multi-agent orchestration. The current single-agent implementation is intentionally designed with:

- Pluggable interfaces that can be multiplied
- Async streaming that supports concurrent agents
- Modular persistence that scales to workflows
- DI patterns that enable swapping

This makes OpenClawNet a solid foundation for evolution toward NemoClaw-style agent workflows, while keeping Session 1–4 focused on teaching single-agent agent architecture in depth.

---

## Reference

- See [OpenClaw Mapping](openclaw-mapping.md) for the current architecture
- See [Overview](overview.md) for system architecture
- See [Components](components.md) for implementation details

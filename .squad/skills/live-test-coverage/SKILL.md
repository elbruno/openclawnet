# Skill: Live Test Coverage Analysis

@extracted: 2026-04-30, petey, from gap analysis of live test infrastructure  
@validated-by: petey (high), dylan (medium)

**Domain:** Testing strategy for LLM-driven platforms

**When to use:** When planning test coverage for systems that interact with real LLM providers (Ollama, Azure OpenAI, Foundry, GitHub Copilot).

---

## Core Principle

**Unit tests with mocks miss 90% of real LLM failure modes.**

In an LLM-driven agent platform like OpenClawNet, the "business logic" IS the interaction with the LLM:
- Does the LLM pick the right tool?
- Does it format JSON arguments correctly?
- Does it recover from tool errors?
- Do streaming tokens arrive in valid format?
- Do provider-specific quirks (Ollama vs AOAI) break the abstraction?

**Mocking the LLM (FakeModelClient) tests the plumbing, not the product.**

---

## Live Test = Real Provider + Real I/O

A "live test" hits a real LLM provider (Ollama at localhost:11434 or Azure OpenAI endpoint) and verifies:
1. **End-to-end flows work** — not just "does the method return 200?" but "does the agent complete the task?"
2. **Provider contracts hold** — error codes, streaming formats, tool-calling schemas
3. **Failure modes surface** — hallucinations, token limits, rate limits, network drops

---

## What to Cover (Priority Order)

### 🚨 Tier 1: Core Product Flows (MUST have live tests)
1. **Agent loop end-to-end (multi-turn tool execution)**
   - LLM picks tool → invokes → result feeds back → LLM produces final answer
   - THE core product flow — if this is broken, the product doesn't work
   - Unit test miss: LLM hallucinates tool args, picks wrong tool, formats JSON incorrectly

2. **Job/scheduled execution against live LLM**
   - JobExecutor → agent runtime → LLM → JobRun/JobRunEvents persisted
   - Catches: profile resolution bugs, tool approval deadlocks, result persistence failures

3. **Streaming chat endpoint**
   - HTTP streaming endpoint with live model → NDJSON tokens → clean termination
   - Catches: NDJSON malformed on errors, connection drops, token limit mid-stream

4. **Tool discovery + invocation (MCP or custom tools)**
   - Agent discovers tools (MCP servers, built-in registry) → LLM picks tool → invokes successfully
   - Catches: schema mismatches, server crashes, result format incompatibility

5. **Conversation-driven session rename**
   - Create a session, drive one real chat turn, trigger auto-rename, then verify the title persists in both the chat header and sessions list
   - Catches: detached-entity updates, missing title refreshes, and UI/session-list drift after rename

### 🟡 Tier 2: Multi-Provider Stability (SHOULD have)
5. **Provider switching (RuntimeModelSettings overlay)**
   - Test calls Ollama → switches to AOAI → second call works without config contamination
   - Catches: singleton state leakage (DeploymentName, Endpoint, Model)

6. **Agent profiles (instructions → LLM behavior)**
   - Profile with custom instructions → LLM output reflects instructions (prompt injection check)
   - Catches: instructions ignored by runtime, profile resolution bugs

### ✅ Tier 3: Error Paths & Edge Cases (NICE to have)
7. **Long context / token limits** — graceful degradation at 8k/32k/128k boundaries
8. **Error paths** — invalid model, expired key, rate limit → clear error messages
9. **Embeddings** (if product uses) — text-embedding round-trip

---

## Test Infrastructure Pattern

### 1. Shared Fixture for Provider Warm-Up
```csharp
public sealed class LiveTestFixture : IDisposable
{
    public OllamaModelClient OllamaClient { get; }
    public AzureOpenAIModelClient? AzureClient { get; }
    public bool IsAzureConfigured { get; }
    
    public LiveTestFixture()
    {
        // Load Gateway user secrets for AOAI config
        // Build clients once per test class (connection pooling)
        // Validate connectivity on startup
    }
}

public class LiveAgentLoopTests : IClassFixture<LiveTestFixture>
{
    // Tests share fixture
}
```

### 2. Parameterize Across Providers
```csharp
public static IEnumerable<object[]> BothProviders()
{
    yield return new object[] { "ollama", "gemma4:e2b" };
    yield return new object[] { "azure-openai", "gpt-5-mini" };
}

[SkippableTheory]
[MemberData(nameof(BothProviders))]
public async Task Agent_MultiTurnToolExecution_CompletesSuccessfully(
    string provider, string model)
{
    var client = provider == "ollama" ? _fixture.OllamaClient : _fixture.AzureClient;
    Skip.IfNot(client is not null, $"{provider} not configured.");
    // ... test body
}
```

**Why parameterize:** Ollama uses different JSON schema, different token limits, different error codes than AOAI. Both are tier-1 providers — test both or risk prod failures on one.

### 3. Skip-on-Unavailable Pattern
```csharp
public static class LiveTestHelper
{
    public static void SkipIfProviderUnavailable(IModelClient? client, string providerName)
    {
        Skip.IfNot(client is not null, $"{providerName} not configured.");
        Skip.IfNot(client.IsAvailableAsync().GetAwaiter().GetResult(), 
            $"{providerName} is not reachable.");
    }
}
```

**Result:** Tests skip gracefully in CI (where AOAI secrets aren't set) but run on developer machines and manual workflow dispatch.

---

## CI/CD Integration

### ❌ DO NOT run live tests on every PR
**Why:**
- Burn AOAI tokens ($0.10–$0.50 per test run)
- Add 2–5 minutes to CI time
- Flaky on network/provider outages

### ✅ DO run live tests:
1. **Manual workflow dispatch** — developer/QA triggers when validating provider changes
2. **Pre-release** — before tagging a release (weekly or monthly)
3. **Post-outage** — after provider outages to verify recovery

**GitHub Actions pattern:**
```yaml
name: Live Tests
on:
  workflow_dispatch:
    inputs:
      provider:
        type: choice
        options: [ollama, azure-openai, both]

jobs:
  live-tests:
    services:
      ollama:
        image: ollama/ollama:latest
    steps:
      - run: docker exec ${{ job.services.ollama.id }} ollama pull gemma4:e2b
      - run: dotnet test --filter "Category=Live"
        env:
          Model__Endpoint: ${{ secrets.AZURE_OPENAI_ENDPOINT }}
          Model__ApiKey: ${{ secrets.AZURE_OPENAI_API_KEY }}
```

---

## Red Flags (Signs You Need More Live Tests)

1. **"It works in direct tool invoke, fails inside agent loop"** → Missing agent loop live test
2. **"Jobs are broken but chat works"** → Missing job pipeline live test
3. **"Ollama works, AOAI fails (or vice versa)"** → Missing provider-parameterized tests
4. **"Tool called but result isn't flowing back"** → Missing tool invocation live test
5. **"Streaming endpoint returns 200 but UI shows nothing"** → Missing streaming live test

---

## Cost Estimates

**Per live test run:**
- **Ollama (local):** Free, ~5–10s runtime
- **Azure OpenAI:** ~$0.02–$0.10 per test (1-5 chat completions, gpt-5-mini pricing)
- **Full suite (8 tests × 2 providers):** ~$0.50–$1.00 per run

**Budget:** $20–$40/month if running weekly. $0 if Ollama-only for daily CI.

---

## Example: OpenClawNet Live Test Gaps (2026-04-30 Analysis)

**Inventory:** 11 live tests (all provider-focused: CompleteAsync, StreamAsync, IsAvailableAsync)

**Critical gaps:**
- ❌ Agent loop end-to-end (multi-turn tool execution) — **HIGH RISK**
- ❌ Job pipeline against live LLM — **HIGH RISK** (jobs are #1 use case)
- ❌ Streaming chat endpoint — **HIGH RISK** (user-facing #1 feature)
- ❌ MCP tool invocation through agent — **HIGH RISK** (product differentiator)
- ❌ Provider switching isolation — **MEDIUM RISK** (contamination bug documented)
- ❌ Agent profile instructions influence — **MEDIUM RISK**

**Result:** 80% of product surface area has zero live coverage. Unit tests with FakeModelClient create false confidence.

---

## Summary

**Live tests are THE validation for LLM-driven platforms.**

Prioritize:
1. Agent loop end-to-end
2. Job/scheduled execution
3. Streaming endpoints
4. Tool invocation

Infrastructure:
- Shared fixture for warm-up
- Parameterize across providers
- Skip gracefully when unavailable
- Manual CI trigger only

**Golden rule:** If a feature's correctness depends on the LLM's behavior, it needs a live test.


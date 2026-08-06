# Skill: Adapter Contract Testing

@extracted: 2026-05-23, copilot, from FunctionResultContent.Result loss fix (IMP-1..7)
@validated-by: copilot (high), confirmed by 7 independent test layers

**Domain:** Testing strategy for adapter layers and abstraction-boundary translation

**Confidence:** `high` (validated across 7 test layers: unit, integration, E2E, Playwright)
- **Validated by:** Real FunctionResultContent.Result loss bug (issue #152) + 7 IMP implementations
- **Verified:** 2026-05-23 (all 7 improvements passing, E2E test index updated)

**When to use:** Whenever implementing a new adapter or internal interface translation layer — especially any code that converts between two message/content representations (e.g., MEAI ↔ OpenClaw, one SDK type to another).

---

## Problem: Silent Data Loss at Adapter Boundaries

Tool result generated successfully — but never reached the LLM.

The `FunctionResultContent.Result` loss scenario:

1. A tool (e.g., `MarkItDownTool`) runs and returns ~1 KB of markdown content.
2. The agent runtime calls `FunctionResultContent(callId, result)` — content is stored in the `.Result` property.
3. `ModelClientChatClientAdapter.ToOpenClawMessage()` converts the MEAI `FunctionResultContent` to an OpenClaw `Message`.
4. **Bug:** The adapter reads `.Text` instead of `.Result` → OpenClaw message content is `null` or empty.
5. The LLM receives an empty tool result and responds with a hallucination ("I couldn't retrieve the content") — no exception, no log warning, no stack trace.

**Why it's invisible to downstream tests:**

- The tool itself is fine — it returns correct output.
- The LLM still responds — just badly.
- Integration tests without content assertions pass (they only check lifecycle, not content).
- The bug lives entirely inside the adapter, at the seam between two abstraction layers.

---

## Root Cause Pattern

Tool results stored in different abstraction layer fields:

| Layer | Field | Type |
|---|---|---|
| MEAI | `FunctionResultContent.Result` | `object?` |
| MEAI | `FunctionResultContent.Text` (via base) | `string?` (may be null) |
| OpenClaw | `Message.Content` | `string` |

The adapter must read `Result?.ToString() ?? Text ?? ""` — not just `Text`.

This same mismatch risk exists for **any** future adapter that translates between abstraction layers with differently-named or differently-typed content fields.

---

## Solution: 5-Step Adapter Testing Recipe

### Step 1: Unit-level round-trip test

- Create a test that sends `{data}` through `ToAdapter → FromAdapter` (or equivalent conversion pipeline).
- Assert output **exactly matches** input: no corruption, truncation, or loss.
- Test all types your adapter handles: `string`, `object`, `null`, collections.
- The test should fail if the adapter reads the wrong field.

```csharp
// Example: ModelClientChatClientAdapterTests
[Fact]
public async Task GetResponseAsync_WithToolRoundTrip_ToolResultContentReachesSecondTurn()
{
    var toolResult = new string('x', 1024); // ~1 KB marker
    var functionResult = new FunctionResultContent("call-1", toolResult);
    // ... build history, invoke adapter, capture second-turn ChatRequest
    Assert.Contains(toolResult, capturedRequest.Messages[1].Content);
}
```

**For each content type `ToOpenClawMessage` handles:**

```
FunctionResultContent.Result = "string value"   → Content = "string value"
FunctionResultContent.Result = new { key = 1 }  → Content = "{ key = 1 }" (or JSON)
FunctionResultContent.Result = null             → Content = "" (not null/exception)
TextContent, ImageContent, DataContent, ...     → test each branch
```

---

### Step 2: Type variant coverage

For each content type the adapter might receive, test it explicitly:

```csharp
// ModelClientChatClientAdapterTests — ToOpenClawMessage variants
[Theory]
[InlineData("hello")]              // string result
[InlineData(null)]                 // null result (IMP-7 edge case)
public async Task ToOpenClawMessage_FunctionResultContent_VariantCoverage(object? result)
{
    var content = new FunctionResultContent("id-1", result);
    // ... verify OpenClaw message content is non-null and matches
}
```

Types covered in `ModelClientChatClientAdapter.ToOpenClawMessage`:
- `FunctionResultContent` (`.Result` field — primary risk)
- `TextContent`
- `ImageContent`
- `DataContent`
- `FunctionCallContent`

---

### Step 3: Asserting fakes that fail-fast on data loss

Do NOT use lifecycle-only fakes (fakes that only check "was the method called?"). Create fakes that validate **message content** on every call:

```csharp
// FakeAssertingToolCallingModelClient pattern
public class FakeAssertingToolCallingModelClient : IModelClient
{
    public Task<ChatCompletion> CompleteAsync(IList<ChatMessage> messages, ...)
    {
        if (_callCount > 0) // second call (after tool result)
        {
            var toolMsg = messages.FirstOrDefault(m => m.Role == ChatMessageRole.Tool);
            if (toolMsg is null)
                throw new InvalidOperationException("Second turn must contain a Tool-role message.");
            if (string.IsNullOrWhiteSpace(toolMsg.Content?.ToString()))
                throw new InvalidOperationException("Tool-role message has empty/whitespace content — adapter dropped it.");
        }
        _callCount++;
        // return scripted response...
    }
}
```

**Rule:** If content is missing or empty, throw immediately with a descriptive error. Don't silently swallow gaps.

---

### Step 4: Integration round-trip — capture downstream consumer input

Capture the full request sent to the downstream consumer (e.g., `ChatRequest` to the model) and assert it contains the original content:

```csharp
// CapturingModelClient + AgentRuntimeStreamTests pattern
public class CapturingModelClient : IModelClient
{
    public List<ChatRequest> CapturedRequests { get; } = new();

    public Task<ChatCompletion> CompleteAsync(IList<ChatMessage> messages, ...)
    {
        CapturedRequests.Add(new ChatRequest(messages, ...));
        return _inner.CompleteAsync(messages, ...);
    }
}

// In test:
var captured = capturingClient.CapturedRequests[1]; // second turn
Assert.True(captured.Messages.Any(m =>
    m.Role == ChatMessageRole.Tool &&
    m.Content?.ToString()?.Contains(expectedToolOutput) == true));
```

This test would fail if the adapter silently drops tool result content before it reaches the model.

---

### Step 5: E2E marker validation

Tool output should include characteristic markers (e.g., `# Source: {url}`, a UUID, a known string).
The final LLM response should reference or include these markers.
If the marker is absent from the LLM output, the tool result never reached the LLM.

```csharp
// ChatUrlSummaryE2ETests marker check
var response = await chatClient.SendAsync("summarize https://elbruno.com");
Assert.Contains("elbruno.com", response.Text, StringComparison.OrdinalIgnoreCase);
Assert.True(response.Text.Length >= 200,
    "Response too short — tool result likely didn't reach the LLM.");
```

Playwright variant:
```typescript
// Verify tool-injected marker appears in the final response text
await expect(page.locator('[data-testid="assistant-message"]'))
    .toContainText('elbruno.com');
```

---

## Examples — Implementation Locations

| Test | Layer | What it covers |
|---|---|---|
| [`ModelClientChatClientAdapterTests`](../../tests/OpenClawNet.UnitTests/Agent/ModelClientChatClientAdapterTests.cs) | Unit | Round-trip: FunctionResultContent → OpenClaw → back; all type variants including null (IMP-1, IMP-7) |
| [`GatewayWebAppFactory` / `FakeAssertingToolCallingModelClient`](../../tests/OpenClawNet.IntegrationTests/GatewayWebAppFactory.cs) | Integration | Asserting fake fails-fast on missing/empty tool-role message content (IMP-2) |
| [`AgentRuntimeStreamTests`](../../tests/OpenClawNet.UnitTests/Agent/AgentRuntimeStreamTests.cs) | Unit/Integration | `CapturingModelClient` captures second-turn `ChatRequest`; asserts Tool-role content matches tool output exactly (IMP-3) |
| [`LiveMarkItDownToolE2ETests`](../../tests/OpenClawNet.IntegrationTests/Jobs/LiveMarkItDownToolE2ETests.cs) | E2E Live | Real HTTP fetch; response length floor ≥200 chars proves content survived all layers (IMP-5) |
| [`ChatUrlSummaryE2ETests`](../../tests/OpenClawNet.PlaywrightTests/ChatUrlSummaryE2ETests.cs) | E2E Playwright | Tool-injected URL marker (`elbruno.com`) must appear in final browser-rendered response (IMP-6) |

---

## Anti-Patterns

| Anti-pattern | Why it fails | Fix |
|---|---|---|
| Only testing that no exception is thrown | Data loss is silent — no exception occurs | Assert exact content match |
| Lifecycle-only fakes (`wasCalled == true`) | Don't validate what was passed | Assert content of every argument |
| Mocking only the "happy path" with a simple string | Misses the `null` branch (`Result = null`) | Add null/object/empty variants |
| Testing tool output in isolation (not through adapter) | The bug lives at the seam, not inside the tool | Test data through the full conversion pipeline |
| Single assertion on final output only | Doesn't pinpoint which layer dropped the data | Add per-layer assertions (unit → integration → E2E) |

---

## Summary

**Silent data loss at adapter layers is structurally invisible to downstream tests.**

The five-step recipe creates a defence-in-depth stack:

```
Unit round-trip → Type coverage → Asserting fakes → Integration capture → E2E markers
     ↑                ↑                ↑                    ↑                  ↑
  Catches wrong    Catches null     Catches lifecycle   Catches adapter    Catches full
  field read       branch           fakes               drops in situ      pipeline loss
```

When new adapters are added, follow this recipe before the PR is opened.
See `.github/ADAPTER_REVIEW_CHECKLIST.md` for the gate checklist used by reviewers.

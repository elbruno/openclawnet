# Test Gap Analysis: Tool Output Silent Loss (markdown_convert / FunctionResultContent)

**Date:** 2026-05-23  
**Issue Ref:** elbruno/openclawnet-plan#115  
**Bug Summary:** `markdown_convert` generated 17,435+ characters of markdown that were silently discarded before reaching the LLM for the second turn.  
**Root Cause Fix:** `ModelClientChatClientAdapter.ToOpenClawMessage()` (lines 135–140) now extracts `FunctionResultContent.Result` and maps it to `OCChatMessage.Content`.

---

## Executive Summary

The silent tool-output loss was a **data-conversion bug at an internal adapter boundary** — a seam that every existing test layer treated as a black box. No test broke because:

1. **Unit tests for `ModelClientChatClientAdapter` were added as a regression fix after the bug was found**, not before. Prior to the fix there were no unit tests asserting that `FunctionResultContent` data survived the round-trip.
2. **Integration / E2E tests verified events and lifecycle** (`tool_start`, `tool_complete`, final response non-empty) but never inspected what the model *received* on the second turn, so silent loss was invisible to them.
3. **Live E2E tests (Playwright / job-runner)** check whether the LLM's final answer looks plausible, which is too coarse to catch a case where the LLM simply responded without the tool's data — an empty tool result still yields a valid (if low-quality) assistant message.
4. **The bug pre-dates the FunctionResultContent regression tests** that now exist in `ModelClientChatClientAdapterTests.cs` (`PropagatesFunctionResultContent_StringResult`, `_ObjectResult`). Those tests were written as part of the same commit that fixed the bug — they are a regression guard, not a preventive net.

---

## Test Gap Inventory

### GAP-1 — No pre-fix unit test for `FunctionResultContent` in the adapter  
**Severity: Critical**  
**File:** `tests/OpenClawNet.UnitTests/Agent/ModelClientChatClientAdapterTests.cs`  
**Detail:** Before the bug fix, `ToOpenClawMessage_MapsRoles_Correctly` and `ToOpenClawMessage_ExtractsFunctionCallContent` tested role mapping and tool-call extraction. There was **no test** asserting that a `ChatRole.Tool` message with a `FunctionResultContent` payload produced a non-empty `OCChatMessage.Content`. The two tests added in the fix PR (`PropagatesFunctionResultContent_StringResult` and `_ObjectResult`) are the first to cover this path.  
**Why it was missed:** The test file covered the "outbound" direction (LLM → MEAI tool call) but not the "return" direction (tool result → MEAI → IModelClient). The asymmetry was not obvious without tracing the full round-trip.

---

### GAP-2 — `ChatHubToolCallStreamTests` verifies lifecycle events, not content fidelity  
**Severity: High**  
**File:** `tests/OpenClawNet.IntegrationTests/ChatHubToolCallStreamTests.cs`  
**Detail:** Five tests confirm that `tool_start`, `tool_complete`, and `complete` events appear in the correct order. `FakeToolCallingModelClient` emits one tool call and then a second chunk with `"Here are the files."` — but the second call is hardcoded; it does **not** use the tool result it "received". No assertion checks that the `ChatRequest` passed to the model on the second turn contains the tool's output. Silent data loss is structurally invisible: the stream still emits `tool_complete` and eventually `complete`.  
**Why it was missed:** Tests were written to validate the streaming event protocol, not to validate message content propagation.

---

### GAP-3 — `FakeToolCallingModelClient` ignores the second-turn messages  
**Severity: High**  
**File:** `tests/OpenClawNet.IntegrationTests/GatewayWebAppFactory.cs`  
**Detail:** `FakeToolCallingModelClient.StreamAsync` increments a counter and returns a hardcoded final answer on the second call, regardless of what is in `request.Messages`. Because the fake never inspects `request.Messages`, a bug that erases tool results from those messages produces identical test output. The same pattern exists in `FakeModelClientWithToolCall` in `AgentRuntimeStreamTests.cs` (line 444–457).  
**Why it was missed:** Fakes are typically written for the minimum behavior needed to run the test. Capturing and asserting on the inbound `ChatRequest` on the second turn was not considered a test requirement.

---

### GAP-4 — `LiveMarkItDownToolE2ETests` relies on LLM output heuristics  
**Severity: Medium**  
**File:** `tests/OpenClawNet.IntegrationTests/Jobs/LiveMarkItDownToolE2ETests.cs`  
**Detail:** The test asks a live model to "convert example.com to Markdown and return it verbatim", then checks whether the output contains `# Source:`, `Example Domain`, or any `# ` heading. Because a real LLM can respond with a plausible-looking answer even when the tool result was empty ("I was unable to retrieve the page content"), such a response would satisfy `looksLikeMarkdown.Should().BeTrue()` only marginally — but a well-written model response saying "Here is the content: [blank]" could still pass the length check or contain one of the wildcard strings.  
**Why it was missed:** Live tests are inherently non-deterministic. The fix for the regression was to verify the *job didn't fail*, not to assert a minimum content length or assert the specific marker injected by the tool.

---

### GAP-5 — `MarkItDownToolTests` tests parameter validation only, not tool output  
**Severity: Medium**  
**File:** `tests/OpenClawNet.UnitTests/Tools/MarkItDownToolTests.cs`  
**Detail:** The three existing tests cover `save_to_file` parameter parsing and `IStorageDirectoryProvider` interface wiring. There is no test that calls `MarkItDownTool.ExecuteAsync` with a stub HTTP service and verifies the returned `ToolResult.Content`. Without knowing what the tool produces, it is impossible to assert that the content survives the adapter round-trip.  
**Why it was missed:** The tool was deemed "tested by integration tests" and the unit tests were reduced to structural/parameter checks.

---

### GAP-6 — No dedicated adapter round-trip integration test  
**Severity: Medium**  
**Detail:** There is no test that wires `ModelClientChatClientAdapter` to a real `IModelClient` mock with a multi-turn conversation (user → tool call → tool result → final answer) and asserts that `request.Messages[n].Content` on the second turn contains the tool output. The gap exists at every layer: unit, integration, E2E.  
**Why it was missed:** The adapter is `internal sealed`; it is not visible to integration tests that target the HTTP surface. The missing seam between "MEAI message list going into CompleteAsync/StreamAsync" and "what the real model sees" is never surfaced.

---

### GAP-7 — Playwright / `ChatUrlSummaryE2ETests` uses fuzzy content heuristics  
**Severity: Low**  
**File:** `tests/OpenClawNet.PlaywrightTests/ChatUrlSummaryE2ETests.cs`  
**Detail:** The test asserts the response contains one of: "bruno", "capuano", "microsoft", "azure", "ai", "blog", "developer", OR is longer than 100 characters. A model that hallucinates a plausible-sounding response about a blog about AI satisfies all these checks without the tool output ever having been used.  
**Why it was missed:** Browser-level E2E tests are designed for UX correctness, not data-fidelity assertions.

---

## Root Cause Analysis: Architecture Issues That Enabled the Gap

### 1. The adapter is a one-way translation surface with no observability

`ModelClientChatClientAdapter` sits between Microsoft's agent framework (MEAI) and OpenClawNet's `IModelClient`. It is `internal sealed` and tested only via `InternalsVisibleTo`. Because it is not exposed through any interface other than `IChatClient`, no integration test can directly inspect what it produces. Bugs at this seam are only detectable if either:
- the unit tests cover the exact content path, or  
- the LLM's final answer is semantically validated against the tool output.

Neither condition held before the fix.

### 2. The message round-trip crosses two abstraction boundaries without a contract test

The MEAI framework stores tool results in `FunctionResultContent.Result` (an `object?`). The OpenClawNet model layer stores them in `ChatMessage.Content` (a `string`). The conversion is lossy if `FunctionResultContent.Result` is not explicitly handled. There is no "wire-compatibility" or "contract" test asserting that all MEAI message content types survive the translation.

### 3. Fake model clients are stateless across turns

Both `FakeToolCallingModelClient` and `FakeModelClientWithToolCall` track call count to differentiate turns but never validate the message list they receive. This makes them useless as detectors of message corruption.

### 4. E2E assertions are biased toward "did it work" not "did the data flow correctly"

Across all test layers, the typical assertion pattern is:
- Did the pipeline complete without exceptions?
- Did the LLM produce a non-empty response?
- Does the response contain one of a list of expected keywords?

None of these checks can detect silent data loss if the LLM still produces a reasonable-looking output without the dropped data.

---

## Concrete Improvements

### IMP-1 — Adapter contract test: full multi-turn round-trip
**Target file:** `tests/OpenClawNet.UnitTests/Agent/ModelClientChatClientAdapterTests.cs`  
**Test to add:**
```csharp
[Fact]
public async Task GetResponseAsync_WithToolRoundTrip_ToolResultContentReachesSecondTurn()
{
    // Arrange: capture the ChatRequest that arrives on the second call
    int callCount = 0;
    ChatRequest? secondTurnRequest = null;
    var modelClient = new Mock<IModelClient>();
    modelClient
        .Setup(m => m.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
        .Callback<ChatRequest, CancellationToken>((req, _) =>
        {
            if (++callCount == 2) secondTurnRequest = req;
        })
        .ReturnsAsync(new OCChatResponse { Content = "Done.", Role = ChatMessageRole.Assistant, Model = "test" });

    var adapter = new ModelClientChatClientAdapter(modelClient.Object);

    // First turn: user asks, model responds with tool call
    var toolCallMsg = new MEAIChatMessage(ChatRole.Assistant, [
        new FunctionCallContent("call_1", "markdown_convert", new Dictionary<string, object?> { ["url"] = "https://example.com" })
    ]);

    // Tool result: simulate 17 KB of markdown
    var bigMarkdown = "# Source: https://example.com\n\n" + new string('x', 17_000);
    var toolResultMsg = new MEAIChatMessage(ChatRole.Tool, [
        new FunctionResultContent("call_1", bigMarkdown)
    ]);

    var messages = new List<MEAIChatMessage>
    {
        new(ChatRole.User, "Summarize example.com"),
        toolCallMsg,
        toolResultMsg
    };

    // Act
    await adapter.GetResponseAsync(messages);

    // Assert: the second-turn request contains the tool output
    secondTurnRequest.Should().NotBeNull("adapter must call model");
    var toolMsg = secondTurnRequest!.Messages.FirstOrDefault(m => m.Role == ChatMessageRole.Tool);
    toolMsg.Should().NotBeNull("tool result message must be in the second-turn request");
    toolMsg!.Content.Should().Contain("# Source:", "tool output markdown must survive the round-trip");
    toolMsg.Content.Should().HaveLength(bigMarkdown.Length, "no characters should be lost in translation");
}
```
**Value:** Would have caught the bug before the fix was applied.

---

### IMP-2 — Fake model client that validates second-turn message content
**Target file:** `tests/OpenClawNet.IntegrationTests/GatewayWebAppFactory.cs`  
**Change:** Add `FakeAssertingToolCallingModelClient` that on the second call asserts `request.Messages` contains a Tool-role message with non-empty content:
```csharp
internal sealed class FakeAssertingToolCallingModelClient : IModelClient
{
    private int _callCount;
    public string ProviderName => "fake-asserting";

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        if (++_callCount > 1)
        {
            var toolMsg = request.Messages.FirstOrDefault(m => m.Role == ChatMessageRole.Tool);
            if (toolMsg is null || string.IsNullOrWhiteSpace(toolMsg.Content))
                throw new InvalidOperationException(
                    $"[FakeAssertingToolCallingModelClient] Second-turn ChatRequest is missing tool result content. " +
                    $"Messages: {string.Join(", ", request.Messages.Select(m => $"{m.Role}:{m.Content?.Length ?? 0}ch"))}");
        }
        return Task.FromResult(new ChatResponse { Content = "Done.", Role = ChatMessageRole.Assistant, Model = "test" });
    }
    // ... streaming variant similarly
}
```
**Value:** Any integration test using this factory will fail-fast if the adapter drops tool content.

---

### IMP-3 — `AgentRuntimeStreamTests`: assert second-turn request carries tool content
**Target file:** `tests/OpenClawNet.UnitTests/Agent/AgentRuntimeStreamTests.cs`  
**Test to add:** A new test using `FakeModelClientWithToolCall` but with a capturing variant that records the `ChatRequest` on the second invocation, then asserts `request.Messages` includes a Tool-role entry with content matching the executor's return value.  
**Value:** Closes the gap between the runtime-level test and the adapter-level conversion.

---

### IMP-4 — `MarkItDownToolTests`: test actual tool output
**Target file:** `tests/OpenClawNet.UnitTests/Tools/MarkItDownToolTests.cs`  
**Change:** Add a test using `WireMock.Net` or `HttpMessageHandler` stub that returns a simple HTML page; invoke `MarkItDownTool.ExecuteAsync` and assert `result.Content.Length > 0` and `result.Content.Contains("# Source:")`.  
**Value:** Verifies the tool itself produces non-empty content before it even reaches the adapter.

---

### IMP-5 — `LiveMarkItDownToolE2ETests`: assert minimum output length
**Target file:** `tests/OpenClawNet.IntegrationTests/Jobs/LiveMarkItDownToolE2ETests.cs`  
**Change:** After the `looksLikeMarkdown` check, add:
```csharp
output.Length.Should().BeGreaterThan(200,
    "markdown_convert on example.com should produce at least 200 characters of output, " +
    "not a brief 'I was unable to retrieve...' message");
```
**Value:** Would have caught the symptom even without knowing the root cause.

---

### IMP-6 — Playwright: assert response references page-specific content
**Target file:** `tests/OpenClawNet.PlaywrightTests/ChatUrlSummaryE2ETests.cs`  
**Change:** Replace the fuzzy multi-OR check with a stricter assertion that the response contains content uniquely derived from the fetched page (e.g., the tool-injected `# Source: https://elbruno.com` marker, or a title that can only come from the live page).  
**Value:** Rules out hallucinated plausible responses that bypass the check.

---

## Prevention Strategy

### Strategy 1: Adapter contract suite
For every new `IModelClient` adapter or MEAI-to-OpenClawNet conversion layer, require:
- A test covering each `AIContent` subtype that can appear in tool-role messages (`FunctionResultContent` with string result, with object result, with null result).
- A round-trip test that feeds a multi-turn conversation with tool results and verifies the downstream `ChatRequest` contains those results verbatim.

### Strategy 2: Asserting fake model clients
Retire "passive" fakes that ignore `request.Messages`. Introduce `FakeAssertingModelClient` as a shared test fixture (in `OpenClawNet.Tests.Fixtures`) that throws when tool-role messages have empty content on the second turn. Use this by default in integration tests that exercise the tool-call pipeline.

### Strategy 3: Content-length floor assertions
Any live or integration test that verifies "the tool produced output" should include a minimum content length assertion. Heuristic keyword checks are insufficient because models can hallucinate plausible responses.

### Strategy 4: Message-boundary logging in production
Instrument the adapter's `ToOpenClawMessage` method to emit a structured log entry at `Debug` level including the role, content length, and whether `FunctionResultContent` was found. This makes the bug category observable in production before users report it.

### Strategy 5: New adapter checklist
When a new MEAI abstraction layer is introduced, add to the PR checklist:
- [ ] All `AIContent` subtypes handled in `ToOpenClawMessage` / `ToMEAIMessage`
- [ ] Unit tests cover string result, object result, and null result for `FunctionResultContent`
- [ ] Integration test uses asserting fake to verify second-turn content fidelity

---

## Summary Table

| Gap ID | Layer | Severity | Test That Would Have Caught It |
|--------|-------|----------|-------------------------------|
| GAP-1 | Unit (adapter) | Critical | `ToOpenClawMessage_PropagatesFunctionResultContent_StringResult` (now exists post-fix) |
| GAP-2 | Integration (SignalR) | High | Asserting fake model client on second turn |
| GAP-3 | Fake infrastructure | High | `FakeAssertingToolCallingModelClient` |
| GAP-4 | Live E2E (job) | Medium | Minimum output length assertion |
| GAP-5 | Unit (tool) | Medium | `ExecuteAsync` with stub HTTP |
| GAP-6 | Integration (adapter) | Medium | Multi-turn round-trip test (IMP-1) |
| GAP-7 | Playwright E2E | Low | Page-specific content assertion |

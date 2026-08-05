# Decision: AspireHostFixture Extended with Full Feature Parity

**Date:** 2026-05-25  
**Author:** Irving (Backend Dev)  
**Status:** Active  
**Scope:** `tests/OpenClawNet.PlaywrightTests/AspireHostFixture.cs`, `AspireHostPlaywrightTestBase.cs`

## Decision

`AspireHostFixture` has been extended to reach full feature parity with `AppHostFixture` for all E2E test capabilities:

1. **Ollama model probing** — `IsToolCapableModelAvailable`, `ToolCapableTestModel`, `ToolCapableModelSkipReason`, `ProbeOllamaToolCallCompatibilityAsync()` (with per-model cache)
2. **Azure OpenAI probing** — `IsAzureOpenAIAvailable`, `AzureOpenAIEndpoint`, `AzureOpenAIApiKey`, `AzureOpenAIDeployment`, `IsAnyToolCapableModelAvailable`, `AnyToolCapableModelSkipReason`
3. **Scheduler client** — `CreateSchedulerHttpClient()` (mirrors `CreateGatewayHttpClient()`)
4. **Base class helpers** — `LogStepAsync()` and `WaitForWithTicksAsync()` added to `AspireHostPlaywrightTestBase`

## Rationale

Wave 3c required these capabilities for the 12 complex test files that use Ollama/Azure model skip gates and LLM-wait helpers. Rather than keeping two feature sets diverged, we bring `AspireHostFixture` to full parity so the `AppHostFixture` can be retired in Wave 3d.

## Consequence

- All 20 remaining `[Collection("AppHost")]` tests now use `[Collection("AspireHost")]`
- `AppHostFixture` and `PlaywrightTestBase` are no longer referenced by any active test (only by the `AppHostCollection` definition class)
- Wave 3d action: evaluate safe retirement of `AppHostFixture` / `PlaywrightTestBase` / `AppHostCollection`

## Validation

- Build: ✅ 0 errors
- Test run: ✅ 124 tests enumerated, 124 skipped (Playwright node blocker — expected in this environment), 0 failures

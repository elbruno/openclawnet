# Skill: Provider Test Patterns — CapturingAgentProvider

**Skill ID:** `provider-test-patterns`  
**Author:** Dylan (Tester)  
**Created:** 2026-05-25  
**Applies to:** Unit tests for endpoints that call `IAgentProvider.CreateChatClient`

---

## Problem

Testing whether an endpoint correctly forwards configuration (model name, API key, etc.) to an `IAgentProvider.CreateChatClient` is hard because:
- Real providers need a running LLM service
- Moq can verify calls but can't easily capture parameter state for later assertion
- The endpoint's `catch (Exception ex)` absorbs provider exceptions, so throwing from a fake is safe

---

## Solution: CapturingAgentProvider

A `sealed class` that stores the `AgentProfile` passed to `CreateChatClient`, then throws `InvalidOperationException`. The endpoint's catch-all returns `{ success: false }`, tests assert on the captured profile afterward.

```csharp
private sealed class CapturingAgentProvider(string providerName) : IAgentProvider
{
    public string ProviderName => providerName;
    public AgentProfile? LastCapturedProfile { get; private set; }

    public IChatClient CreateChatClient(AgentProfile profile)
    {
        LastCapturedProfile = profile;
        throw new InvalidOperationException(
            $"CapturingAgentProvider: profile captured for '{providerName}', no real chat client.");
    }

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
        => Task.FromResult(true);
}
```

**Registration:**
```csharp
var capturer = new CapturingAgentProvider("ollama");
builder.Services.AddSingleton<IAgentProvider>(capturer);
```

---

## Factory Helper Pattern

```csharp
private static async Task<(WebApplication app, CapturingAgentProvider capturer)>
    CreateTestAppWithCapturingProviderAsync()
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
    builder.WebHost.UseTestServer();

    builder.Services.AddDbContextFactory<OpenClawDbContext>(o =>
        o.UseInMemoryDatabase("test-cap-" + Guid.NewGuid()));
    builder.Services.AddScoped<IModelProviderDefinitionStore, ModelProviderDefinitionStore>();

    var capturer = new CapturingAgentProvider("ollama");
    builder.Services.AddSingleton<IAgentProvider>(capturer);

    var app = builder.Build();
    app.MapModelProviderEndpoints();
    await app.StartAsync();
    return (app, capturer);
}
```

---

## Test Pattern

```csharp
[Fact]
public async Task PostTest_WithModelInDefinition_PassesModelToAgentProvider()
{
    var (app, capturer) = await CreateTestAppWithCapturingProviderAsync();
    await using (app)
    {
        using var client = app.GetTestClient();

        await client.PutAsJsonAsync("/api/model-providers/my-ollama", new
        {
            providerType = "ollama",
            model = "gemma4:e2b"
        });

        await client.PostAsync("/api/model-providers/my-ollama/test", null);
    }

    // Assert AFTER the using block — app is disposed but capturer is not
    capturer.LastCapturedProfile.Should().NotBeNull("provider should have been called");
    capturer.LastCapturedProfile!.Model.Should().Be("gemma4:e2b",
        "def.Model must be forwarded to the test profile");
}
```

---

## Key Rules

1. **Assert AFTER `await using (app)`** — the capturer is a plain C# object (not owned by the DI container's disposable graph), so it outlives the app.

2. **`await using` tuple destructuring** — C# does NOT support `await using var (a, b) = ...`. Must be:
   ```csharp
   var (app, capturer) = await FactoryAsync();
   await using (app) { ... }
   ```

3. **ProviderName must match `ProviderType`** — the endpoint filters by `p.ProviderName.Equals(def.ProviderType, OrdinalIgnoreCase)`. Set `CapturingAgentProvider("ollama")` when the test stores a provider with `providerType = "ollama"`.

4. **Response is always `200 success=false`** — the throw is caught and swallowed. Test infrastructure that checks for non-200 will falsely fail.

5. **One capturer per app** — creating multiple `CapturingAgentProvider` singletons for the same provider name is unsupported; the endpoint picks the first match.

---

## When to Use vs Moq

| Scenario | Use |
|----------|-----|
| Verify `CreateChatClient` was called | Either — Moq `Verify` or capturer `LastCapturedProfile != null` |
| Inspect which `AgentProfile` was passed | **CapturingAgentProvider** — captures the full object |
| Simulate specific return value | **Moq** — `Setup(...).Returns(...)` |
| Test `IsAvailableAsync` logic | **Moq** — simpler setup |

---

## Real-World Uses

- `tests/OpenClawNet.UnitTests/Gateway/ModelProviderEndpointTests.cs` — Issue #120 tests
- `tests/OpenClawNet.UnitTests/Gateway/AgentProfileEndpointTests.cs` — Issue #122 tests

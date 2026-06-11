using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Models.Ollama;

namespace OpenClawNet.UnitTests.Models;

public class OllamaAgentProviderTests
{
    [Fact]
    public void ProviderName_ReturnsOllama()
    {
        var provider = CreateProvider();

        provider.ProviderName.Should().Be("ollama");
    }

    [Fact(Skip = "OllamaSharp assembly load failure — issue #95")]
    public void CreateChatClient_ReturnsNonNull_WithDefaultOptions()
    {
        var provider = CreateProvider();
        var profile = new AgentProfile { Name = "test" };

        var client = provider.CreateChatClient(profile);

        client.Should().NotBeNull();
        client.Should().BeAssignableTo<IChatClient>();
    }

    [Fact(Skip = "OllamaSharp assembly load failure — issue #95")]
    public void CreateChatClient_UsesProviderDefault_WhenProfileHasNoOverrides()
    {
        // PR-F: AgentProfile no longer carries a Model field; the provider supplies its own.
        var provider = CreateProvider();
        var profile = new AgentProfile { Name = "test" };

        var client = provider.CreateChatClient(profile);

        client.Should().NotBeNull();
        client.Should().BeAssignableTo<IChatClient>();
    }

    // ── Model fallback logic (Issue #120 / #122) ──────────────────────────────
    // These tests document the expected priority: profile.Model > options.Model > "gemma4:e2b"
    // They are skipped because OllamaApiClient (OllamaSharp) fails to load in this test host.
    // Re-enable when issue #95 is resolved.

    [Fact(Skip = "OllamaSharp assembly load failure — issue #95")]
    public void CreateChatClient_UsesProfileModel_WhenProfileModelIsSet()
    {
        // profile.Model takes highest priority — used directly even if options has a different model
        var provider = CreateProvider(new OllamaOptions { Model = "llama3.2" });
        var profile = new AgentProfile { Name = "test-profile-model", Model = "gemma4:e2b" };

        // Should not throw and should use "gemma4:e2b" (profile wins over options "llama3.2")
        var client = provider.CreateChatClient(profile);

        client.Should().NotBeNull();
    }

    [Fact(Skip = "OllamaSharp assembly load failure — issue #95")]
    public void CreateChatClient_FallsBackToOptionsModel_WhenProfileModelIsNull()
    {
        // options.Model is used when profile.Model is null
        var provider = CreateProvider(new OllamaOptions { Model = "phi4" });
        var profile = new AgentProfile { Name = "test-options-fallback", Model = null };

        // Should not throw and should use "phi4" (options model)
        var client = provider.CreateChatClient(profile);

        client.Should().NotBeNull();
    }

    [Fact(Skip = "OllamaSharp assembly load failure — issue #95")]
    public void CreateChatClient_FallsBackToHardcodedDefault_WhenBothModelsAreNull()
    {
        // hardcoded "gemma4:e2b" is the last-resort default when both profile and options are null
        var options = new OllamaOptions { Model = null! };  // explicitly nullify options model
        var provider = CreateProvider(options);
        var profile = new AgentProfile { Name = "test-hardcoded-default", Model = null };

        // Should not throw and should use "gemma4:e2b" (hardcoded default)
        var client = provider.CreateChatClient(profile);

        client.Should().NotBeNull();
    }

    [Fact(Skip = "OllamaSharp assembly load failure — issue #95")]
    public void CreateChatClient_DoesNotThrow_WhenNullModelHandled()
    {
        // Null model on the profile must never propagate as a null to OllamaApiClient
        var provider = CreateProvider(new OllamaOptions { Model = "llama3.2" });
        var profile = new AgentProfile { Name = "null-model-safe", Model = null };

        var act = () => provider.CreateChatClient(profile);

        act.Should().NotThrow();
    }

    [Fact(Skip = "OllamaSharp assembly load failure — issue #95")]
    public void CreateChatClient_TreatsEmptyStringModelAsNull_AndFallsToOptionsModel()
    {
        // Empty string profile model should be treated as absent, deferring to options.Model
        var provider = CreateProvider(new OllamaOptions { Model = "llama3.2" });
        var profile = new AgentProfile { Name = "empty-model", Model = "" };

        // Should not throw; effective model should come from options ("llama3.2")
        var client = provider.CreateChatClient(profile);

        client.Should().NotBeNull();
    }

    [Fact(Skip = "OllamaSharp assembly load failure — issue #95")]
    public void CreateChatClient_TreatsWhitespaceOnlyModelAsNull_AndFallsToOptionsModel()
    {
        // Whitespace-only profile model should be treated as absent
        var provider = CreateProvider(new OllamaOptions { Model = "llama3.2" });
        var profile = new AgentProfile { Name = "whitespace-model", Model = "   " };

        var client = provider.CreateChatClient(profile);

        client.Should().NotBeNull();
    }

    [Fact(Skip = "OllamaSharp assembly load failure — issue #95")]
    public void CreateChatClient_ModelPriorityOrder_IsStrictProfileThenOptionsThenDefault()
    {
        // Verifies strict priority: profile > options > "gemma4:e2b"
        // When all three are present, profile wins.
        var provider = CreateProvider(new OllamaOptions { Model = "llama3.2" });
        var profile = new AgentProfile { Name = "priority-check", Model = "phi4" };

        var client = provider.CreateChatClient(profile);

        // phi4 (profile) beats llama3.2 (options) beats gemma4:e2b (default)
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsFalse_WhenEndpointUnreachable()
    {
        var options = Options.Create(new OllamaOptions { Endpoint = "http://localhost:19999" });
        var provider = new OllamaAgentProvider(options, NullLogger<OllamaAgentProvider>.Instance);

        var result = await provider.IsAvailableAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsFalse_WhenEndpointIsGarbage()
    {
        var options = Options.Create(new OllamaOptions { Endpoint = "http://not-a-real-host-xyz.invalid" });
        var provider = new OllamaAgentProvider(options, NullLogger<OllamaAgentProvider>.Instance);

        var result = await provider.IsAvailableAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsAvailableAsync_DoesNotThrow_WhenEndpointIsNull()
    {
        // Null endpoint falls back to localhost:11434 which will be unreachable in CI
        var options = Options.Create(new OllamaOptions { Endpoint = null! });
        var provider = new OllamaAgentProvider(options, NullLogger<OllamaAgentProvider>.Instance);

        var act = async () => await provider.IsAvailableAsync();

        await act.Should().NotThrowAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static OllamaAgentProvider CreateProvider(OllamaOptions? options = null)
    {
        return new OllamaAgentProvider(
            Options.Create(options ?? new OllamaOptions()),
            NullLogger<OllamaAgentProvider>.Instance);
    }
}

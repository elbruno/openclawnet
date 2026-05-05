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

    [Fact]
    public async Task IsAvailableAsync_ReturnsFalse_WhenEndpointUnreachable()
    {
        var options = Options.Create(new OllamaOptions { Endpoint = "http://localhost:19999" });
        var provider = new OllamaAgentProvider(options, NullLogger<OllamaAgentProvider>.Instance);

        var result = await provider.IsAvailableAsync();

        result.Should().BeFalse();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static OllamaAgentProvider CreateProvider(OllamaOptions? options = null)
    {
        return new OllamaAgentProvider(
            Options.Create(options ?? new OllamaOptions()),
            NullLogger<OllamaAgentProvider>.Instance);
    }
}

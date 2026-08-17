using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Models.Foundry;

namespace OpenClawNet.UnitTests.Models;

/// <summary>
/// Unit tests for <see cref="FoundryAgentProvider"/> — Issue #230 regression suite.
///
/// Regression scenario: users configure Foundry endpoint + API key via the Model Providers UI
/// but Test Connection fails with "Foundry is not configured. Set Endpoint and ApiKey."
/// Root cause: profile.Endpoint and profile.ApiKey (from the stored definition) must override
/// the (empty) DI options; if those fields are null the guard throws.
/// </summary>
public sealed class FoundryAgentProviderTests
{
    // ── ProviderName ──────────────────────────────────────────────────────────

    [Fact]
    public void ProviderName_IsFoundry()
    {
        var provider = CreateProvider(new FoundryOptions());
        provider.ProviderName.Should().Be("foundry");
    }

    // ── IsAvailableAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task IsAvailableAsync_ReturnsFalse_WhenEndpointEmpty()
    {
        var provider = CreateProvider(new FoundryOptions { ApiKey = "key" });

        var result = await provider.IsAvailableAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsFalse_WhenApiKeyEmpty()
    {
        var provider = CreateProvider(new FoundryOptions { Endpoint = "http://localhost:11434" });

        var result = await provider.IsAvailableAsync();

        result.Should().BeFalse();
    }

    // ── CreateChatClient with profile overrides (Issue #230) ─────────────────

    [Fact]
    public void CreateChatClient_UsesProfileEndpointAndApiKey_WhenOptsAreEmpty()
    {
        // Issue #230: when DI options are empty (configured via UI/DB, not appsettings),
        // profile.Endpoint and profile.ApiKey must be used so CreateChatClient succeeds.
        var provider = CreateProvider(new FoundryOptions()); // empty opts
        var profile = new AgentProfile
        {
            Name = "issue-230-foundry",
            Endpoint = "https://ai-foundry.services.ai.azure.com/api/projects/proj-default",
            ApiKey = "synthetic-foundry-key-abc123"
        };

        // Should NOT throw "Foundry is not configured" — profile fields must be honoured.
        var act = () => provider.CreateChatClient(profile);

        act.Should().NotThrow<InvalidOperationException>(
            "profile.Endpoint and profile.ApiKey override empty DI opts (Issue #230)");
    }

    [Fact]
    public void CreateChatClient_UsesProfileApiKey_WhenOnlyOptsApiKeyIsEmpty()
    {
        // Opts have the endpoint, profile supplies only the API key.
        var provider = CreateProvider(new FoundryOptions
        {
            Endpoint = "https://ai-foundry.services.ai.azure.com/api/projects/proj-default"
        });
        var profile = new AgentProfile
        {
            Name = "issue-230-foundry-key-only",
            ApiKey = "profile-only-key-xyz"
        };

        var act = () => provider.CreateChatClient(profile);

        act.Should().NotThrow<InvalidOperationException>(
            "profile.ApiKey must override empty opts.ApiKey (Issue #230)");
    }

    [Fact]
    public void CreateChatClient_UsesProfileEndpoint_WhenOnlyOptsEndpointIsEmpty()
    {
        // Opts have the API key, profile supplies only the endpoint.
        var provider = CreateProvider(new FoundryOptions { ApiKey = "opts-key" });
        var profile = new AgentProfile
        {
            Name = "issue-230-foundry-ep-only",
            Endpoint = "https://ai-foundry.services.ai.azure.com/api/projects/proj-default"
        };

        var act = () => provider.CreateChatClient(profile);

        act.Should().NotThrow<InvalidOperationException>(
            "profile.Endpoint must override empty opts.Endpoint (Issue #230)");
    }

    [Fact]
    public void CreateChatClient_Throws_WhenNeitherProfileNorOptsHaveEndpoint()
    {
        var provider = CreateProvider(new FoundryOptions()); // empty opts
        var profile = new AgentProfile
        {
            Name = "missing-endpoint",
            ApiKey = "some-key"
            // No Endpoint
        };

        var act = () => provider.CreateChatClient(profile);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Foundry*not configured*");
    }

    [Fact]
    public void CreateChatClient_Throws_WhenNeitherProfileNorOptsHaveApiKey()
    {
        var provider = CreateProvider(new FoundryOptions()); // empty opts
        var profile = new AgentProfile
        {
            Name = "missing-apikey",
            Endpoint = "https://ai-foundry.services.ai.azure.com/api/projects/proj-default"
            // No ApiKey
        };

        var act = () => provider.CreateChatClient(profile);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Foundry*not configured*");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static FoundryAgentProvider CreateProvider(FoundryOptions options)
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
                   .Returns(new HttpClient());

        return new FoundryAgentProvider(
            Options.Create(options),
            mockFactory.Object,
            NullLoggerFactory.Instance,
            NullLogger<FoundryAgentProvider>.Instance);
    }
}

using FluentAssertions;
using GitHub.Copilot;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Models.GitHubCopilot;

namespace OpenClawNet.UnitTests.Models;

public class GitHubCopilotAgentProviderTests
{
    private static GitHubCopilotAgentProvider CreateProvider(GitHubCopilotOptions? opts = null)
    {
        var options = Options.Create(opts ?? new GitHubCopilotOptions());
        return new GitHubCopilotAgentProvider(options, NullLogger<GitHubCopilotAgentProvider>.Instance);
    }

    [Fact]
    public void ProviderName_ReturnsGitHubCopilot()
    {
        var provider = CreateProvider();
        provider.ProviderName.Should().Be("github-copilot");
    }

    [Fact]
    public void CreateChatClient_ReturnsNonNull()
    {
        var provider = CreateProvider();
        var profile = new AgentProfile { Name = "test" };

        var client = provider.CreateChatClient(profile);

        client.Should().NotBeNull();
    }

    [Fact]
    public void CreateChatClient_UsesDefaultModel_WhenProfileHasNone()
    {
        var provider = CreateProvider(new GitHubCopilotOptions { Model = "gpt-5-mini" });
        var profile = new AgentProfile { Name = "test" };

        var client = provider.CreateChatClient(profile);

        // The client wraps the model — we verify it was created without throwing
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsTrueWhenTokenConfigured()
    {
        var provider = CreateProvider(new GitHubCopilotOptions { GitHubToken = "ghp_test123" });

        var available = await provider.IsAvailableAsync();

        available.Should().BeTrue();
    }

    [Fact]
    public async Task IsAvailableAsync_DoesNotThrow_WhenNoToken()
    {
        // Without token and without gh CLI, should not throw
        var provider = CreateProvider(new GitHubCopilotOptions { GitHubToken = null });

        var act = async () => await provider.IsAvailableAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        var provider = CreateProvider();

        // Should not throw on multiple dispose calls
        await provider.DisposeAsync();
        await provider.DisposeAsync();
    }

    [Fact]
    public void BuildClientOptions_SetsStdioConnection_WhenCliPathProvided()
    {
        const string cliPath = @"C:\tools\copilot.exe";
        var options = new GitHubCopilotOptions { CliPath = cliPath };

        var clientOptions = GitHubCopilotAgentProvider.BuildClientOptions(
            options,
            NullLogger<GitHubCopilotAgentProvider>.Instance,
            token: null);

        clientOptions.Connection.Should().BeOfType<StdioRuntimeConnection>();
        ((ChildProcessRuntimeConnection)clientOptions.Connection!).Path.Should().Be(cliPath);
    }

    [Fact]
    public void BuildClientOptions_LeavesConnectionNull_WhenCliPathMissing()
    {
        var options = new GitHubCopilotOptions { CliPath = null };

        var clientOptions = GitHubCopilotAgentProvider.BuildClientOptions(
            options,
            NullLogger<GitHubCopilotAgentProvider>.Instance,
            token: null);

        clientOptions.Connection.Should().BeNull();
    }

    [Fact]
    public void BuildClientOptions_SetsGitHubToken_WhenProvided()
    {
        var options = new GitHubCopilotOptions();

        var clientOptions = GitHubCopilotAgentProvider.BuildClientOptions(
            options,
            NullLogger<GitHubCopilotAgentProvider>.Instance,
            token: "ghp_test_token");

        clientOptions.GitHubToken.Should().Be("ghp_test_token");
    }

    [Fact]
    public void CreateSessionConfig_DisablesManagedSettingsAndInfiniteSessions()
    {
        var config = CopilotChatClient.CreateSessionConfig(
            model: "gpt-5-mini",
            systemMessage: "system",
            streaming: false);

        config.Model.Should().Be("gpt-5-mini");
        config.Streaming.Should().BeNull();
        config.EnableManagedSettings.Should().BeFalse();
        config.OnPermissionRequest.Should().NotBeNull();
        config.InfiniteSessions.Should().NotBeNull();
        config.InfiniteSessions!.Enabled.Should().BeFalse();
        config.SystemMessage.Should().NotBeNull();
        config.SystemMessage!.Content.Should().Be("system");
    }

    [Fact]
    public void CreateSessionConfig_EnablesStreaming_WhenRequested()
    {
        var config = CopilotChatClient.CreateSessionConfig(
            model: "gpt-5-mini",
            systemMessage: null,
            streaming: true);

        config.Streaming.Should().BeTrue();
        config.SystemMessage.Should().BeNull();
    }
}

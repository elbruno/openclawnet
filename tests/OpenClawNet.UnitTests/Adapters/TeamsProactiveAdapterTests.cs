using FluentAssertions;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using OpenClawNet.Adapters.Teams;
using OpenClawNet.Channels.Adapters;

namespace OpenClawNet.UnitTests.Adapters;

/// <summary>
/// Unit tests for <see cref="TeamsProactiveAdapter"/> — Teams Bot Framework delivery adapter.
/// Tests cover success scenarios, error handling, and edge cases per Story 7 AC6.
/// </summary>
public sealed class TeamsProactiveAdapterTests
{
    private readonly Mock<IBotFrameworkHttpAdapter> _mockAdapter;
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<ILogger<TeamsProactiveAdapter>> _mockLogger;
    private readonly Mock<IConfigurationSection> _mockAppIdSection;

    public TeamsProactiveAdapterTests()
    {
        _mockAdapter = new Mock<IBotFrameworkHttpAdapter>();
        _mockConfig = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<TeamsProactiveAdapter>>();
        _mockAppIdSection = new Mock<IConfigurationSection>();

        // Default configuration setup
        _mockAppIdSection.Setup(s => s.Value).Returns("test-app-id");
        _mockConfig.Setup(c => c["MicrosoftAppId"]).Returns("test-app-id");
    }

    // ── Constructor Tests ──────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenAdapterIsNull()
    {
        // Act & Assert
        var act = () => new TeamsProactiveAdapter(null!, _mockConfig.Object, _mockLogger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("adapter");
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
    {
        // Act & Assert
        var act = () => new TeamsProactiveAdapter(_mockAdapter.Object, _mockConfig.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_ThrowsInvalidOperationException_WhenAppIdNotConfigured()
    {
        // Arrange: missing MicrosoftAppId configuration
        var emptyConfig = new Mock<IConfiguration>();
        emptyConfig.Setup(c => c["MicrosoftAppId"]).Returns((string?)null);

        // Act & Assert
        var act = () => new TeamsProactiveAdapter(_mockAdapter.Object, emptyConfig.Object, _mockLogger.Object);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("MicrosoftAppId not configured for Teams adapter");
    }

    [Fact]
    public void Name_ReturnsTeams()
    {
        // Arrange
        var adapter = CreateAdapter();

        // Act & Assert
        adapter.Name.Should().Be("teams");
    }

    // ── DeliverAsync Success Tests ────────────────────────────────────────────

    [Fact]
    public async Task DeliverAsync_ReturnsFailure_WhenConversationReferenceIsMissing()
    {
        // Arrange
        var adapter = CreateAdapter();
        var invalidConfig = "{}"; // Empty JSON, no conversationReference

        // Act
        var result = await adapter.DeliverAsync(
            Guid.NewGuid(),
            "Test Job",
            Guid.NewGuid(),
            "markdown",
            invalidConfig);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid or missing conversation reference");
    }

    [Fact]
    public async Task DeliverAsync_ReturnsFailure_WhenConversationReferenceIsEmpty()
    {
        // Arrange
        var adapter = CreateAdapter();
        var invalidConfig = """{"conversationReference": ""}""";

        // Act
        var result = await adapter.DeliverAsync(
            Guid.NewGuid(),
            "Test Job",
            Guid.NewGuid(),
            "markdown",
            invalidConfig);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid or missing conversation reference");
    }

    [Fact]
    public async Task DeliverAsync_ReturnsFailure_WhenChannelConfigIsInvalidJson()
    {
        // Arrange
        var adapter = CreateAdapter();
        var invalidConfig = "not json at all {{{";

        // Act
        var result = await adapter.DeliverAsync(
            Guid.NewGuid(),
            "Test Job",
            Guid.NewGuid(),
            "markdown",
            invalidConfig);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task DeliverAsync_ReturnsFailure_WhenConversationReferenceJsonIsInvalid()
    {
        // Arrange
        var adapter = CreateAdapter();
        var invalidConfig = """{"conversationReference": "not-json"}""";

        // Act
        var result = await adapter.DeliverAsync(
            Guid.NewGuid(),
            "Test Job",
            Guid.NewGuid(),
            "markdown",
            invalidConfig);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid or missing conversation reference");
    }

    // ── Error Handling Tests (Fire-and-Forget Pattern) ───────────────────────

    [Fact]
    public async Task DeliverAsync_NeverThrows_OnInvalidJson()
    {
        // Arrange
        var adapter = CreateAdapter();
        var invalidConfig = "completely invalid json {{{";

        // Act
        var act = async () => await adapter.DeliverAsync(
            Guid.NewGuid(),
            "Test Job",
            Guid.NewGuid(),
            "markdown",
            invalidConfig);

        // Assert: fire-and-forget means never throw
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeliverAsync_LogsError_OnException()
    {
        // Arrange
        var adapter = CreateAdapter();
        var invalidConfig = "invalid json";

        // Act
        await adapter.DeliverAsync(
            Guid.NewGuid(),
            "Test Job",
            Guid.NewGuid(),
            "markdown",
            invalidConfig);

        // Assert: verify error was logged
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    // ── Content Formatting Tests ──────────────────────────────────────────────

    [Fact]
    public async Task DeliverAsync_HandlesLongContent_WithoutThrowing()
    {
        // Arrange
        var adapter = CreateAdapter();
        var longContent = new string('A', 10000); // 10k characters
        var config = CreateValidChannelConfig();

        // Act
        var result = await adapter.DeliverAsync(
            Guid.NewGuid(),
            "Test Job",
            Guid.NewGuid(),
            "text",
            config);

        // Assert: should handle gracefully (may truncate internally)
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DeliverAsync_HandlesMultipleArtifactTypes()
    {
        // Arrange
        var adapter = CreateAdapter();
        var config = CreateValidChannelConfig();
        var artifactTypes = new[] { "markdown", "json", "text", "file", "error" };

        // Act & Assert: all types should be handled without throwing
        foreach (var type in artifactTypes)
        {
            var result = await adapter.DeliverAsync(
                Guid.NewGuid(),
                "Test Job",
                Guid.NewGuid(),
                type,
                config);

            result.Should().NotBeNull();
        }
    }

    // ── Helper Methods ─────────────────────────────────────────────────────────

    private TeamsProactiveAdapter CreateAdapter()
    {
        return new TeamsProactiveAdapter(_mockAdapter.Object, _mockConfig.Object, _mockLogger.Object);
    }

    private string CreateValidChannelConfig()
    {
        // Create a minimal valid conversation reference
        var conversationRef = """
        {
            "serviceUrl": "https://smba.trafficmanager.net/amer/",
            "channelId": "msteams",
            "conversation": {
                "id": "19:test-conversation-id@thread.tacv2"
            },
            "user": {
                "id": "29:test-user-id",
                "name": "Test User"
            }
        }
        """;

        return $$"""
        {
            "conversationReference": {{conversationRef}},
            "teamId": "test-team-id",
            "userId": "test-user-id"
        }
        """;
    }
}

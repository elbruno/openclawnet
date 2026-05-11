using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.Dashboard;
using Xunit;

namespace OpenClawNet.UnitTests.Tools;

/// <summary>
/// Unit tests for DashboardPublisherTool (S4-4).
/// Validates input parsing, metadata, and error handling with mocked IDashboardPublisher.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Layer", "Unit")]
public sealed class DashboardPublisherToolUnitTests
{
    private static ToolInput Args(string json) => new()
    {
        ToolName = "dashboard_publish",
        RawArguments = json
    };

    [Fact]
    public void Metadata_Has_Correct_Name_And_Description()
    {
        // ARRANGE
        var publisher = Mock.Of<IDashboardPublisher>();
        var tool = new DashboardPublisherTool(publisher, NullLogger<DashboardPublisherTool>.Instance);

        // ACT
        var metadata = tool.Metadata;

        // ASSERT
        Assert.Equal("dashboard_publish", metadata.Name);
        Assert.Contains("repository insights", metadata.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dashboard", metadata.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Metadata_Requires_Approval()
    {
        // ARRANGE
        var publisher = Mock.Of<IDashboardPublisher>();
        var tool = new DashboardPublisherTool(publisher, NullLogger<DashboardPublisherTool>.Instance);

        // ACT
        var metadata = tool.Metadata;

        // ASSERT
        Assert.True(metadata.RequiresApproval, "Publishing to external dashboard should require approval");
    }

    [Fact]
    public void Metadata_Has_Integration_Category()
    {
        // ARRANGE
        var publisher = Mock.Of<IDashboardPublisher>();
        var tool = new DashboardPublisherTool(publisher, NullLogger<DashboardPublisherTool>.Instance);

        // ACT
        var metadata = tool.Metadata;

        // ASSERT
        Assert.Equal("integration", metadata.Category);
    }

    [Fact]
    public void Metadata_Parameter_Schema_Has_Required_Fields()
    {
        // ARRANGE
        var publisher = Mock.Of<IDashboardPublisher>();
        var tool = new DashboardPublisherTool(publisher, NullLogger<DashboardPublisherTool>.Instance);

        // ACT
        var root = tool.Metadata.ParameterSchema.RootElement;
        var required = root.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();

        // ASSERT
        Assert.Contains("title", required);
        Assert.Contains("insights", required);
    }

    [Theory]
    [InlineData("""{ "insights": [{ "repo": "elbruno/openclawnet" }] }""")] // missing title
    [InlineData("""{ "title": "" }""")] // missing insights
    [InlineData("""{ "title": "   ", "insights": [{ "repo": "test/test" }] }""")] // whitespace title
    public async Task ExecuteAsync_Missing_Required_Fields_Returns_Error(string json)
    {
        // ARRANGE
        var publisher = Mock.Of<IDashboardPublisher>();
        var tool = new DashboardPublisherTool(publisher, NullLogger<DashboardPublisherTool>.Instance);

        // ACT
        var result = await tool.ExecuteAsync(Args(json));

        // ASSERT
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_Invalid_Insights_Array_Returns_Error()
    {
        // ARRANGE
        var publisher = Mock.Of<IDashboardPublisher>();
        var tool = new DashboardPublisherTool(publisher, NullLogger<DashboardPublisherTool>.Instance);

        var json = """{ "title": "Test", "insights": [] }"""; // empty array

        // ACT
        var result = await tool.ExecuteAsync(Args(json));

        // ASSERT
        Assert.False(result.Success);
        Assert.Contains("insights", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("empty", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_Invalid_JSON_Returns_Error()
    {
        // ARRANGE
        var publisher = Mock.Of<IDashboardPublisher>();
        var tool = new DashboardPublisherTool(publisher, NullLogger<DashboardPublisherTool>.Instance);

        var json = """{ "title": "Test", "insights": "not-an-array" }""";

        // ACT
        var result = await tool.ExecuteAsync(Args(json));

        // ASSERT
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_Successful_Publish_Returns_Dashboard_URL()
    {
        // ARRANGE
        var mockPublisher = new Mock<IDashboardPublisher>(MockBehavior.Strict);
        mockPublisher
            .Setup(p => p.PublishAsync(It.IsAny<DashboardPublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardPublishResult
            {
                Id = "abc123",
                ViewUrl = "https://dashboard.example.com/view/abc123"
            });

        var tool = new DashboardPublisherTool(mockPublisher.Object, NullLogger<DashboardPublisherTool>.Instance);

        var json = """
        {
            "title": "Multi-repo Insights",
            "insights": [
                {
                    "repo": "elbruno/openclawnet",
                    "openIssues": 15,
                    "openPRs": 3,
                    "stars": 42,
                    "lastPush": "2026-05-06T14:30:00Z",
                    "summary": "Active development"
                }
            ],
            "format": "card"
        }
        """;

        // ACT
        var result = await tool.ExecuteAsync(Args(json));

        // ASSERT
        Assert.True(result.Success, result.Error);
        Assert.Contains("https://dashboard.example.com/view/abc123", result.Output);
        Assert.Contains("Published", result.Output);

        // Verify publisher was called with correct data
        mockPublisher.Verify(p => p.PublishAsync(
            It.Is<DashboardPublishRequest>(req =>
                req.Title == "Multi-repo Insights" &&
                req.Insights.Count == 1 &&
                req.Insights[0].Repo == "elbruno/openclawnet" &&
                req.Insights[0].OpenIssues == 15),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Publisher_Throws_DashboardPublisherException_Returns_Error_Result()
    {
        // ARRANGE
        var mockPublisher = new Mock<IDashboardPublisher>(MockBehavior.Strict);
        mockPublisher
            .Setup(p => p.PublishAsync(It.IsAny<DashboardPublishRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DashboardPublisherException(System.Net.HttpStatusCode.Unauthorized, "Invalid API key"));

        var tool = new DashboardPublisherTool(mockPublisher.Object, NullLogger<DashboardPublisherTool>.Instance);

        var json = """
        {
            "title": "Test",
            "insights": [{ "repo": "test/repo" }]
        }
        """;

        // ACT
        var result = await tool.ExecuteAsync(Args(json));

        // ASSERT
        Assert.False(result.Success);
        Assert.Contains("401", result.Error);
        Assert.Contains("Unauthorized", result.Error);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_Publisher_Throws_Generic_Exception_Returns_Error_Result()
    {
        // ARRANGE
        var mockPublisher = new Mock<IDashboardPublisher>(MockBehavior.Strict);
        mockPublisher
            .Setup(p => p.PublishAsync(It.IsAny<DashboardPublishRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Network failure"));

        var tool = new DashboardPublisherTool(mockPublisher.Object, NullLogger<DashboardPublisherTool>.Instance);

        var json = """
        {
            "title": "Test",
            "insights": [{ "repo": "test/repo" }]
        }
        """;

        // ACT
        var result = await tool.ExecuteAsync(Args(json));

        // ASSERT
        Assert.False(result.Success);
        Assert.Contains("Unexpected error", result.Error);
        Assert.Contains("Network failure", result.Error);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_Multiple_Insights_All_Serialized_To_Publisher()
    {
        // ARRANGE
        var mockPublisher = new Mock<IDashboardPublisher>(MockBehavior.Strict);
        mockPublisher
            .Setup(p => p.PublishAsync(It.IsAny<DashboardPublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardPublishResult
            {
                Id = "xyz789",
                ViewUrl = "https://dashboard.example.com/view/xyz789"
            });

        var tool = new DashboardPublisherTool(mockPublisher.Object, NullLogger<DashboardPublisherTool>.Instance);

        var json = """
        {
            "title": "Three Repos",
            "insights": [
                { "repo": "owner1/repo1", "stars": 10 },
                { "repo": "owner2/repo2", "stars": 20 },
                { "repo": "owner3/repo3", "stars": 30 }
            ]
        }
        """;

        // ACT
        var result = await tool.ExecuteAsync(Args(json));

        // ASSERT
        Assert.True(result.Success, result.Error);

        mockPublisher.Verify(p => p.PublishAsync(
            It.Is<DashboardPublishRequest>(req =>
                req.Insights.Count == 3 &&
                req.Insights[0].Repo == "owner1/repo1" &&
                req.Insights[1].Repo == "owner2/repo2" &&
                req.Insights[2].Repo == "owner3/repo3"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.Dashboard;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using System.Text.Json;

namespace OpenClawNet.IntegrationTests.Tools;

/// <summary>
/// Integration tests for S4 (Dashboard Publisher) - WireMock round-trip test.
/// Tests that DashboardPublisherTool can successfully publish insights through the
/// full HTTP pipeline to a WireMock server simulating the external dashboard API.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Layer", "Integration")]
public sealed class DashboardPublisherToolWireMockTests : IAsyncLifetime
{
    private WireMockServer? _wireMockServer;
    private IServiceProvider? _serviceProvider;

    public Task InitializeAsync()
    {
        // Start WireMock server
        _wireMockServer = WireMockServer.Start();
        
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_wireMockServer is not null)
        {
            _wireMockServer.Stop();
            _wireMockServer.Dispose();
        }
        
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        
        await Task.CompletedTask;
    }

    [Fact]
    public async Task DashboardPublisherTool_Success_RoundTrip_Returns_ViewUrl()
    {
        // ARRANGE: Set up WireMock stub for dashboard API endpoint
        _wireMockServer.Should().NotBeNull();
        
        const string expectedId = "abc123";
        const string expectedViewUrl = "https://dashboard.example.com/view/abc123";

        // Stub POST /api/v1/insights
        _wireMockServer!.Given(
            Request.Create()
                .WithPath("/api/v1/insights")
                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(201)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody($$"""
                    {
                        "id": "{{expectedId}}",
                        "viewUrl": "{{expectedViewUrl}}"
                    }
                    """));

        // Configure DI container with WireMock base URL
        var services = new ServiceCollection();
        
        // Add configuration pointing to WireMock
        var configValues = new Dictionary<string, string?>
        {
            ["Dashboard:BaseUrl"] = _wireMockServer.Urls[0],
            ["Dashboard:ApiKey"] = "test-api-key-123",
            ["Dashboard:TimeoutSeconds"] = "30"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
        
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        
        // Add DashboardTool with publisher pointing at WireMock
        services.AddDashboardTool(configuration);
        
        // Also register the concrete type for test resolution
        services.AddSingleton(sp => sp.GetServices<ITool>().OfType<DashboardPublisherTool>().First());
        
        _serviceProvider = services.BuildServiceProvider();

        // Resolve DashboardPublisherTool from DI
        var dashboardTool = _serviceProvider.GetRequiredService<DashboardPublisherTool>();
        
        dashboardTool.Should().NotBeNull("DashboardPublisherTool should be registered in DI");

        // ACT: Invoke tool with ToolInput
        var input = new ToolInput
        {
            ToolName = "dashboard_publish",
            RawArguments = JsonSerializer.Serialize(new
            {
                title = "Multi-repo Insights 2026-05-06",
                insights = new[]
                {
                    new
                    {
                        repo = "elbruno/openclawnet",
                        openIssues = 15,
                        openPRs = 3,
                        stars = 42,
                        lastPush = "2026-05-06T14:30:00Z",
                        summary = "Active development"
                    },
                    new
                    {
                        repo = "elbruno/phi3",
                        openIssues = 8,
                        openPRs = 1,
                        stars = 128,
                        lastPush = "2026-05-05T10:00:00Z",
                        summary = "Stable"
                    }
                },
                format = "card"
            })
        };

        var result = await dashboardTool!.ExecuteAsync(input);

        // ASSERT: Verify result
        result.Should().NotBeNull();
        result.Success.Should().BeTrue($"tool execution should succeed; error: {result.Error}");
        result.Output.Should().NotBeNullOrWhiteSpace("output should return dashboard URL");
        result.Output.Should().Contain(expectedViewUrl, "output should contain the view URL from WireMock");
        result.Output.Should().Contain("Published", "output should confirm publication");

        // Verify WireMock received the expected POST request
        var requests = _wireMockServer.LogEntries
            .Where(e => e.RequestMessage.Path == "/api/v1/insights" && e.RequestMessage.Method == "POST")
            .ToList();
        requests.Should().ContainSingle("WireMock should have received exactly one POST request");

        // Verify request body shape
        var requestBody = requests[0].RequestMessage.Body;
        requestBody.Should().NotBeNull("request should have a body");
        
        var bodyJson = JsonDocument.Parse(requestBody!);
        var root = bodyJson.RootElement;
        
        root.GetProperty("title").GetString().Should().Be("Multi-repo Insights 2026-05-06");
        root.GetProperty("source").GetString().Should().Be("openclawnet");
        root.GetProperty("insights").GetArrayLength().Should().Be(2);
        
        var insights = root.GetProperty("insights");
        insights[0].GetProperty("repo").GetString().Should().Be("elbruno/openclawnet");
        insights[0].GetProperty("openIssues").GetInt32().Should().Be(15);
        insights[1].GetProperty("repo").GetString().Should().Be("elbruno/phi3");
        insights[1].GetProperty("stars").GetInt32().Should().Be(128);

        // Verify Authorization header
        var authHeader = requests[0].RequestMessage.Headers?["Authorization"];
        authHeader.Should().NotBeNull("request should have Authorization header");
        authHeader!.ToString().Should().Contain("Bearer test-api-key-123");
    }

    [Fact]
    public async Task DashboardPublisherTool_401_Unauthorized_Returns_Error()
    {
        // ARRANGE: Stub returns 401
        _wireMockServer.Should().NotBeNull();
        
        _wireMockServer!.Given(
            Request.Create()
                .WithPath("/api/v1/insights")
                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(401)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""
                    {
                        "error": "Invalid API key"
                    }
                    """));

        // Configure DI
        var services = new ServiceCollection();
        var configValues = new Dictionary<string, string?>
        {
            ["Dashboard:BaseUrl"] = _wireMockServer.Urls[0],
            ["Dashboard:ApiKey"] = "invalid-key",
            ["Dashboard:TimeoutSeconds"] = "30"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
        
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddDashboardTool(configuration);
        
        // Also register the concrete type for test resolution
        services.AddSingleton(sp => sp.GetServices<ITool>().OfType<DashboardPublisherTool>().First());
        
        _serviceProvider = services.BuildServiceProvider();

        var dashboardTool = _serviceProvider.GetRequiredService<DashboardPublisherTool>();
        
        dashboardTool.Should().NotBeNull();

        // ACT
        var input = new ToolInput
        {
            ToolName = "dashboard_publish",
            RawArguments = JsonSerializer.Serialize(new
            {
                title = "Test",
                insights = new[] { new { repo = "test/repo" } }
            })
        };

        var result = await dashboardTool!.ExecuteAsync(input);

        // ASSERT
        result.Should().NotBeNull();
        result.Success.Should().BeFalse("tool should report failure on 401");
        result.Error.Should().NotBeNullOrWhiteSpace("error message should be populated");
        result.Error.Should().Contain("401", "error should mention 401 status code");
        result.Error.Should().Contain("Unauthorized", "error should mention Unauthorized");
    }

    [Fact]
    public async Task DashboardPublisherTool_500_Server_Error_Returns_Error()
    {
        // ARRANGE: Stub returns 500
        _wireMockServer.Should().NotBeNull();
        
        _wireMockServer!.Given(
            Request.Create()
                .WithPath("/api/v1/insights")
                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(500)
                    .WithHeader("Content-Type", "text/plain")
                    .WithBody("Internal server error"));

        // Configure DI
        var services = new ServiceCollection();
        var configValues = new Dictionary<string, string?>
        {
            ["Dashboard:BaseUrl"] = _wireMockServer.Urls[0],
            ["Dashboard:ApiKey"] = "test-key",
            ["Dashboard:TimeoutSeconds"] = "30"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
        
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddDashboardTool(configuration);
        
        // Also register the concrete type for test resolution
        services.AddSingleton(sp => sp.GetServices<ITool>().OfType<DashboardPublisherTool>().First());
        
        _serviceProvider = services.BuildServiceProvider();

        var dashboardTool = _serviceProvider.GetRequiredService<DashboardPublisherTool>();
        
        dashboardTool.Should().NotBeNull();

        // ACT
        var input = new ToolInput
        {
            ToolName = "dashboard_publish",
            RawArguments = JsonSerializer.Serialize(new
            {
                title = "Test",
                insights = new[] { new { repo = "test/repo" } }
            })
        };

        var result = await dashboardTool!.ExecuteAsync(input);

        // ASSERT
        result.Should().NotBeNull();
        result.Success.Should().BeFalse("tool should report failure on 500");
        result.Error.Should().NotBeNullOrWhiteSpace("error message should be populated");
        result.Error.Should().Contain("500", "error should mention 500 status code");
    }

    [Fact]
    public async Task DashboardPublisherTool_Verifies_Request_JSON_Payload_Structure()
    {
        // ARRANGE: Stub returns success for any POST
        _wireMockServer.Should().NotBeNull();
        
        _wireMockServer!.Given(
            Request.Create()
                .WithPath("/api/v1/insights")
                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(201)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""
                    {
                        "id": "matched123",
                        "viewUrl": "https://dashboard.example.com/view/matched123"
                    }
                    """));

        // Configure DI
        var services = new ServiceCollection();
        var configValues = new Dictionary<string, string?>
        {
            ["Dashboard:BaseUrl"] = _wireMockServer.Urls[0],
            ["Dashboard:ApiKey"] = "test-key",
            ["Dashboard:TimeoutSeconds"] = "30"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
        
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddDashboardTool(configuration);
        
        // Also register the concrete type for test resolution
        services.AddSingleton(sp => sp.GetServices<ITool>().OfType<DashboardPublisherTool>().First());
        
        _serviceProvider = services.BuildServiceProvider();

        var dashboardTool = _serviceProvider.GetRequiredService<DashboardPublisherTool>();
        
        dashboardTool.Should().NotBeNull();

        // ACT
        var input = new ToolInput
        {
            ToolName = "dashboard_publish",
            RawArguments = JsonSerializer.Serialize(new
            {
                title = "Specific Title",
                insights = new[]
                {
                    new { repo = "owner/repo", stars = 99 }
                }
            })
        };

        var result = await dashboardTool!.ExecuteAsync(input);

        // ASSERT
        result.Success.Should().BeTrue("tool should succeed");
        result.Output.Should().Contain("matched123", "should receive ID from WireMock stub");

        // Verify all expected fields are in the request
        var requests = _wireMockServer.LogEntries
            .Where(e => e.RequestMessage.Path == "/api/v1/insights")
            .ToList();
        requests.Should().ContainSingle();

        var bodyJson = JsonDocument.Parse(requests[0].RequestMessage.Body!);
        var root = bodyJson.RootElement;
        
        root.TryGetProperty("title", out var titleProp).Should().BeTrue();
        titleProp.GetString().Should().Be("Specific Title");
        
        root.TryGetProperty("source", out var sourceProp).Should().BeTrue();
        sourceProp.GetString().Should().Be("openclawnet");
        
        root.TryGetProperty("generatedAt", out _).Should().BeTrue();
        root.TryGetProperty("insights", out var insightsProp).Should().BeTrue();
        
        insightsProp.GetArrayLength().Should().Be(1);
        insightsProp[0].GetProperty("repo").GetString().Should().Be("owner/repo");
        insightsProp[0].GetProperty("stars").GetInt32().Should().Be(99);
    }
}

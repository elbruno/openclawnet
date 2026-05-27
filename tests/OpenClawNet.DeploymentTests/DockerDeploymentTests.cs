using System.Net;
using System.Text.Json;
using FluentAssertions;
using OpenClawNet.DeploymentTests.Fixtures;

namespace OpenClawNet.DeploymentTests;

/// <summary>
/// End-to-end Docker deployment tests for OpenClawNet.
/// Validates that docker-compose up successfully starts the full stack
/// and all services are healthy and communicating.
/// 
/// Tests are skipped if Docker is not available on the system.
/// </summary>
public sealed class DockerDeploymentTests : IAsyncLifetime
{
    private readonly DockerComposeFixture _fixture;
    private readonly HealthCheckHelper _healthCheck;
    private string? _skipReason;

    public DockerDeploymentTests()
    {
        // Default: compose file expected at docs/deployments/docker/
        var composeDir = Path.Combine(GetProjectRoot(), "docs", "deployments", "docker");
        _fixture = new DockerComposeFixture(composeDir);
        _healthCheck = new HealthCheckHelper();
    }

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();

        if (!_fixture.IsDockerAvailable)
        {
            _skipReason = "Docker and docker-compose are not available on this system";
        }
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    [Fact(Skip = "Waiting for Irving to deliver Dockerfile and docker-compose.yml")]
    public async Task StartDockerCompose_AllServicesHealthy()
    {
        if (_skipReason != null) Skip.If(true, _skipReason);

        _fixture.IsRunning.Should().BeTrue("services should be running after InitializeAsync");

        // Wait for the gateway health endpoint to become available (primary API service)
        var gatewayHealthUrl = "http://localhost:8080/health";
        
        // Retry health checks with exponential backoff
        await _healthCheck.WaitForHealthyAsync(gatewayHealthUrl);

        // Verify the service is responsive
        using var client = new HttpClient();
        var response = await client.GetAsync(gatewayHealthUrl);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(Skip = "Waiting for Irving to deliver Dockerfile and docker-compose.yml")]
    public async Task ServiceCommunication_ApiCanReachDatabase()
    {
        if (_skipReason != null) Skip.If(true, _skipReason);

        await _fixture.InitializeAsync(); // Ensure services are up

        var apiBaseUrl = "http://localhost:8080";
        
        // Wait for API to be healthy
        await _healthCheck.WaitForHealthyAsync($"{apiBaseUrl}/health");

        using var client = new HttpClient();
        
        // Test a simple API call that exercises the database
        // (This endpoint should exist in the Gateway)
        var response = await client.GetAsync($"{apiBaseUrl}/api/version");
        
        response.StatusCode.Should().Be(HttpStatusCode.OK, "API should return version info");
        
        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        
        doc.RootElement.GetProperty("name").GetString().Should().Be("OpenClawNet");
        doc.RootElement.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact(Skip = "Waiting for Irving to deliver Dockerfile and docker-compose.yml")]
    public async Task HealthChecks_ReadinessAndLiveness()
    {
        if (_skipReason != null) Skip.If(true, _skipReason);

        var apiBaseUrl = "http://localhost:8080";
        
        // Wait for the service to be healthy
        await _healthCheck.WaitForHealthyAsync($"{apiBaseUrl}/health");

        using var client = new HttpClient();

        // Test readiness endpoint
        var readinessResponse = await client.GetAsync($"{apiBaseUrl}/health/ready");
        readinessResponse.StatusCode.Should().Be(HttpStatusCode.OK, "readiness check should pass");
        
        var readinessBody = await readinessResponse.Content.ReadAsStringAsync();
        using var readinessDoc = JsonDocument.Parse(readinessBody);
        readinessDoc.RootElement.GetProperty("status").GetString().Should().Be("healthy");

        // Test liveness endpoint
        var livenessResponse = await client.GetAsync($"{apiBaseUrl}/health/live");
        livenessResponse.StatusCode.Should().Be(HttpStatusCode.OK, "liveness check should pass");
        
        var livenessBody = await livenessResponse.Content.ReadAsStringAsync();
        using var livenessDoc = JsonDocument.Parse(livenessBody);
        livenessDoc.RootElement.GetProperty("status").GetString().Should().Be("healthy");
    }

    [Fact(Skip = "Waiting for Irving to deliver Dockerfile and docker-compose.yml")]
    public async Task Logging_OutputCapture()
    {
        if (_skipReason != null) Skip.If(true, _skipReason);

        await _fixture.CaptureLogsAsync();

        var logs = _fixture.CapturedLogs;
        logs.Should().NotBeEmpty("logs should be captured");

        // Verify expected startup messages appear (e.g., service names)
        var logText = string.Join("\n", logs);
        logText.Should().Contain("gateway");

        // Verify no critical ERROR-level logs that would indicate a failed startup
        var errorLogs = _fixture.GetErrorLogs().ToList();
        
        // Some warnings are OK, but ERROR level logs indicate a problem
        errorLogs.Should().BeEmpty(
            $"Docker logs should not contain ERROR-level messages; found: {string.Join(", ", errorLogs)}");
    }

    [Fact(Skip = "Waiting for Irving to deliver Dockerfile and docker-compose.yml")]
    public async Task EnvironmentVariables_ProperlyInjected()
    {
        if (_skipReason != null) Skip.If(true, _skipReason);

        // Check if the gateway container is running
        var containerRunning = await _fixture.IsContainerRunningAsync("openclawnet-gateway");
        
        if (!containerRunning)
        {
            Skip.If(true, "Gateway container not found; compose may not define it with expected name");
        }

        // Get environment variables from the running container
        var containerEnv = await _fixture.GetContainerEnvAsync("openclawnet-gateway");
        
        containerEnv.Should().NotBeEmpty("container should have environment variables");

        // Verify some expected env vars are present
        // These would come from the docker-compose.yml and .env file
        containerEnv.Keys.Should().Contain(k => k.StartsWith("DOTNET_"),
            "container should have .NET environment variables");
    }

    private static string GetProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OpenClawNet.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}

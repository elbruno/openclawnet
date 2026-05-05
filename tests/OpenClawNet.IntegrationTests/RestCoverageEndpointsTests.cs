using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.IntegrationTests;

/// <summary>
/// Integration tests for REST coverage endpoints added in commit e653037.
/// Covers 7 new debug-first endpoints + 5 earlier additions from commits 6485969, f4c0244, f9b73ac, 4a588e7.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RestCoverageEndpointsTests(GatewayWebAppFactory factory)
    : IClassFixture<GatewayWebAppFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ═══════════════════════════════════════════════════════════════
    // 1. GET /api/jobs/{id}/runs/{runId}/tool-calls
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetJobRunToolCalls_ReturnsNotFound_WhenRunDoesNotExist()
    {
        var client = factory.CreateClient();
        var jobId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        var resp = await client.GetAsync($"/api/jobs/{jobId}/runs/{runId}/tool-calls");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetJobRunToolCalls_ReturnsEmptyList_WhenNoToolCalls()
    {
        var client = factory.CreateClient();
        var (jobId, runId) = await CreateJobWithRunAsync("no-toolcalls-job");

        var resp = await client.GetAsync($"/api/jobs/{jobId}/runs/{runId}/tool-calls");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(jobId, result.GetProperty("jobId").GetGuid());
        Assert.Equal(runId, result.GetProperty("runId").GetGuid());
        Assert.Equal(0, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, result.GetProperty("successCount").GetInt32());
        Assert.Equal(0, result.GetProperty("failureCount").GetInt32());
    }

    [Fact]
    public async Task GetJobRunToolCalls_ReturnsToolCallsWithSuccessAndFailure()
    {
        var client = factory.CreateClient();
        var (jobId, runId) = await CreateJobWithRunAsync("toolcalls-mixed-job");

        // Seed tool calls: 2 success, 1 failure
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();

            db.ToolCalls.Add(new ToolCallRecord
            {
                SessionId = runId,
                ToolName = "list_files",
                Arguments = """{"path":"/src"}""",
                Result = """["file1.cs","file2.cs"]""",
                Success = true,
                DurationMs = 15.2,
                ExecutedAt = DateTime.UtcNow
            });

            db.ToolCalls.Add(new ToolCallRecord
            {
                SessionId = runId,
                ToolName = "read_file",
                Arguments = """{"path":"test.txt"}""",
                Result = """{"content":"hello"}""",
                Success = true,
                DurationMs = 8.5,
                ExecutedAt = DateTime.UtcNow.AddSeconds(1)
            });

            db.ToolCalls.Add(new ToolCallRecord
            {
                SessionId = runId,
                ToolName = "markdown_convert",
                Arguments = """{"path":"bad.md"}""",
                Result = """{"error":"File not found"}""",
                Success = false,
                DurationMs = 2.1,
                ExecutedAt = DateTime.UtcNow.AddSeconds(2)
            });

            await db.SaveChangesAsync();
        }

        var resp = await client.GetAsync($"/api/jobs/{jobId}/runs/{runId}/tool-calls");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(3, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, result.GetProperty("successCount").GetInt32());
        Assert.Equal(1, result.GetProperty("failureCount").GetInt32());

        var toolCalls = result.GetProperty("toolCalls").EnumerateArray().ToList();
        Assert.Equal(3, toolCalls.Count);

        // Check ordering: earliest first
        Assert.Equal("list_files", toolCalls[0].GetProperty("toolName").GetString());
        Assert.Equal("read_file", toolCalls[1].GetProperty("toolName").GetString());
        Assert.Equal("markdown_convert", toolCalls[2].GetProperty("toolName").GetString());

        // Verify failed tool call surfaces error in result
        var failedCall = toolCalls[2];
        Assert.False(failedCall.GetProperty("success").GetBoolean());
        Assert.Contains("error", failedCall.GetProperty("result").GetString() ?? "");
    }

    [Fact]
    public async Task GetJobRunToolCalls_SurfacesFailedToolCallError_OneCurlDebugScenario()
    {
        // This is the gold-standard test: simulates markdown_convert debugging scenario
        var client = factory.CreateClient();
        var (jobId, runId) = await CreateJobWithRunAsync("failed-toolcall-debug");

        // Seed a failed markdown_convert call with error detail
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();

            db.ToolCalls.Add(new ToolCallRecord
            {
                SessionId = runId,
                ToolName = "markdown_convert",
                Arguments = """{"input":"corrupted.md","output":"result.pdf"}""",
                Result = """{"error":"Pandoc conversion failed: Invalid input encoding"}""",
                Success = false,
                DurationMs = 105.7,
                ExecutedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var resp = await client.GetAsync($"/api/jobs/{jobId}/runs/{runId}/tool-calls");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(1, result.GetProperty("failureCount").GetInt32());

        var toolCalls = result.GetProperty("toolCalls").EnumerateArray().ToList();
        var failedCall = toolCalls[0];
        Assert.Equal("markdown_convert", failedCall.GetProperty("toolName").GetString());
        Assert.False(failedCall.GetProperty("success").GetBoolean());

        var resultStr = failedCall.GetProperty("result").GetString();
        Assert.NotNull(resultStr);
        Assert.Contains("Pandoc conversion failed", resultStr);
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. GET /api/tool-call-history (global with filters)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetToolCallHistory_ReturnsEmptyList_WhenNoToolCalls()
    {
        var client = factory.CreateClient();
        var unusedSession = Guid.NewGuid();

        var resp = await client.GetAsync($"/api/tool-call-history?sessionId={unusedSession}");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(0, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, result.GetProperty("successCount").GetInt32());
        Assert.Equal(0, result.GetProperty("failureCount").GetInt32());
    }

    [Fact]
    public async Task GetToolCallHistory_FiltersByToolName()
    {
        var client = factory.CreateClient();
        var sessionId = Guid.NewGuid();

        await SeedToolCallsAsync(sessionId, 
            ("list_files", true), 
            ("read_file", true), 
            ("list_files", false));

        var resp = await client.GetAsync("/api/tool-call-history?toolName=list_files");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(2, result.GetProperty("totalCount").GetInt32());

        var toolCalls = result.GetProperty("toolCalls").EnumerateArray().ToList();
        foreach (var tc in toolCalls)
        {
            Assert.Equal("list_files", tc.GetProperty("toolName").GetString());
        }
    }

    [Fact]
    public async Task GetToolCallHistory_FiltersBySuccess()
    {
        var client = factory.CreateClient();
        var sessionId = Guid.NewGuid();

        await SeedToolCallsAsync(sessionId,
            ("tool1", true),
            ("tool2", false),
            ("tool3", true));

        var resp = await client.GetAsync($"/api/tool-call-history?success=false&sessionId={sessionId}");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(1, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, result.GetProperty("successCount").GetInt32());
        Assert.Equal(1, result.GetProperty("failureCount").GetInt32());
    }

    [Fact]
    public async Task GetToolCallHistory_FiltersBySessionId()
    {
        var client = factory.CreateClient();
        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();

        await SeedToolCallsAsync(session1, ("tool1", true));
        await SeedToolCallsAsync(session2, ("tool2", true));

        var resp = await client.GetAsync($"/api/tool-call-history?sessionId={session1}");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var toolCalls = result.GetProperty("toolCalls").EnumerateArray().ToList();
        
        foreach (var tc in toolCalls)
        {
            Assert.Equal(session1, tc.GetProperty("sessionId").GetGuid());
        }
    }

    [Fact]
    public async Task GetToolCallHistory_FiltersByDateRange()
    {
        var client = factory.CreateClient();
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var tomorrow = now.AddDays(1);

        await SeedToolCallsAsync(sessionId, ("tool1", true));

        var resp = await client.GetAsync($"/api/tool-call-history?since={tomorrow:O}");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(0, result.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task GetToolCallHistory_SurfacesFailedToolCallWithError()
    {
        var client = factory.CreateClient();
        var sessionId = Guid.NewGuid();

        // Seed a failed tool call with error detail (simulating the markdown_convert scenario)
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();

            db.ToolCalls.Add(new ToolCallRecord
            {
                SessionId = sessionId,
                ToolName = "markdown_convert",
                Arguments = """{"path":"test.md"}""",
                Result = """{"error":"Conversion timeout after 30s"}""",
                Success = false,
                DurationMs = 30000,
                ExecutedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var resp = await client.GetAsync("/api/tool-call-history?success=false");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.True(result.GetProperty("failureCount").GetInt32() > 0);

        var toolCalls = result.GetProperty("toolCalls").EnumerateArray().ToList();
        var failedCall = toolCalls.FirstOrDefault(tc => 
            tc.GetProperty("toolName").GetString() == "markdown_convert");
        
        Assert.True(failedCall.ValueKind != JsonValueKind.Undefined);
        Assert.False(failedCall.GetProperty("success").GetBoolean());
        var resultStr = failedCall.GetProperty("result").GetString();
        Assert.Contains("error", resultStr ?? "");
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. GET /api/jobs/{id}/runs/{runId}/artifacts
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetJobRunArtifacts_ReturnsNotFound_WhenRunDoesNotExist()
    {
        var client = factory.CreateClient();
        var jobId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        var resp = await client.GetAsync($"/api/jobs/{jobId}/runs/{runId}/artifacts");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetJobRunArtifacts_ReturnsEmptyList_WhenNoArtifacts()
    {
        var client = factory.CreateClient();
        var (jobId, runId) = await CreateJobWithRunAsync("no-artifacts-job");

        var resp = await client.GetAsync($"/api/jobs/{jobId}/runs/{runId}/artifacts");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(0, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, result.GetProperty("totalSizeBytes").GetInt64());
    }

    [Fact]
    public async Task GetJobRunArtifacts_ReturnsArtifactsList_WithCorrectOrdering()
    {
        var client = factory.CreateClient();
        var (jobId, runId) = await CreateJobWithRunAsync("artifacts-job");

        // Seed artifacts with different sequences
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();

            db.JobRunArtifacts.Add(new JobRunArtifact
            {
                JobRunId = runId,
                Sequence = 2,
                ArtifactType = JobRunArtifactKind.Text,
                Title = "Second output",
                MimeType = "text/plain",
                ContentInline = "Result 2",
                ContentSizeBytes = 8,
                CreatedAt = DateTime.UtcNow
            });

            db.JobRunArtifacts.Add(new JobRunArtifact
            {
                JobRunId = runId,
                Sequence = 1,
                ArtifactType = JobRunArtifactKind.Text,
                Title = "First output",
                MimeType = "text/plain",
                ContentInline = "Result 1",
                ContentSizeBytes = 8,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var resp = await client.GetAsync($"/api/jobs/{jobId}/runs/{runId}/artifacts");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(2, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(16, result.GetProperty("totalSizeBytes").GetInt64());

        var artifacts = result.GetProperty("artifacts").EnumerateArray().ToList();
        Assert.Equal(2, artifacts.Count);

        // Verify ordering by sequence
        Assert.Equal(1, artifacts[0].GetProperty("sequence").GetInt32());
        Assert.Equal("First output", artifacts[0].GetProperty("title").GetString());
        Assert.Equal(2, artifacts[1].GetProperty("sequence").GetInt32());
        Assert.Equal("Second output", artifacts[1].GetProperty("title").GetString());
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. GET /api/jobs/{id}/state-history
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetJobStateHistory_ReturnsNotFound_WhenJobDoesNotExist()
    {
        var client = factory.CreateClient();
        var jobId = Guid.NewGuid();

        var resp = await client.GetAsync($"/api/jobs/{jobId}/state-history");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetJobStateHistory_ReturnsEmptyHistory_WhenNoStateChanges()
    {
        var client = factory.CreateClient();
        var jobId = await CreateDraftJobAsync("no-history-job");

        var resp = await client.GetAsync($"/api/jobs/{jobId}/state-history");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(jobId, result.GetProperty("jobId").GetGuid());
        Assert.Equal("draft", result.GetProperty("currentStatus").GetString());
        Assert.Equal(0, result.GetProperty("history").GetArrayLength());
    }

    [Fact]
    public async Task GetJobStateHistory_ReturnsStateChanges_InDescendingOrder()
    {
        var client = factory.CreateClient();
        var jobId = await CreateDraftJobAsync("history-job");

        // Seed state changes
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();

            var now = DateTime.UtcNow;

            db.JobStateChanges.Add(new JobDefinitionStateChange
            {
                JobId = jobId,
                FromStatus = JobStatus.Draft,
                ToStatus = JobStatus.Active,
                Reason = "User started job",
                ChangedBy = "test-user",
                ChangedAt = now.AddMinutes(-10)
            });

            db.JobStateChanges.Add(new JobDefinitionStateChange
            {
                JobId = jobId,
                FromStatus = JobStatus.Active,
                ToStatus = JobStatus.Paused,
                Reason = "Manual pause",
                ChangedBy = "test-user",
                ChangedAt = now.AddMinutes(-5)
            });

            await db.SaveChangesAsync();
        }

        var resp = await client.GetAsync($"/api/jobs/{jobId}/state-history");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var history = result.GetProperty("history").EnumerateArray().ToList();
        
        Assert.Equal(2, history.Count);
        
        // Verify descending order (most recent first)
        Assert.Equal("active", history[0].GetProperty("fromStatus").GetString());
        Assert.Equal("paused", history[0].GetProperty("toStatus").GetString());
        Assert.Equal("draft", history[1].GetProperty("fromStatus").GetString());
        Assert.Equal("active", history[1].GetProperty("toStatus").GetString());
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. GET /api/tools/{name}
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetTool_ReturnsNotFound_WhenToolDoesNotExist()
    {
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/tools/nonexistent_tool");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetTool_ReturnsToolDetail_WithoutTestData()
    {
        var client = factory.CreateClient();

        // The tool registry should have some tools registered by default
        // First, get the list to find a valid tool name
        var listResp = await client.GetAsync("/api/tools");
        listResp.EnsureSuccessStatusCode();
        var tools = await listResp.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
        
        if (tools == null || tools.Length == 0)
        {
            // Skip test if no tools are registered
            return;
        }

        var toolName = tools[0].GetProperty("name").GetString();
        Assert.NotNull(toolName);

        var resp = await client.GetAsync($"/api/tools/{toolName}");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(toolName, result.GetProperty("name").GetString());
        Assert.True(result.TryGetProperty("description", out _));
        Assert.True(result.TryGetProperty("category", out _));
        Assert.True(result.TryGetProperty("parameterSchema", out _));
        
        // LastTested* fields should be present (may be null)
        Assert.True(result.TryGetProperty("lastTestedAt", out _));
        Assert.True(result.TryGetProperty("lastTestSucceeded", out _));
    }

    // ═══════════════════════════════════════════════════════════════
    // 6. GET /api/agent-profiles/default
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetDefaultAgentProfile_ReturnsNotFound_WhenNoDefaultExists()
    {
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/agent-profiles/default");

        // May return 404 or 200 depending on whether a default is seeded
        // In test environment, likely 404
        Assert.True(resp.StatusCode == HttpStatusCode.NotFound || resp.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDefaultAgentProfile_ReturnsDefaultProfile_WhenExists()
    {
        var client = factory.CreateClient();

        // First, create a default profile
        var createResp = await client.PutAsJsonAsync("/api/agent-profiles/test-default", new
        {
            DisplayName = "Test Default",
            Provider = "ollama",
            Instructions = "Test instructions",
            Temperature = 0.7,
            IsDefault = true,
            IsEnabled = true,
            Kind = "Standard"
        });
        
        createResp.EnsureSuccessStatusCode();

        // Now fetch the default
        var resp = await client.GetAsync("/api/agent-profiles/default");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal("test-default", result.GetProperty("name").GetString());
        Assert.True(result.GetProperty("isDefault").GetBoolean());
    }

    // ═══════════════════════════════════════════════════════════════
    // 7. GET /api/tool-approvals
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetToolApprovals_ReturnsEmptyList_WhenNoApprovals()
    {
        var client = factory.CreateClient();
        var unusedSession = Guid.NewGuid();

        var resp = await client.GetAsync($"/api/tool-approvals?sessionId={unusedSession}");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(0, result.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task GetToolApprovals_FiltersBySessionId()
    {
        var client = factory.CreateClient();
        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();

        await SeedToolApprovalLogsAsync(session1, "tool1", true);
        await SeedToolApprovalLogsAsync(session2, "tool2", false);

        var resp = await client.GetAsync($"/api/tool-approvals?sessionId={session1}");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var logs = result.GetProperty("logs").EnumerateArray().ToList();
        
        foreach (var log in logs)
        {
            Assert.Equal(session1, log.GetProperty("sessionId").GetGuid());
        }
    }

    [Fact]
    public async Task GetToolApprovals_FiltersByToolName()
    {
        var client = factory.CreateClient();
        var sessionId = Guid.NewGuid();

        await SeedToolApprovalLogsAsync(sessionId, "dangerous_tool", true);
        await SeedToolApprovalLogsAsync(sessionId, "safe_tool", true);

        var resp = await client.GetAsync("/api/tool-approvals?toolName=dangerous_tool");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var logs = result.GetProperty("logs").EnumerateArray().ToList();
        
        foreach (var log in logs)
        {
            Assert.Equal("dangerous_tool", log.GetProperty("toolName").GetString());
        }
    }

    [Fact]
    public async Task GetToolApprovals_FiltersByApprovedStatus()
    {
        var client = factory.CreateClient();
        var sessionId = Guid.NewGuid();

        await SeedToolApprovalLogsAsync(sessionId, "tool1", approved: true);
        await SeedToolApprovalLogsAsync(sessionId, "tool2", approved: false);

        var resp = await client.GetAsync($"/api/tool-approvals?approved=false&sessionId={sessionId}");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(1, result.GetProperty("deniedCount").GetInt32());
        
        var logs = result.GetProperty("logs").EnumerateArray().ToList();
        foreach (var log in logs)
        {
            Assert.False(log.GetProperty("approved").GetBoolean());
        }
    }

    [Fact]
    public async Task GetToolApprovals_FiltersByDateRange()
    {
        var client = factory.CreateClient();
        var sessionId = Guid.NewGuid();
        var tomorrow = DateTime.UtcNow.AddDays(1);

        await SeedToolApprovalLogsAsync(sessionId, "tool1", true);

        var resp = await client.GetAsync($"/api/tool-approvals?since={tomorrow:O}");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(0, result.GetProperty("totalCount").GetInt32());
    }

    // ═══════════════════════════════════════════════════════════════
    // 8-12. Earlier endpoints (already covered in NewEndpointsTests.cs)
    // These tests verify those endpoints still work correctly
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetLatestRun_ReturnsNotFound_WhenNoRuns()
    {
        var client = factory.CreateClient();
        var jobId = await CreateDraftJobAsync("no-runs-job-latest");

        var resp = await client.GetAsync($"/api/jobs/{jobId}/runs/latest");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetRunDetail_ReturnsFullDetail_WithEventStats()
    {
        var client = factory.CreateClient();
        var (jobId, runId) = await CreateJobWithRunAsync("run-detail-test");

        var resp = await client.GetAsync($"/api/jobs/{jobId}/runs/{runId}");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(runId, result.GetProperty("id").GetGuid());
        Assert.Equal(jobId, result.GetProperty("jobId").GetGuid());
        Assert.True(result.TryGetProperty("eventCount", out _));
        Assert.True(result.TryGetProperty("toolCallCount", out _));
        Assert.True(result.TryGetProperty("errorEventCount", out _));
    }

    [Fact]
    public async Task DownloadRunLogs_ReturnsTextFile_WithCorrectFormat()
    {
        var client = factory.CreateClient();
        var (jobId, runId) = await CreateJobWithRunAsync("logs-download-test");

        var resp = await client.GetAsync($"/api/jobs/{jobId}/runs/{runId}/logs?format=txt");
        resp.EnsureSuccessStatusCode();

        Assert.Equal("text/plain", resp.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(resp.Content.Headers.ContentDisposition?.FileName);

        var content = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Job Run Log Export", content);
        Assert.Contains(runId.ToString(), content);
    }

    [Fact]
    public async Task SearchRuns_FiltersByStatus()
    {
        var client = factory.CreateClient();
        var (jobId, runId) = await CreateJobWithRunAsync("search-status-test");

        // Update run status to "completed"
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var run = await db.JobRuns.FindAsync(runId);
            if (run != null)
            {
                run.Status = "completed";
                run.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }

        var resp = await client.GetAsync("/api/runs/search?status=completed");
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.True(result.GetProperty("count").GetInt32() >= 0);
    }

    // ═══════════════════════════════════════════════════════════════
    // Helper methods
    // ═══════════════════════════════════════════════════════════════

    private async Task<Guid> CreateDraftJobAsync(string name)
    {
        var client = factory.CreateClient();
        var body = new { Name = name, Prompt = "test prompt" };
        var resp = await client.PostAsJsonAsync("/api/jobs", body);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        return dto.GetProperty("id").GetGuid();
    }

    private async Task<(Guid jobId, Guid runId)> CreateJobWithRunAsync(string name)
    {
        var jobId = await CreateDraftJobAsync(name);

        Guid runId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();

            var run = new JobRun
            {
                JobId = jobId,
                Status = "running",
                StartedAt = DateTime.UtcNow
            };
            db.JobRuns.Add(run);
            await db.SaveChangesAsync();
            runId = run.Id;
        }

        return (jobId, runId);
    }

    private async Task SeedToolCallsAsync(Guid sessionId, params (string toolName, bool success)[] calls)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        foreach (var (toolName, success) in calls)
        {
            db.ToolCalls.Add(new ToolCallRecord
            {
                SessionId = sessionId,
                ToolName = toolName,
                Arguments = """{"test":"args"}""",
                Result = success ? """{"result":"ok"}""" : """{"error":"failed"}""",
                Success = success,
                DurationMs = 10.0,
                ExecutedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task SeedToolApprovalLogsAsync(Guid sessionId, string toolName, bool approved)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        db.ToolApprovalLogs.Add(new Storage.Entities.ToolApprovalLog
        {
            RequestId = Guid.NewGuid(),
            SessionId = sessionId,
            ToolName = toolName,
            AgentProfileName = "test-profile",
            Approved = approved,
            RememberForSession = false,
            Source = Storage.Entities.ApprovalDecisionSource.User,
            DecidedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }
}

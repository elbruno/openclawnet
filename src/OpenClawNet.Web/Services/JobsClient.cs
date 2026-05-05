using System.Net.Http.Json;
using OpenClawNet.Web.Models.Jobs;

namespace OpenClawNet.Web.Services;

/// <summary>
/// Typed HttpClient for Jobs API endpoints.
/// Wraps both Gateway (/api/jobs) and Scheduler (/api/scheduler/jobs) endpoints.
/// </summary>
public sealed class JobsClient
{
    private readonly HttpClient _gatewayClient;
    private readonly HttpClient _schedulerClient;

    public JobsClient(IHttpClientFactory httpClientFactory)
    {
        _gatewayClient = httpClientFactory.CreateClient("gateway");
        _schedulerClient = httpClientFactory.CreateClient("scheduler");
    }

    // ── Gateway endpoints (CRUD) ──

    public async Task<List<JobDto>> ListAsync(CancellationToken ct = default)
    {
        var response = await _gatewayClient.GetAsync("api/jobs", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<JobDto>>(ct) ?? [];
    }

    public async Task<JobDetailDto?> GetAsync(Guid jobId, CancellationToken ct = default)
    {
        var response = await _gatewayClient.GetAsync($"api/jobs/{jobId}", ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<JobDetailDto>(ct);
    }

    public async Task<JobDto> CreateAsync(CreateJobRequest request, CancellationToken ct = default)
    {
        var response = await _gatewayClient.PostAsJsonAsync("api/jobs", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobDto>(ct)
            ?? throw new InvalidOperationException("Create job returned null");
    }

    public async Task<JobDto> UpdateAsync(Guid jobId, CreateJobRequest request, CancellationToken ct = default)
    {
        var response = await _gatewayClient.PutAsJsonAsync($"api/jobs/{jobId}", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobDto>(ct)
            ?? throw new InvalidOperationException("Update job returned null");
    }

    public async Task<bool> DeleteAsync(Guid jobId, CancellationToken ct = default)
    {
        var response = await _gatewayClient.DeleteAsync($"api/jobs/{jobId}", ct);
        return response.IsSuccessStatusCode;
    }

    // ── Scheduler endpoints (Lifecycle + Execution) ──

    public async Task<JobTransitionResponse> StartAsync(Guid jobId, CancellationToken ct = default)
    {
        var response = await _schedulerClient.PostAsync($"api/scheduler/jobs/{jobId}/start", null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobTransitionResponse>(ct)
            ?? throw new InvalidOperationException("Start job returned null");
    }

    public async Task<JobTransitionResponse> PauseAsync(Guid jobId, CancellationToken ct = default)
    {
        var response = await _schedulerClient.PostAsync($"api/scheduler/jobs/{jobId}/pause", null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobTransitionResponse>(ct)
            ?? throw new InvalidOperationException("Pause job returned null");
    }

    public async Task<JobTransitionResponse> ResumeAsync(Guid jobId, CancellationToken ct = default)
    {
        var response = await _schedulerClient.PostAsync($"api/scheduler/jobs/{jobId}/resume", null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobTransitionResponse>(ct)
            ?? throw new InvalidOperationException("Resume job returned null");
    }

    public async Task<JobTransitionResponse> CancelAsync(Guid jobId, CancellationToken ct = default)
    {
        var response = await _schedulerClient.PostAsync($"api/scheduler/jobs/{jobId}/cancel", null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobTransitionResponse>(ct)
            ?? throw new InvalidOperationException("Cancel job returned null");
    }

    public async Task<JobExecutionResultDto> ExecuteAsync(Guid jobId, CancellationToken ct = default)
    {
        // Manual trigger via scheduler/trigger endpoint (fire-and-forget in backend)
        var response = await _schedulerClient.PostAsync($"api/scheduler/jobs/{jobId}/trigger", null, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JobTriggerResponse>(ct);
        return new JobExecutionResultDto
        {
            RunId = result?.RunId ?? Guid.Empty,
            Status = result?.Status ?? "triggered",
            StartedAt = DateTime.UtcNow
        };
    }

    // ── Gateway execution & analytics endpoints (PR #7) ──

    public async Task<JobExecutionResponse> DryRunAsync(Guid jobId, JobExecutionRequest? request = null, CancellationToken ct = default)
    {
        var response = await _gatewayClient.PostAsJsonAsync($"api/jobs/{jobId}/dry-run", request ?? new JobExecutionRequest(), ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobExecutionResponse>(ct)
            ?? throw new InvalidOperationException("Dry-run job returned null");
    }

    public async Task<JobStatsResponse> GetStatsAsync(Guid jobId, CancellationToken ct = default)
    {
        var response = await _gatewayClient.GetAsync($"api/jobs/{jobId}/stats", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobStatsResponse>(ct)
            ?? throw new InvalidOperationException("Get stats returned null");
    }

    public async Task<IReadOnlyList<JobRunDto>> GetRunsAsync(Guid jobId, int limit = 50, CancellationToken ct = default)
    {
        var response = await _gatewayClient.GetAsync($"api/jobs/{jobId}/runs?limit={limit}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<JobRunDto>>(ct)
            ?? throw new InvalidOperationException("Get runs returned null");
    }

    /// <summary>Persisted timeline for a single run. See JobRunEventDto.</summary>
    public async Task<IReadOnlyList<JobRunEventDto>> GetRunEventsAsync(Guid jobId, Guid runId, CancellationToken ct = default)
    {
        var response = await _gatewayClient.GetAsync($"api/jobs/{jobId}/runs/{runId}/events", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return [];
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<JobRunEventDto>>(ct) ?? [];
    }

    /// <summary>Built-in job templates that can be used to seed a new job.</summary>
    public async Task<IReadOnlyList<JobTemplateDto>> ListTemplatesAsync(CancellationToken ct = default)
    {
        var response = await _gatewayClient.GetAsync("api/jobs/templates", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<JobTemplateDto>>(ct) ?? [];
    }

    public async Task<JobTemplateDto?> GetTemplateAsync(string id, CancellationToken ct = default)
    {
        var response = await _gatewayClient.GetAsync($"api/jobs/templates/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobTemplateDto>(ct);
    }

    // ── Agent Profiles ──

    public async Task<List<AgentProfileDto>> GetAgentProfilesAsync(CancellationToken ct = default)
    {
        var response = await _gatewayClient.GetAsync("api/agent-profiles?kind=Standard", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<AgentProfileDto>>(ct) ?? [];
    }

    private sealed record JobTriggerResponse(Guid RunId, Guid JobId, string Status);

    // ── Scheduler helpers ──

    public async Task<TranslateCronResult> TranslateCronAsync(string text, CancellationToken ct = default)
    {
        try
        {
            var response = await _gatewayClient.PostAsJsonAsync(
                "api/scheduler/translate-cron", new { text }, ct);

            if (response.IsSuccessStatusCode)
            {
                var ok = await response.Content.ReadFromJsonAsync<TranslateCronOk>(cancellationToken: ct);
                if (ok is not null && !string.IsNullOrWhiteSpace(ok.Cron))
                {
                    return new TranslateCronResult(true, ok.Cron, ok.Explanation, null);
                }
                return new TranslateCronResult(false, null, null, "Server returned empty cron expression.");
            }

            string? message = null;
            try
            {
                var err = await response.Content.ReadFromJsonAsync<TranslateCronErr>(cancellationToken: ct);
                message = err?.Error;
            }
            catch { /* not JSON */ }

            if (string.IsNullOrWhiteSpace(message))
            {
                message = $"HTTP {(int)response.StatusCode} — {response.ReasonPhrase}";
            }

            return new TranslateCronResult(false, null, null, message);
        }
        catch (Exception ex)
        {
            return new TranslateCronResult(false, null, null, ex.Message);
        }
    }

    private sealed record TranslateCronOk(string Cron, string? Explanation);
    private sealed record TranslateCronErr(string? Error);

    // ── Channel Configuration ──

    public async Task<List<JobChannelConfigDto>> GetChannelConfigsAsync(Guid jobId, CancellationToken ct = default)
    {
        var response = await _gatewayClient.GetAsync($"api/jobs/{jobId}/channels", ct);
        if (!response.IsSuccessStatusCode) return [];
        return await response.Content.ReadFromJsonAsync<List<JobChannelConfigDto>>(ct) ?? [];
    }

    public async Task SaveChannelConfigsAsync(Guid jobId, SaveJobChannelConfigRequest request, CancellationToken ct = default)
    {
        var response = await _gatewayClient.PostAsJsonAsync($"api/jobs/{jobId}/channels", request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> DeleteChannelConfigAsync(Guid jobId, string channelType, CancellationToken ct = default)
    {
        var response = await _gatewayClient.DeleteAsync($"api/jobs/{jobId}/channels/{channelType}", ct);
        return response.IsSuccessStatusCode;
    }
}

public sealed record AgentProfileDto(
    string Name,
    string? DisplayName,
    string? Provider,
    string? Model,
    string? Instructions,
    string[]? EnabledTools,
    double? Temperature,
    int? MaxTokens,
    bool IsDefault);

public sealed record TranslateCronResult(bool Success, string? Cron, string? Explanation, string? Error);

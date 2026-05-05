using System.Net;
using System.Net.Http.Json;
using OpenClawNet.Web.Models.Skills;

namespace OpenClawNet.Web.Services;

/// <summary>
/// Typed HttpClient for the K-3 skills gateway endpoints. Routes through the
/// Aspire <c>https+http://gateway</c> base address registered in
/// <c>Program.cs</c> via the named "gateway" HttpClient — same pattern as
/// <see cref="UserFolderClient"/>.
///
/// All methods either return a strongly-typed result OR throw
/// <see cref="SkillsClientException"/> carrying the structured
/// <see cref="SkillsProblem"/> so the UI can render a Bootstrap alert.
/// </summary>
public sealed class SkillsClient
{
    private readonly HttpClient _http;

    public SkillsClient(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <summary>GET /api/skills/snapshot — cheap pulse, polled every 5s by the hot-reload banner.</summary>
    public async Task<SkillsSnapshotDto> GetSnapshotAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("api/skills/snapshot", ct).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<SkillsSnapshotDto>(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Snapshot endpoint returned a null body.");
    }

    /// <summary>GET /api/skills — full skill list across all layers.</summary>
    public async Task<IReadOnlyList<SkillDto>> ListAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("api/skills", ct).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, ct).ConfigureAwait(false);
        var list = await response.Content.ReadFromJsonAsync<List<SkillDto>>(ct).ConfigureAwait(false);
        return list ?? [];
    }

    /// <summary>GET /api/skills/{name} — single skill detail (body + metadata).</summary>
    public async Task<SkillDto> GetAsync(string name, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(
            $"api/skills/{Uri.EscapeDataString(name)}", ct).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<SkillDto>(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Skill '{name}' returned a null body.");
    }

    /// <summary>POST /api/skills — create an Installed-layer skill (in-app authoring path).</summary>
    public async Task<SkillDto> CreateAsync(CreateSkillRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var response = await _http.PostAsJsonAsync("api/skills", request, ct).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<SkillDto>(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Create returned a null body.");
    }

    /// <summary>
    /// PUT /api/skills/{name}/enabled-for/{agentName} — toggle enabled.json for one agent.
    /// Per Q1, body is { "enabled": true|false }.
    /// </summary>
    public async Task SetEnabledAsync(string skillName, string agentName, bool enabled, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync(
            $"api/skills/{Uri.EscapeDataString(skillName)}/enabled-for/{Uri.EscapeDataString(agentName)}",
            new { enabled },
            ct).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, ct).ConfigureAwait(false);
    }

    /// <summary>DELETE /api/skills/{name} — Installed layer only (L-2: System is read-only).</summary>
    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync(
            $"api/skills/{Uri.EscapeDataString(name)}", ct).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, ct).ConfigureAwait(false);
    }

    /// <summary>GET /api/skills/changes-since/{snapshotId} — diff against a pinned snapshot.</summary>
    public async Task<SkillsChangesDto> GetChangesSinceAsync(string snapshotId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(
            $"api/skills/changes-since/{Uri.EscapeDataString(snapshotId)}", ct).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<SkillsChangesDto>(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("changes-since returned a null body.");
    }

    private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        SkillsProblem? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<SkillsProblem>(ct).ConfigureAwait(false);
        }
        catch
        {
            // Body wasn't JSON — fall through with null problem.
        }

        throw new SkillsClientException(response.StatusCode, problem);
    }
}

/// <summary>
/// Carries the structured 4xx / 5xx response from a skills API call. UI
/// consumers render <see cref="Reason"/> in alerts; <see cref="StatusCode"/>
/// distinguishes 400 (validation) from 404 (missing) from 409 (collision).
/// </summary>
public sealed class SkillsClientException : Exception
{
    public SkillsClientException(HttpStatusCode statusCode, SkillsProblem? problem)
        : base(BuildMessage(statusCode, problem))
    {
        StatusCode = statusCode;
        Problem = problem;
    }

    public HttpStatusCode StatusCode { get; }
    public SkillsProblem? Problem { get; }

    public string Reason => Problem?.Reason ?? StatusCode.ToString();

    private static string BuildMessage(HttpStatusCode statusCode, SkillsProblem? problem)
        => problem is null
            ? $"Skills request failed: HTTP {(int)statusCode} {statusCode}."
            : $"Skills request failed: {problem.Reason} (HTTP {(int)statusCode}).";
}

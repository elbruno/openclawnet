using Microsoft.EntityFrameworkCore;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Gateway.Endpoints;

/// <summary>
/// Global run search and query endpoints (cross-job).
/// </summary>
public static class RunsEndpoints
{
    public static void MapRunsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/runs").WithTags("Runs");

        group.MapGet("/search", async (
            string? status,
            DateTime? since,
            DateTime? until,
            Guid? jobId,
            int? limit,
            IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            
            var query = db.JobRuns.AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                var statusLower = status.ToLowerInvariant();
                query = query.Where(r => r.Status.ToLower() == statusLower);
            }

            if (since.HasValue)
                query = query.Where(r => r.StartedAt >= since.Value);

            if (until.HasValue)
                query = query.Where(r => r.StartedAt <= until.Value);

            if (jobId.HasValue)
                query = query.Where(r => r.JobId == jobId.Value);

            var pageSize = Math.Min(limit ?? 100, 500);

            var runs = await query
                .OrderByDescending(r => r.StartedAt)
                .Take(pageSize)
                .Include(r => r.Job)
                .Select(r => new GlobalJobRunDto
                {
                    Id = r.Id,
                    JobId = r.JobId,
                    JobName = r.Job!.Name,
                    Status = r.Status,
                    StartedAt = r.StartedAt,
                    CompletedAt = r.CompletedAt,
                    TokensUsed = r.TokensUsed,
                    Error = r.Error,
                    ExecutedByAgentProfile = r.ExecutedByAgentProfile
                })
                .ToListAsync();

            return Results.Ok(new GlobalRunsSearchResponse
            {
                Runs = runs,
                Count = runs.Count,
                Filters = new
                {
                    status,
                    since,
                    until,
                    jobId
                }
            });
        })
        .WithName("SearchRuns")
        .WithDescription("Search runs across all jobs with filters (status, date range, jobId). Default limit 100, max 500.");
    }
}

public sealed record GlobalJobRunDto
{
    public Guid Id { get; init; }
    public Guid JobId { get; init; }
    public required string JobName { get; init; }
    public required string Status { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int? TokensUsed { get; init; }
    public string? Error { get; init; }
    public string? ExecutedByAgentProfile { get; init; }
}

public sealed record GlobalRunsSearchResponse
{
    public List<GlobalJobRunDto> Runs { get; init; } = [];
    public int Count { get; init; }
    public object? Filters { get; init; }
}

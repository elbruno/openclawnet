using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenClawNet.Storage;

namespace OpenClawNet.Gateway.Endpoints;

public static class JobScheduleEndpoints
{
    public static void MapJobScheduleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/jobs").WithTags("Jobs");

        // GET /api/jobs/{jobId}/schedule — get the cron/interval expression for a job
        group.MapGet("/{jobId:guid}/schedule", async (
            Guid jobId,
            [FromServices] IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobId);

            if (job is null)
                return Results.NotFound();

            return Results.Ok(new JobScheduleDto(
                job.Id,
                job.Name,
                job.CronExpression,
                job.IsRecurring,
                job.StartAt,
                job.EndAt,
                job.TimeZone,
                job.NaturalLanguageSchedule,
                job.NextRunAt
            ));
        })
        .WithName("GetJobSchedule")
        .WithDescription("Get scheduling configuration for a job");

        // PUT /api/jobs/{jobId}/schedule — change a job's schedule without touching anything else
        group.MapPut("/{jobId:guid}/schedule", async (
            Guid jobId,
            [FromBody] UpdateJobScheduleRequest request,
            [FromServices] IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId);

            if (job is null)
                return Results.NotFound();

            job.CronExpression = request.CronExpression;
            job.IsRecurring = request.IsRecurring ?? job.IsRecurring;
            job.StartAt = request.StartAt;
            job.EndAt = request.EndAt;
            job.TimeZone = request.TimeZone;
            job.NaturalLanguageSchedule = request.NaturalLanguageSchedule;

            // NextRunAt calculation is done by the scheduler service automatically
            // We just save the updated schedule configuration

            await db.SaveChangesAsync();

            return Results.Ok(new JobScheduleDto(
                job.Id,
                job.Name,
                job.CronExpression,
                job.IsRecurring,
                job.StartAt,
                job.EndAt,
                job.TimeZone,
                job.NaturalLanguageSchedule,
                job.NextRunAt
            ));
        })
        .WithName("UpdateJobSchedule")
        .WithDescription("Update the schedule configuration for a job");

        // GET /api/jobs/{jobId}/next-run — when will this job next fire?
        group.MapGet("/{jobId:guid}/next-run", async (
            Guid jobId,
            [FromServices] IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobId);

            if (job is null)
                return Results.NotFound();

            string? error = null;
            if (string.IsNullOrWhiteSpace(job.CronExpression))
            {
                error = "Job has no cron expression";
            }
            else if (job.EndAt.HasValue && job.NextRunAt.HasValue && job.NextRunAt.Value > job.EndAt.Value)
            {
                error = "Job schedule has expired (past EndAt time)";
            }

            return Results.Ok(new JobNextRunDto(
                job.Id,
                job.Name,
                job.CronExpression,
                job.NextRunAt,
                error
            ));
        })
        .WithName("GetJobNextRun")
        .WithDescription("Get when a job will next fire (NextRunAt is calculated by scheduler service)");

        // GET /api/jobs/by-schedule — find all jobs matching a cron pattern (debugging)
        group.MapGet("/by-schedule", async (
            [FromQuery] string? expression,
            [FromServices] IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            if (string.IsNullOrWhiteSpace(expression))
                return Results.BadRequest("expression query parameter is required");

            await using var db = await dbFactory.CreateDbContextAsync();

            var jobs = await db.Jobs
                .AsNoTracking()
                .Where(j => j.CronExpression == expression)
                .Select(j => new JobScheduleSummaryDto(
                    j.Id,
                    j.Name,
                    j.CronExpression,
                    j.IsRecurring,
                    j.NextRunAt,
                    j.Status.ToString()
                ))
                .ToListAsync();

            return Results.Ok(new { expression, count = jobs.Count, jobs });
        })
        .WithName("GetJobsBySchedule")
        .WithDescription("Find all jobs with a specific cron expression (debugging tool)");
    }
}

public sealed record JobScheduleDto(
    Guid Id,
    string Name,
    string? CronExpression,
    bool IsRecurring,
    DateTime? StartAt,
    DateTime? EndAt,
    string? TimeZone,
    string? NaturalLanguageSchedule,
    DateTime? NextRunAt
);

public sealed record UpdateJobScheduleRequest
{
    public string? CronExpression { get; init; }
    public bool? IsRecurring { get; init; }
    public DateTime? StartAt { get; init; }
    public DateTime? EndAt { get; init; }
    public string? TimeZone { get; init; }
    public string? NaturalLanguageSchedule { get; init; }
}

public sealed record JobNextRunDto(
    Guid Id,
    string Name,
    string? CronExpression,
    DateTime? NextRunAt,
    string? Error
);

public sealed record JobScheduleSummaryDto(
    Guid Id,
    string Name,
    string? CronExpression,
    bool IsRecurring,
    DateTime? NextRunAt,
    string Status
);

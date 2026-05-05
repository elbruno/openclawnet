using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Services.Scheduler;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.UnitTests.Scheduler;

/// <summary>
/// Regression tests for the "stuck Running" bug. When the Scheduler process is
/// killed mid-run (Aspire restart, crash), the fire-and-forget Task.Run that
/// owns the run dies with it, leaving the JobRun row at status="running"
/// indefinitely. <see cref="SchedulerPollingService.ReclaimOrphanedRunsAsync"/>
/// is invoked once at startup to mark such rows as failed.
/// </summary>
public sealed class SchedulerOrphanReclaimTests
{
    private static IServiceScopeFactory CreateScopeFactory(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<OpenClawDbContext>(o => o.UseInMemoryDatabase(dbName));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public async Task ReclaimOrphanedRunsAsync_MarksRunningRows_AsFailed()
    {
        var dbName = $"orphan-reclaim-{Guid.NewGuid()}";
        var scopeFactory = CreateScopeFactory(dbName);

        var jobId = Guid.NewGuid();
        using (var scope = scopeFactory.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            db.Jobs.Add(new ScheduledJob { Id = jobId, Name = "stuck", Prompt = "p" });
            db.JobRuns.AddRange(
                new JobRun { JobId = jobId, Status = "running", StartedAt = DateTime.UtcNow.AddMinutes(-30) },
                new JobRun { JobId = jobId, Status = "running", StartedAt = DateTime.UtcNow.AddMinutes(-1) },
                new JobRun { JobId = jobId, Status = "completed", StartedAt = DateTime.UtcNow.AddHours(-2), CompletedAt = DateTime.UtcNow.AddHours(-2) });
            await db.SaveChangesAsync();
        }

        await SchedulerPollingService.ReclaimOrphanedRunsAsync(scopeFactory, NullLogger.Instance, default);

        using (var scope = scopeFactory.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            var runs = await db.JobRuns.OrderBy(r => r.StartedAt).ToListAsync();
            runs.Should().HaveCount(3);
            runs[0].Status.Should().Be("completed");
            runs[1].Status.Should().Be("failed", "previously stuck 'running' row should be reclaimed");
            runs[1].Error.Should().Contain("Scheduler restarted");
            runs[1].CompletedAt.Should().NotBeNull();
            runs[2].Status.Should().Be("failed");
            runs[2].CompletedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task ReclaimOrphanedRunsAsync_NoRunningRows_IsNoOp()
    {
        var scopeFactory = CreateScopeFactory($"orphan-noop-{Guid.NewGuid()}");

        // Should not throw, should not write anything
        await SchedulerPollingService.ReclaimOrphanedRunsAsync(scopeFactory, NullLogger.Instance, default);

        using var scope = scopeFactory.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        (await db.JobRuns.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ReclaimOrphanedRunsAsync_DoesNotTouchTerminalRows()
    {
        var dbName = $"orphan-terminal-{Guid.NewGuid()}";
        var scopeFactory = CreateScopeFactory(dbName);

        var jobId = Guid.NewGuid();
        var completedAt = DateTime.UtcNow.AddMinutes(-5);
        using (var scope = scopeFactory.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            db.Jobs.Add(new ScheduledJob { Id = jobId, Name = "j", Prompt = "p" });
            db.JobRuns.AddRange(
                new JobRun { JobId = jobId, Status = "completed", Result = "ok", CompletedAt = completedAt },
                new JobRun { JobId = jobId, Status = "failed", Error = "boom", CompletedAt = completedAt });
            await db.SaveChangesAsync();
        }

        await SchedulerPollingService.ReclaimOrphanedRunsAsync(scopeFactory, NullLogger.Instance, default);

        using (var scope = scopeFactory.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            var runs = await db.JobRuns.ToListAsync();
            runs.Should().OnlyContain(r => r.Status == "completed" || (r.Status == "failed" && r.Error == "boom"));
            runs.Should().OnlyContain(r => r.CompletedAt == completedAt);
        }
    }
}

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.UnitTests.Scheduler;

public sealed class ArtifactRetentionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OpenClawDbContext> _options;
    private readonly string _testArtifactRoot;

    public ArtifactRetentionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<OpenClawDbContext>()
            .UseSqlite(_connection)
            .Options;

        _testArtifactRoot = Path.Combine(Path.GetTempPath(), $"openclaw-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testArtifactRoot);
    }

    public void Dispose()
    {
        _connection?.Dispose();
        if (Directory.Exists(_testArtifactRoot))
            Directory.Delete(_testArtifactRoot, recursive: true);
    }

    [Fact]
    public async Task RetentionPolicy_KeepsLast100RunsPerJob()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);

        for (int i = 0; i < 120; i++)
        {
            var run = new JobRun
            {
                JobId = job.Id,
                Status = "completed",
                StartedAt = DateTime.UtcNow.AddHours(-120 + i)
            };
            db.JobRuns.Add(run);
            await db.SaveChangesAsync();

            await storageService.CreateArtifactAsync(
                job.Id, run.Id, JobRunArtifactKind.Text, $"Artifact {i}", $"content {i}", 0);
        }

        var deleted = await storageService.CleanupOldArtifactsAsync(maxRunsPerJob: 100, maxAgeDays: 9999);

        await using var db2 = new OpenClawDbContext(_options);
        var remainingArtifacts = await db2.JobRunArtifacts.Where(a => a.JobId == job.Id).CountAsync();
        remainingArtifacts.Should().Be(100);
        deleted.Should().Be(20);
    }

    [Fact]
    public async Task RetentionPolicy_DeletesArtifactsOlderThan30Days()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);

        for (int i = 0; i < 3; i++)
        {
            var oldRun = new JobRun
            {
                JobId = job.Id,
                Status = "completed",
                StartedAt = DateTime.UtcNow.AddDays(-40 - i)
            };
            db.JobRuns.Add(oldRun);
            await db.SaveChangesAsync();
            await storageService.CreateArtifactAsync(
                job.Id, oldRun.Id, JobRunArtifactKind.Text, $"Old {i}", $"old content {i}", 0);
        }

        for (int i = 0; i < 5; i++)
        {
            var recentRun = new JobRun
            {
                JobId = job.Id,
                Status = "completed",
                StartedAt = DateTime.UtcNow.AddDays(-10 - i)
            };
            db.JobRuns.Add(recentRun);
            await db.SaveChangesAsync();
            await storageService.CreateArtifactAsync(
                job.Id, recentRun.Id, JobRunArtifactKind.Text, $"Recent {i}", $"recent content {i}", 0);
        }

        var deleted = await storageService.CleanupOldArtifactsAsync(maxRunsPerJob: 9999, maxAgeDays: 30);

        await using var db2 = new OpenClawDbContext(_options);
        var remainingArtifacts = await db2.JobRunArtifacts.Where(a => a.JobId == job.Id).ToListAsync();
        remainingArtifacts.Should().HaveCount(5);
        deleted.Should().Be(3);
    }

    [Fact]
    public async Task RetentionPolicy_AppliesBothRulesOnSingleJob()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);

        for (int i = 0; i < 5; i++)
        {
            var oldRun = new JobRun
            {
                JobId = job.Id,
                Status = "completed",
                StartedAt = DateTime.UtcNow.AddDays(-40 - i)
            };
            db.JobRuns.Add(oldRun);
            await db.SaveChangesAsync();
            await storageService.CreateArtifactAsync(
                job.Id, oldRun.Id, JobRunArtifactKind.Text, $"Old {i}", $"old content {i}", 0);
        }

        for (int i = 0; i < 10; i++)
        {
            var recentRun = new JobRun
            {
                JobId = job.Id,
                Status = "completed",
                StartedAt = DateTime.UtcNow.AddDays(-10 - i)
            };
            db.JobRuns.Add(recentRun);
            await db.SaveChangesAsync();
            await storageService.CreateArtifactAsync(
                job.Id, recentRun.Id, JobRunArtifactKind.Text, $"Recent {i}", $"recent content {i}", 0);
        }

        var deleted = await storageService.CleanupOldArtifactsAsync(maxRunsPerJob: 7, maxAgeDays: 30);

        await using var db2 = new OpenClawDbContext(_options);
        var remainingArtifacts = await db2.JobRunArtifacts.Where(a => a.JobId == job.Id).CountAsync();
        remainingArtifacts.Should().Be(7);
        deleted.Should().Be(8);
    }

    [Fact]
    public async Task RetentionPolicy_HandlesMultipleJobsSeparately()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job1 = new ScheduledJob { Name = "Job 1", Prompt = "Test", Status = JobStatus.Active };
        var job2 = new ScheduledJob { Name = "Job 2", Prompt = "Test", Status = JobStatus.Active };
        db.Jobs.AddRange(job1, job2);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);

        for (int i = 0; i < 15; i++)
        {
            var run = new JobRun
            {
                JobId = job1.Id,
                Status = "completed",
                StartedAt = DateTime.UtcNow.AddHours(-15 + i)
            };
            db.JobRuns.Add(run);
            await db.SaveChangesAsync();
            await storageService.CreateArtifactAsync(
                job1.Id, run.Id, JobRunArtifactKind.Text, $"J1 Artifact {i}", $"content {i}", 0);
        }

        for (int i = 0; i < 8; i++)
        {
            var run = new JobRun
            {
                JobId = job2.Id,
                Status = "completed",
                StartedAt = DateTime.UtcNow.AddHours(-8 + i)
            };
            db.JobRuns.Add(run);
            await db.SaveChangesAsync();
            await storageService.CreateArtifactAsync(
                job2.Id, run.Id, JobRunArtifactKind.Text, $"J2 Artifact {i}", $"content {i}", 0);
        }

        var deleted = await storageService.CleanupOldArtifactsAsync(maxRunsPerJob: 10, maxAgeDays: 9999);

        await using var db2 = new OpenClawDbContext(_options);
        var job1Artifacts = await db2.JobRunArtifacts.Where(a => a.JobId == job1.Id).CountAsync();
        var job2Artifacts = await db2.JobRunArtifacts.Where(a => a.JobId == job2.Id).CountAsync();

        job1Artifacts.Should().Be(10);
        job2Artifacts.Should().Be(8);
        deleted.Should().Be(5);
    }

    [Fact]
    public async Task RetentionPolicy_DeletesDiskFiles_WhenRowDeleted()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);

        var run = new JobRun
        {
            JobId = job.Id,
            Status = "completed",
            StartedAt = DateTime.UtcNow.AddDays(-40)
        };
        db.JobRuns.Add(run);
        await db.SaveChangesAsync();

        var largeContent = new string('x', 100_000);
        var artifact = await storageService.CreateArtifactAsync(
            job.Id, run.Id, JobRunArtifactKind.Text, "Large", largeContent, 0);

        await using var db2 = new OpenClawDbContext(_options);
        var retrievedArtifact = await db2.JobRunArtifacts.FindAsync(artifact.Id);
        retrievedArtifact!.ContentPath.Should().NotBeNull();
        var fullPath = Path.Combine(_testArtifactRoot, "artifacts", retrievedArtifact.ContentPath!);
        File.Exists(fullPath).Should().BeTrue("disk file should exist before cleanup");

        var deleted = await storageService.CleanupOldArtifactsAsync(maxRunsPerJob: 9999, maxAgeDays: 30);

        deleted.Should().Be(1);
        await using var db3 = new OpenClawDbContext(_options);
        var artifactExists = await db3.JobRunArtifacts.AnyAsync(a => a.Id == artifact.Id);
        artifactExists.Should().BeFalse("artifact row should be deleted");
        File.Exists(fullPath).Should().BeFalse("disk file should be deleted");
    }

    private ArtifactStorageService CreateArtifactStorageService(OpenClawDbContext db)
    {
        var dbFactory = new Mock<IDbContextFactory<OpenClawDbContext>>();
        dbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new OpenClawDbContext(_options));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:RootPath"] = _testArtifactRoot
            })
            .Build();

        var logger = new Mock<ILogger<ArtifactStorageService>>();

        return new ArtifactStorageService(dbFactory.Object, config, logger.Object);
    }
}

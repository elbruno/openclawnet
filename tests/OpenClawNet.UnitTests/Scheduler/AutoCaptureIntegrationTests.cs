using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.UnitTests.Scheduler;

public sealed class AutoCaptureIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OpenClawDbContext> _options;
    private readonly string _testArtifactRoot;

    public AutoCaptureIntegrationTests()
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
    public async Task AutoCapture_MarkdownResult_CreatesMarkdownArtifact()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        var run = new JobRun
        {
            JobId = job.Id,
            Status = "completed",
            StartedAt = DateTime.UtcNow,
            Result = "# Test Result\n\nThis is markdown content with **bold** text."
        };
        db.Jobs.Add(job);
        db.JobRuns.Add(run);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);

        var artifact = await storageService.CreateArtifactFromJobRunAsync(run);

        artifact.Should().NotBeNull();
        artifact.ArtifactType.Should().Be(JobRunArtifactKind.Markdown);
        artifact.JobRunId.Should().Be(run.Id);
        artifact.JobId.Should().Be(job.Id);

        var content = await storageService.GetArtifactContentAsync(artifact);
        content.Should().Contain("# Test Result");
        content.Should().Contain("**bold**");
    }

    [Fact]
    public async Task AutoCapture_PlainTextResult_CreatesTextArtifact()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        var run = new JobRun
        {
            JobId = job.Id,
            Status = "completed",
            StartedAt = DateTime.UtcNow,
            Result = "Simple plain text result without any formatting"
        };
        db.Jobs.Add(job);
        db.JobRuns.Add(run);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);

        var artifact = await storageService.CreateArtifactFromJobRunAsync(run);

        artifact.ArtifactType.Should().Be(JobRunArtifactKind.Text);
        var content = await storageService.GetArtifactContentAsync(artifact);
        content.Should().Be("Simple plain text result without any formatting");
    }

    [Fact]
    public async Task AutoCapture_JsonResult_CreatesJsonArtifact()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        var run = new JobRun
        {
            JobId = job.Id,
            Status = "completed",
            StartedAt = DateTime.UtcNow,
            Result = """{"status": "success", "data": [1, 2, 3]}"""
        };
        db.Jobs.Add(job);
        db.JobRuns.Add(run);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);

        var artifact = await storageService.CreateArtifactFromJobRunAsync(run);

        artifact.ArtifactType.Should().Be(JobRunArtifactKind.Json);
        var content = await storageService.GetArtifactContentAsync(artifact);
        content.Should().Contain("\"status\"");
        content.Should().Contain("\"data\"");
    }

    [Fact]
    public async Task AutoCapture_ErrorResult_CreatesErrorArtifact()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        var run = new JobRun
        {
            JobId = job.Id,
            Status = "failed",
            StartedAt = DateTime.UtcNow,
            Error = "System.Exception: Something went wrong at line 42"
        };
        db.Jobs.Add(job);
        db.JobRuns.Add(run);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);

        var artifact = await storageService.CreateArtifactFromJobRunAsync(run);

        artifact.ArtifactType.Should().Be(JobRunArtifactKind.Error);
        artifact.Title.Should().Be("Execution Error");
        var content = await storageService.GetArtifactContentAsync(artifact);
        content.Should().Contain("System.Exception");
        content.Should().Contain("line 42");
    }

    [Fact]
    public async Task AutoCapture_MultipleRuns_CreatesSeparateArtifacts()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        var run1 = new JobRun
        {
            JobId = job.Id,
            Status = "completed",
            StartedAt = DateTime.UtcNow.AddHours(-1),
            Result = "First run result"
        };
        var run2 = new JobRun
        {
            JobId = job.Id,
            Status = "completed",
            StartedAt = DateTime.UtcNow,
            Result = "Second run result"
        };
        db.Jobs.Add(job);
        db.JobRuns.AddRange(run1, run2);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);

        var artifact1 = await storageService.CreateArtifactFromJobRunAsync(run1);
        var artifact2 = await storageService.CreateArtifactFromJobRunAsync(run2);

        artifact1.Id.Should().NotBe(artifact2.Id);
        artifact1.JobRunId.Should().Be(run1.Id);
        artifact2.JobRunId.Should().Be(run2.Id);

        await using var db2 = new OpenClawDbContext(_options);
        var allArtifacts = await db2.JobRunArtifacts.Where(a => a.JobId == job.Id).ToListAsync();
        allArtifacts.Should().HaveCount(2);
    }

    [Fact]
    public async Task AutoCapture_LargeResult_UsesContentPath()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        var largeResult = new string('A', 100_000);
        var run = new JobRun
        {
            JobId = job.Id,
            Status = "completed",
            StartedAt = DateTime.UtcNow,
            Result = largeResult
        };
        db.Jobs.Add(job);
        db.JobRuns.Add(run);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);

        var artifact = await storageService.CreateArtifactFromJobRunAsync(run);

        artifact.ContentInline.Should().BeNull();
        artifact.ContentPath.Should().NotBeNullOrEmpty();
        artifact.ContentSizeBytes.Should().Be(100_000);

        var fullPath = Path.Combine(_testArtifactRoot, "artifacts", artifact.ContentPath!);
        File.Exists(fullPath).Should().BeTrue();

        var retrievedContent = await storageService.GetArtifactContentAsync(artifact);
        retrievedContent.Should().Be(largeResult);
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

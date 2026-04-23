using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.UnitTests.Storage;

public sealed class JobRunArtifactTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OpenClawDbContext> _options;
    private readonly string _testArtifactRoot;

    public JobRunArtifactTests()
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
    public async Task InlineContent_SmallerThan64KB_StoredInContentInline()
    {
        // Arrange
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        var run = new JobRun { JobId = job.Id, Status = "completed", StartedAt = DateTime.UtcNow };
        db.Jobs.Add(job);
        db.JobRuns.Add(run);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);
        var content = new string('a', 1000); // 1KB

        // Act
        var artifact = await storageService.CreateArtifactAsync(
            job.Id, run.Id, JobRunArtifactKind.Text, "Small Content", content, 0);

        // Assert
        await using var db2 = new OpenClawDbContext(_options);
        var retrieved = await db2.JobRunArtifacts.FindAsync(artifact.Id);
        retrieved.Should().NotBeNull();
        retrieved!.ContentInline.Should().Be(content);
        retrieved.ContentPath.Should().BeNull();
        retrieved.ContentSizeBytes.Should().Be(1000);
    }

    [Fact]
    public async Task InlineContent_ExactlyAt64KB_StoredInContentInline()
    {
        // Arrange
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        var run = new JobRun { JobId = job.Id, Status = "completed", StartedAt = DateTime.UtcNow };
        db.Jobs.Add(job);
        db.JobRuns.Add(run);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);
        var content = new string('b', 65536); // Exactly 64KB

        // Act
        var artifact = await storageService.CreateArtifactAsync(
            job.Id, run.Id, JobRunArtifactKind.Text, "Boundary Content", content, 0);

        // Assert
        await using var db2 = new OpenClawDbContext(_options);
        var retrieved = await db2.JobRunArtifacts.FindAsync(artifact.Id);
        retrieved.Should().NotBeNull();
        retrieved!.ContentInline.Should().Be(content);
        retrieved.ContentPath.Should().BeNull();
        retrieved.ContentSizeBytes.Should().Be(65536);
    }

    [Fact]
    public async Task LargeContent_GreaterThan64KB_UsesContentPath()
    {
        // Arrange
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        var run = new JobRun { JobId = job.Id, Status = "completed", StartedAt = DateTime.UtcNow };
        db.Jobs.Add(job);
        db.JobRuns.Add(run);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);
        var content = new string('c', 100_000); // ~100KB

        // Act
        var artifact = await storageService.CreateArtifactAsync(
            job.Id, run.Id, JobRunArtifactKind.Text, "Large Content", content, 0);

        // Assert
        await using var db2 = new OpenClawDbContext(_options);
        var retrieved = await db2.JobRunArtifacts.FindAsync(artifact.Id);
        retrieved.Should().NotBeNull();
        retrieved!.ContentInline.Should().BeNull();
        retrieved.ContentPath.Should().NotBeNullOrEmpty();
        retrieved.ContentSizeBytes.Should().Be(100_000);

        // Verify disk file exists and content is correct
        var fullPath = Path.Combine(_testArtifactRoot, "artifacts", retrieved.ContentPath!);
        File.Exists(fullPath).Should().BeTrue();
        var diskContent = await File.ReadAllTextAsync(fullPath);
        diskContent.Should().Be(content);
    }

    [Fact]
    public async Task DiskPath_Format_PreventsPathTraversal()
    {
        // Arrange
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        var run = new JobRun { JobId = job.Id, Status = "completed", StartedAt = DateTime.UtcNow };
        db.Jobs.Add(job);
        db.JobRuns.Add(run);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);
        var content = new string('d', 100_000);

        // Act
        var artifact = await storageService.CreateArtifactAsync(
            job.Id, run.Id, JobRunArtifactKind.Text, "Traversal Test", content, 0);

        // Assert
        await using var db2 = new OpenClawDbContext(_options);
        var retrieved = await db2.JobRunArtifacts.FindAsync(artifact.Id);
        retrieved!.ContentPath.Should().NotContain("..");
        retrieved.ContentPath.Should().StartWith(job.Id.ToString("N"));
        retrieved.ContentPath.Should().Contain(run.Id.ToString("N"));
    }

    [Theory]
    [InlineData(JobRunArtifactKind.Markdown)]
    [InlineData(JobRunArtifactKind.Json)]
    [InlineData(JobRunArtifactKind.Text)]
    [InlineData(JobRunArtifactKind.File)]
    [InlineData(JobRunArtifactKind.Link)]
    [InlineData(JobRunArtifactKind.Error)]
    public async Task AllArtifactKindValues_RoundTrip(JobRunArtifactKind kind)
    {
        // Arrange
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        var run = new JobRun { JobId = job.Id, Status = "completed", StartedAt = DateTime.UtcNow };
        db.Jobs.Add(job);
        db.JobRuns.Add(run);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);

        // Act
        var artifact = await storageService.CreateArtifactAsync(
            job.Id, run.Id, kind, $"Test {kind}", "test content", 0);

        // Assert
        await using var db2 = new OpenClawDbContext(_options);
        var retrieved = await db2.JobRunArtifacts.FindAsync(artifact.Id);
        retrieved.Should().NotBeNull();
        retrieved!.ArtifactType.Should().Be(kind);
    }

    [Fact]
    public async Task CascadeDelete_DeletingJobRun_DeletesArtifacts()
    {
        // Arrange
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        var run = new JobRun { JobId = job.Id, Status = "completed", StartedAt = DateTime.UtcNow };
        db.Jobs.Add(job);
        db.JobRuns.Add(run);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);
        var artifact1 = await storageService.CreateArtifactAsync(
            job.Id, run.Id, JobRunArtifactKind.Text, "Artifact 1", "content1", 0);
        var artifact2 = await storageService.CreateArtifactAsync(
            job.Id, run.Id, JobRunArtifactKind.Text, "Artifact 2", "content2", 1);

        // Act - delete the job run
        await using var db2 = new OpenClawDbContext(_options);
        var runToDelete = await db2.JobRuns.FindAsync(run.Id);
        db2.JobRuns.Remove(runToDelete!);
        await db2.SaveChangesAsync();

        // Assert - artifacts should be cascade-deleted
        await using var db3 = new OpenClawDbContext(_options);
        var remainingArtifacts = await db3.JobRunArtifacts.Where(a => a.JobRunId == run.Id).ToListAsync();
        remainingArtifacts.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryByJobId_ReturnsArtifacts_InReverseChronologicalOrder()
    {
        // Arrange
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        var run1 = new JobRun { JobId = job.Id, Status = "completed", StartedAt = DateTime.UtcNow.AddHours(-2) };
        var run2 = new JobRun { JobId = job.Id, Status = "completed", StartedAt = DateTime.UtcNow.AddHours(-1) };
        var run3 = new JobRun { JobId = job.Id, Status = "completed", StartedAt = DateTime.UtcNow };
        db.Jobs.Add(job);
        db.JobRuns.AddRange(run1, run2, run3);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);
        var artifact1 = await storageService.CreateArtifactAsync(job.Id, run1.Id, JobRunArtifactKind.Text, "Old", "content", 0);
        await Task.Delay(10); // Ensure different timestamps
        var artifact2 = await storageService.CreateArtifactAsync(job.Id, run2.Id, JobRunArtifactKind.Text, "Middle", "content", 0);
        await Task.Delay(10);
        var artifact3 = await storageService.CreateArtifactAsync(job.Id, run3.Id, JobRunArtifactKind.Text, "New", "content", 0);

        // Act
        await using var db2 = new OpenClawDbContext(_options);
        var artifacts = await db2.JobRunArtifacts
            .Where(a => a.JobId == job.Id)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        // Assert
        artifacts.Should().HaveCount(3);
        artifacts[0].Title.Should().Be("New");
        artifacts[1].Title.Should().Be("Middle");
        artifacts[2].Title.Should().Be("Old");
    }

    [Fact]
    public async Task SequenceOrdering_WithinRun_PreservesOrder()
    {
        // Arrange
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        var run = new JobRun { JobId = job.Id, Status = "completed", StartedAt = DateTime.UtcNow };
        db.Jobs.Add(job);
        db.JobRuns.Add(run);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);

        // Act - create artifacts with explicit sequence
        var artifact1 = await storageService.CreateArtifactAsync(job.Id, run.Id, JobRunArtifactKind.Text, "First", "1", 0);
        var artifact2 = await storageService.CreateArtifactAsync(job.Id, run.Id, JobRunArtifactKind.Text, "Second", "2", 1);
        var artifact3 = await storageService.CreateArtifactAsync(job.Id, run.Id, JobRunArtifactKind.Text, "Third", "3", 2);

        // Assert
        await using var db2 = new OpenClawDbContext(_options);
        var artifacts = await db2.JobRunArtifacts
            .Where(a => a.JobRunId == run.Id)
            .OrderBy(a => a.Sequence)
            .ToListAsync();

        artifacts.Should().HaveCount(3);
        artifacts[0].Sequence.Should().Be(0);
        artifacts[0].Title.Should().Be("First");
        artifacts[1].Sequence.Should().Be(1);
        artifacts[1].Title.Should().Be("Second");
        artifacts[2].Sequence.Should().Be(2);
        artifacts[2].Title.Should().Be("Third");
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

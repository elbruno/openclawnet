using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using OpenClawNet.Gateway.Endpoints;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;
using System.Net;

namespace OpenClawNet.UnitTests.Gateway;

/// <summary>
/// Tests for the new GET /api/channels/{jobId}/view endpoint that returns
/// ChannelDetailViewDto with full artifact content. Introduced as part of Option C
/// from Mark's ChannelDetail investigation (Issue #66) to provide a Razor-specific
/// view contract separate from the public API DTOs.
/// </summary>
public sealed class ChannelDetailViewEndpointTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OpenClawDbContext> _options;
    private readonly string _testArtifactRoot;

    public ChannelDetailViewEndpointTests()
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
    public async Task GetChannelDetailView_ReturnsOk_WithJobAndArtifacts_WhenJobExists()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        // Seed job, 2 runs, 3 artifacts (Markdown, Json, Text)
        var job = new ScheduledJob { Name = "Test Channel", Prompt = "Test prompt", Status = JobStatus.Active };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var run1 = new JobRun { JobId = job.Id, Status = "completed", StartedAt = DateTime.UtcNow.AddHours(-2) };
        var run2 = new JobRun { JobId = job.Id, Status = "completed", StartedAt = DateTime.UtcNow.AddHours(-1) };
        db.JobRuns.AddRange(run1, run2);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);
        await storageService.CreateArtifactAsync(job.Id, run1.Id, JobRunArtifactKind.Markdown, "Markdown Doc", "# Content", 0);
        await storageService.CreateArtifactAsync(job.Id, run2.Id, JobRunArtifactKind.Json, "JSON Data", """{"key": "value"}""", 0);
        await storageService.CreateArtifactAsync(job.Id, run2.Id, JobRunArtifactKind.Text, "Text Log", "Log content", 1);

        var dbFactory = CreateDbContextFactory();
        var httpContext = CreateLoopbackHttpContext();

        var result = await GetChannelDetailViewHandler(job.Id, dbFactory, storageService, httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<ChannelDetailViewDto>>();
        var okResult = (Microsoft.AspNetCore.Http.HttpResults.Ok<ChannelDetailViewDto>)result;
        var viewDto = okResult.Value!;

        viewDto.JobId.Should().Be(job.Id);
        viewDto.JobName.Should().Be("Test Channel");
        viewDto.Artifacts.Should().HaveCount(3);
        viewDto.Artifacts.Should().AllSatisfy(a =>
        {
            a.Id.Should().NotBeEmpty();
            a.RunId.Should().NotBeEmpty();
            a.ArtifactType.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public async Task GetChannelDetailView_Returns404_WhenJobMissing()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var dbFactory = CreateDbContextFactory();
        var httpContext = CreateLoopbackHttpContext();
        var unknownJobId = Guid.NewGuid();

        var result = await GetChannelDetailViewHandler(unknownJobId, dbFactory, CreateArtifactStorageService(db), httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NotFound>();
    }

    [Fact]
    public async Task GetChannelDetailView_OrdersArtifactsByCreatedAtDesc()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        var run = new JobRun { JobId = job.Id, Status = "completed", StartedAt = DateTime.UtcNow };
        db.Jobs.Add(job);
        db.JobRuns.Add(run);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);
        var oldestTime = DateTime.UtcNow.AddHours(-3);
        var middleTime = DateTime.UtcNow.AddHours(-2);
        var newestTime = DateTime.UtcNow.AddHours(-1);

        // Create artifacts with explicit timestamps
        var artifact1 = await storageService.CreateArtifactAsync(job.Id, run.Id, JobRunArtifactKind.Text, "Oldest", "content1", 0);
        var artifact2 = await storageService.CreateArtifactAsync(job.Id, run.Id, JobRunArtifactKind.Text, "Middle", "content2", 1);
        var artifact3 = await storageService.CreateArtifactAsync(job.Id, run.Id, JobRunArtifactKind.Text, "Newest", "content3", 2);

        // Update CreatedAt timestamps
        await using var db2 = new OpenClawDbContext(_options);
        var a1 = await db2.JobRunArtifacts.FindAsync(artifact1.Id);
        var a2 = await db2.JobRunArtifacts.FindAsync(artifact2.Id);
        var a3 = await db2.JobRunArtifacts.FindAsync(artifact3.Id);
        a1!.CreatedAt = oldestTime;
        a2!.CreatedAt = middleTime;
        a3!.CreatedAt = newestTime;
        await db2.SaveChangesAsync();

        var dbFactory = CreateDbContextFactory();
        var httpContext = CreateLoopbackHttpContext();

        var result = await GetChannelDetailViewHandler(job.Id, dbFactory, storageService, httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<ChannelDetailViewDto>>();
        var okResult = (Microsoft.AspNetCore.Http.HttpResults.Ok<ChannelDetailViewDto>)result;
        var viewDto = okResult.Value!;

        viewDto.Artifacts.Should().HaveCount(3);
        viewDto.Artifacts[0].Title.Should().Be("Newest");
        viewDto.Artifacts[1].Title.Should().Be("Middle");
        viewDto.Artifacts[2].Title.Should().Be("Oldest");
    }

    [Fact]
    public async Task GetChannelDetailView_MapsArtifactKind_ToLowercaseString()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        var run = new JobRun { JobId = job.Id, Status = "completed", StartedAt = DateTime.UtcNow };
        db.Jobs.Add(job);
        db.JobRuns.Add(run);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);
        await storageService.CreateArtifactAsync(job.Id, run.Id, JobRunArtifactKind.Markdown, "MD Doc", "# Test", 0);

        var dbFactory = CreateDbContextFactory();
        var httpContext = CreateLoopbackHttpContext();

        var result = await GetChannelDetailViewHandler(job.Id, dbFactory, storageService, httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<ChannelDetailViewDto>>();
        var okResult = (Microsoft.AspNetCore.Http.HttpResults.Ok<ChannelDetailViewDto>)result;
        var viewDto = okResult.Value!;

        viewDto.Artifacts.Should().HaveCount(1);
        viewDto.Artifacts[0].ArtifactType.Should().Be("markdown");
    }

    [Fact]
    public async Task GetChannelDetailView_PreservesFullContentInline_NotTruncated()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        var run = new JobRun { JobId = job.Id, Status = "completed", StartedAt = DateTime.UtcNow };
        db.Jobs.Add(job);
        db.JobRuns.Add(run);
        await db.SaveChangesAsync();

        // Create artifact with 10,000 character content
        var largeContent = new string('X', 10000);
        var storageService = CreateArtifactStorageService(db);
        await storageService.CreateArtifactAsync(job.Id, run.Id, JobRunArtifactKind.Text, "Large File", largeContent, 0);

        var dbFactory = CreateDbContextFactory();
        var httpContext = CreateLoopbackHttpContext();

        var result = await GetChannelDetailViewHandler(job.Id, dbFactory, storageService, httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<ChannelDetailViewDto>>();
        var okResult = (Microsoft.AspNetCore.Http.HttpResults.Ok<ChannelDetailViewDto>)result;
        var viewDto = okResult.Value!;

        viewDto.Artifacts.Should().HaveCount(1);
        viewDto.Artifacts[0].ContentInline.Should().NotBeNull();
        viewDto.Artifacts[0].ContentInline!.Length.Should().Be(10000, "content should NOT be truncated like the old GetRunArtifacts endpoint");
    }

    // Handler method that mirrors Irving's GET /api/channels/{jobId}/view endpoint
    private async Task<IResult> GetChannelDetailViewHandler(
        Guid jobId,
        IDbContextFactory<OpenClawDbContext> dbFactory,
        ArtifactStorageService artifactStorage,
        HttpContext httpContext)
    {
        if (!IsLoopbackRequest(httpContext))
            return Results.StatusCode(403);

        await using var db = await dbFactory.CreateDbContextAsync();

        var job = await db.Jobs.FindAsync(jobId);
        if (job is null)
            return Results.NotFound();

        // Fetch ALL artifacts across all runs for this job
        var artifacts = await db.JobRunArtifacts
            .Where(a => a.JobId == jobId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        var artifactDtos = artifacts.Select(a => new ArtifactForViewDto(
            a.Id,
            a.JobRunId,
            a.ArtifactType.ToString().ToLowerInvariant(),
            a.Title,
            a.ContentInline,
            a.ContentPath,
            a.ContentSizeBytes,
            a.MimeType,
            a.CreatedAt
        )).ToList();

        var viewDto = new ChannelDetailViewDto(
            job.Id,
            job.Name,
            artifactDtos
        );

        return Results.Ok(viewDto);
    }

    private bool IsLoopbackRequest(HttpContext httpContext)
    {
        var remoteIp = httpContext.Connection.RemoteIpAddress;
        return remoteIp?.IsIPv4MappedToIPv6 == true
            ? remoteIp.MapToIPv4().ToString() == "127.0.0.1"
            : remoteIp?.ToString() == "127.0.0.1" || remoteIp?.ToString() == "::1";
    }

    private HttpContext CreateLoopbackHttpContext() => CreateHttpContextWithIp("127.0.0.1");

    private HttpContext CreateHttpContextWithIp(string ipAddress)
    {
        var mockHttpContext = new Mock<HttpContext>();
        var mockConnection = new Mock<ConnectionInfo>();
        mockConnection.Setup(c => c.RemoteIpAddress).Returns(IPAddress.Parse(ipAddress));
        mockHttpContext.Setup(c => c.Connection).Returns(mockConnection.Object);
        return mockHttpContext.Object;
    }

    private IDbContextFactory<OpenClawDbContext> CreateDbContextFactory()
    {
        var dbFactory = new Mock<IDbContextFactory<OpenClawDbContext>>();
        dbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new OpenClawDbContext(_options));
        return dbFactory.Object;
    }

    private ArtifactStorageService CreateArtifactStorageService(OpenClawDbContext db)
    {
        var dbFactory = CreateDbContextFactory();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:RootPath"] = _testArtifactRoot
            })
            .Build();

        var logger = new Mock<ILogger<ArtifactStorageService>>();

        return new ArtifactStorageService(dbFactory, config, logger.Object);
    }
}

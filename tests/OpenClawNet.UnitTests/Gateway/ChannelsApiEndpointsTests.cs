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
using OpenClawNet.UnitTests.Fixtures;
using System.Net;

namespace OpenClawNet.UnitTests.Gateway;

public sealed class ChannelsApiEndpointsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OpenClawDbContext> _options;
    private readonly PerTestTempDirectory _temp = new("openclaw-test");
    private string _testArtifactRoot => _temp.Path;

    public ChannelsApiEndpointsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<OpenClawDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task GetChannels_ReturnsJobsWithArtifacts_OrderedByLastActivity()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job1 = new ScheduledJob { Name = "Job 1", Prompt = "Test", Status = JobStatus.Active };
        var job2 = new ScheduledJob { Name = "Job 2", Prompt = "Test", Status = JobStatus.Active };
        var job3 = new ScheduledJob { Name = "Job 3", Prompt = "Test", Status = JobStatus.Active };
        db.Jobs.AddRange(job1, job2, job3);
        await db.SaveChangesAsync();

        var run1 = new JobRun { JobId = job1.Id, Status = "completed", StartedAt = DateTime.UtcNow.AddHours(-2) };
        var run2 = new JobRun { JobId = job2.Id, Status = "completed", StartedAt = DateTime.UtcNow.AddHours(-1) };
        db.JobRuns.AddRange(run1, run2);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);
        await storageService.CreateArtifactAsync(job1.Id, run1.Id, JobRunArtifactKind.Text, "Artifact 1", "content", 0);
        await storageService.CreateArtifactAsync(job2.Id, run2.Id, JobRunArtifactKind.Text, "Artifact 2", "content", 0);
        await storageService.CreateArtifactAsync(job2.Id, run2.Id, JobRunArtifactKind.Text, "Artifact 3", "content", 1);

        var dbFactory = CreateDbContextFactory();
        var httpContext = CreateLoopbackHttpContext();

        var result = await GetChannelsListHandler(dbFactory, null, httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<List<ChannelSummaryDto>>>();
        var okResult = (Microsoft.AspNetCore.Http.HttpResults.Ok<List<ChannelSummaryDto>>)result;
        var channels = okResult.Value!;

        channels.Should().HaveCount(2);
        channels[0].JobName.Should().Be("Job 2");
        channels[0].ArtifactCount.Should().Be(2);
        channels[1].JobName.Should().Be("Job 1");
        channels[1].ArtifactCount.Should().Be(1);
    }

    [Fact]
    public async Task GetChannelDetail_ReturnsJobMetadata_AndRecentRuns()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test prompt", Status = JobStatus.Active };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var run1 = new JobRun { JobId = job.Id, Status = "completed", StartedAt = DateTime.UtcNow.AddHours(-2), CompletedAt = DateTime.UtcNow.AddHours(-1) };
        var run2 = new JobRun { JobId = job.Id, Status = "running", StartedAt = DateTime.UtcNow };
        db.JobRuns.AddRange(run1, run2);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);
        await storageService.CreateArtifactAsync(job.Id, run1.Id, JobRunArtifactKind.Text, "Artifact 1", "content", 0);

        var dbFactory = CreateDbContextFactory();
        var httpContext = CreateLoopbackHttpContext();

        var result = await GetChannelDetailHandler(job.Id, dbFactory, httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<ChannelDetailDto>>();
        var okResult = (Microsoft.AspNetCore.Http.HttpResults.Ok<ChannelDetailDto>)result;
        var detail = okResult.Value!;

        detail.JobName.Should().Be("Test Job");
        detail.Prompt.Should().Be("Test prompt");
        detail.Status.Should().Be("active");
        detail.RecentRuns.Should().HaveCount(2);
        detail.RecentRuns[0].RunId.Should().Be(run2.Id);
        detail.RecentRuns[0].Status.Should().Be("running");
        detail.RecentRuns[1].RunId.Should().Be(run1.Id);
        detail.RecentRuns[1].ArtifactCount.Should().Be(1);
    }

    [Fact]
    public async Task GetRunArtifacts_ReturnsAllArtifacts_OrderedBySequence()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        var run = new JobRun { JobId = job.Id, Status = "completed", StartedAt = DateTime.UtcNow };
        db.Jobs.Add(job);
        db.JobRuns.Add(run);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);
        await storageService.CreateArtifactAsync(job.Id, run.Id, JobRunArtifactKind.Markdown, "First", "# First", 0);
        await storageService.CreateArtifactAsync(job.Id, run.Id, JobRunArtifactKind.Text, "Second", "Second content", 1);
        await storageService.CreateArtifactAsync(job.Id, run.Id, JobRunArtifactKind.Json, "Third", """{"key": "value"}""", 2);

        var dbFactory = CreateDbContextFactory();
        var httpContext = CreateLoopbackHttpContext();

        var result = await GetRunArtifactsHandler(job.Id, run.Id, dbFactory, storageService, httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<RunArtifactsDto>>();
        var okResult = (Microsoft.AspNetCore.Http.HttpResults.Ok<RunArtifactsDto>)result;
        var dto = okResult.Value!;

        dto.Artifacts.Should().HaveCount(3);
        dto.Artifacts[0].Title.Should().Be("First");
        dto.Artifacts[0].Type.Should().Be("markdown");
        dto.Artifacts[0].ContentPreview.Should().Contain("# First");
        dto.Artifacts[1].Title.Should().Be("Second");
        dto.Artifacts[2].Title.Should().Be("Third");
    }

    [Fact]
    public async Task GetArtifactContent_ReturnsFullContent_WithCorrectMimeType()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        var run = new JobRun { JobId = job.Id, Status = "completed", StartedAt = DateTime.UtcNow };
        db.Jobs.Add(job);
        db.JobRuns.Add(run);
        await db.SaveChangesAsync();

        var storageService = CreateArtifactStorageService(db);
        var content = "# Full Markdown Content\n\nThis is the complete artifact.";
        var artifact = await storageService.CreateArtifactAsync(
            job.Id, run.Id, JobRunArtifactKind.Markdown, "Test", content, 0);

        await using var db2 = new OpenClawDbContext(_options);
        var storedArtifact = await db2.JobRunArtifacts.FindAsync(artifact.Id);
        storedArtifact!.MimeType = "text/markdown";
        await db2.SaveChangesAsync();

        var dbFactory = CreateDbContextFactory();
        var httpContext = CreateLoopbackHttpContext();

        var result = await GetArtifactContentHandler(job.Id, run.Id, artifact.Id, dbFactory, storageService, httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult>();
        var contentResult = (Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult)result;
        contentResult.ContentType.Should().Be("text/markdown");
        contentResult.ResponseContent.Should().Be(content);
    }

    [Fact]
    public async Task PostArtifact_CreatesNewArtifact_ForLatestRun()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var job = new ScheduledJob { Name = "Test Job", Prompt = "Test", Status = JobStatus.Active };
        var oldRun = new JobRun { JobId = job.Id, Status = "completed", StartedAt = DateTime.UtcNow.AddHours(-1) };
        var latestRun = new JobRun { JobId = job.Id, Status = "running", StartedAt = DateTime.UtcNow };
        db.Jobs.Add(job);
        db.JobRuns.AddRange(oldRun, latestRun);
        await db.SaveChangesAsync();

        var request = new CreateArtifactRequest("markdown", "Custom Artifact", "# Custom Content");
        var dbFactory = CreateDbContextFactory();
        var storageService = CreateArtifactStorageService(db);
        var httpContext = CreateLoopbackHttpContext();

        var result = await PostArtifactHandler(job.Id, request, dbFactory, storageService, httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Created<object>>();
        var createdResult = (Microsoft.AspNetCore.Http.HttpResults.Created<object>)result;
        createdResult.Location.Should().Contain(latestRun.Id.ToString());

        await using var db2 = new OpenClawDbContext(_options);
        var artifacts = await db2.JobRunArtifacts.Where(a => a.JobRunId == latestRun.Id).ToListAsync();
        artifacts.Should().HaveCount(1);
        artifacts[0].Title.Should().Be("Custom Artifact");
        artifacts[0].ArtifactType.Should().Be(JobRunArtifactKind.Markdown);
    }

    [Fact]
    public async Task LoopbackAuth_LocalhostIPv4_Allowed()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var dbFactory = CreateDbContextFactory();
        var httpContext = CreateHttpContextWithIp("127.0.0.1");

        var result = await GetChannelsListHandler(dbFactory, null, httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<List<ChannelSummaryDto>>>();
    }

    [Fact]
    public async Task LoopbackAuth_LocalhostIPv6_Allowed()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var dbFactory = CreateDbContextFactory();
        var httpContext = CreateHttpContextWithIp("::1");

        var result = await GetChannelsListHandler(dbFactory, null, httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<List<ChannelSummaryDto>>>();
    }

    [Fact]
    public async Task LoopbackAuth_RemoteIP_Returns403()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var dbFactory = CreateDbContextFactory();
        var httpContext = CreateHttpContextWithIp("192.168.1.100");

        var result = await GetChannelsListHandler(dbFactory, null, httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.StatusCodeHttpResult>();
        var statusResult = (Microsoft.AspNetCore.Http.HttpResults.StatusCodeHttpResult)result;
        statusResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GetChannelDetail_UnknownJobId_Returns404()
    {
        await using var db = new OpenClawDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var dbFactory = CreateDbContextFactory();
        var httpContext = CreateLoopbackHttpContext();
        var unknownJobId = Guid.NewGuid();

        var result = await GetChannelDetailHandler(unknownJobId, dbFactory, httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NotFound>();
    }

    private async Task<IResult> GetChannelsListHandler(
        IDbContextFactory<OpenClawDbContext> dbFactory, int? limit, HttpContext httpContext)
    {
        if (!IsLoopbackRequest(httpContext))
            return Results.StatusCode(403);

        await using var db = await dbFactory.CreateDbContextAsync();

        var jobsWithArtifacts = await db.JobRunArtifacts
            .GroupBy(a => a.JobId)
            .Select(g => new
            {
                JobId = g.Key,
                LastArtifactDate = g.Max(a => a.CreatedAt),
                ArtifactCount = g.Count()
            })
            .OrderByDescending(x => x.LastArtifactDate)
            .Take(limit ?? 50)
            .ToListAsync();

        var jobIds = jobsWithArtifacts.Select(j => j.JobId).ToList();
        var jobs = await db.Jobs
            .Where(j => jobIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id);

        var channels = jobsWithArtifacts.Select(j => new ChannelSummaryDto(
            j.JobId,
            jobs.TryGetValue(j.JobId, out var job) ? job.Name : "Unknown Job",
            j.LastArtifactDate,
            j.ArtifactCount
        )).ToList();

        return Results.Ok(channels);
    }

    private async Task<IResult> GetChannelDetailHandler(
        Guid jobId, IDbContextFactory<OpenClawDbContext> dbFactory, HttpContext httpContext)
    {
        if (!IsLoopbackRequest(httpContext))
            return Results.StatusCode(403);

        await using var db = await dbFactory.CreateDbContextAsync();

        var job = await db.Jobs.FindAsync(jobId);
        if (job is null)
            return Results.NotFound();

        var runs = await db.JobRuns
            .Where(r => r.JobId == jobId)
            .OrderByDescending(r => r.StartedAt)
            .Take(20)
            .Select(r => new
            {
                r.Id,
                r.Status,
                r.StartedAt,
                r.CompletedAt,
                ArtifactCount = db.JobRunArtifacts.Count(a => a.JobRunId == r.Id)
            })
            .ToListAsync();

        var detail = new ChannelDetailDto(
            job.Id,
            job.Name,
            job.Status.ToString().ToLowerInvariant(),
            job.Prompt,
            runs.Select(r => new ChannelRunSummaryDto(
                r.Id,
                r.Status,
                r.StartedAt,
                r.CompletedAt,
                r.ArtifactCount
            )).ToList()
        );

        return Results.Ok(detail);
    }

    private async Task<IResult> GetRunArtifactsHandler(
        Guid jobId, Guid runId, IDbContextFactory<OpenClawDbContext> dbFactory,
        ArtifactStorageService artifactStorage, HttpContext httpContext)
    {
        if (!IsLoopbackRequest(httpContext))
            return Results.StatusCode(403);

        await using var db = await dbFactory.CreateDbContextAsync();

        var run = await db.JobRuns
            .Where(r => r.Id == runId && r.JobId == jobId)
            .FirstOrDefaultAsync();

        if (run is null)
            return Results.NotFound();

        var artifacts = await db.JobRunArtifacts
            .Where(a => a.JobRunId == runId)
            .OrderBy(a => a.Sequence)
            .ThenBy(a => a.CreatedAt)
            .ToListAsync();

        var artifactDtos = new List<ArtifactDto>();
        foreach (var artifact in artifacts)
        {
            string? contentPreview = null;

            if (artifact.ArtifactType != JobRunArtifactKind.File)
            {
                var fullContent = await artifactStorage.GetArtifactContentAsync(artifact);
                contentPreview = fullContent.Length > 500
                    ? fullContent.Substring(0, 500) + "..."
                    : fullContent;
            }

            artifactDtos.Add(new ArtifactDto(
                artifact.Id,
                artifact.ArtifactType.ToString().ToLowerInvariant(),
                artifact.Title,
                contentPreview,
                artifact.ContentSizeBytes,
                artifact.MimeType,
                artifact.CreatedAt
            ));
        }

        return Results.Ok(new RunArtifactsDto(
            run.Id,
            run.JobId,
            run.Status,
            run.StartedAt,
            run.CompletedAt,
            artifactDtos
        ));
    }

    private async Task<IResult> GetArtifactContentHandler(
        Guid jobId, Guid runId, Guid artifactId, IDbContextFactory<OpenClawDbContext> dbFactory,
        ArtifactStorageService artifactStorage, HttpContext httpContext)
    {
        if (!IsLoopbackRequest(httpContext))
            return Results.StatusCode(403);

        await using var db = await dbFactory.CreateDbContextAsync();

        var artifact = await db.JobRunArtifacts
            .Where(a => a.Id == artifactId && a.JobRunId == runId && a.JobId == jobId)
            .FirstOrDefaultAsync();

        if (artifact is null)
            return Results.NotFound();

        var content = await artifactStorage.GetArtifactContentAsync(artifact);
        var contentType = artifact.MimeType ?? "text/plain";

        return Results.Content(content, contentType);
    }

    private async Task<IResult> PostArtifactHandler(
        Guid jobId, CreateArtifactRequest request, IDbContextFactory<OpenClawDbContext> dbFactory,
        ArtifactStorageService artifactStorage, HttpContext httpContext)
    {
        if (!IsLoopbackRequest(httpContext))
            return Results.StatusCode(403);

        await using var db = await dbFactory.CreateDbContextAsync();

        var latestRun = await db.JobRuns
            .Where(r => r.JobId == jobId)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync();

        if (latestRun is null)
            return Results.BadRequest(new { error = "No runs found for this job" });

        var maxSequence = await db.JobRunArtifacts
            .Where(a => a.JobRunId == latestRun.Id)
            .MaxAsync(a => (int?)a.Sequence) ?? -1;

        var artifact = await artifactStorage.CreateArtifactAsync(
            jobId,
            latestRun.Id,
            Enum.Parse<JobRunArtifactKind>(request.Type, ignoreCase: true),
            request.Title,
            request.Content,
            maxSequence + 1
        );

        return Results.Created($"/api/channels/{jobId}/runs/{latestRun.Id}/artifacts/{artifact.Id}",
            new { artifactId = artifact.Id });
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

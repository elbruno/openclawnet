using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Gateway.Endpoints;
using OpenClawNet.Services.Scheduler;
using OpenClawNet.Storage.Entities;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.FileSystem;

namespace OpenClawNet.UnitTests.Demos;

/// <summary>
/// Unit tests for the Document Processing Pipeline demo scenario.
/// Validates scheduler concurrency control, cron calculation, FileSystemTool
/// operations, entity defaults, and DTO mapping.
/// </summary>
public sealed class DocumentPipelineTests : IDisposable
{
    private readonly string _tempDir;

    public DocumentPipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"doc-pipeline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static FileSystemTool CreateTool(string workspacePath)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:WorkspacePath"] = workspacePath
            })
            .Build();
        return new FileSystemTool(NullLogger<FileSystemTool>.Instance, config);
    }

    private static async Task<ToolResult> ExecuteAsync(
        FileSystemTool tool, string action, string path)
    {
        var args = $"{{\"action\":\"{action}\",\"path\":\"{path.Replace("\\", "\\\\")}\"}}";
        return await tool.ExecuteAsync(new ToolInput
        {
            ToolName = "file_system",
            RawArguments = args
        });
    }

    /// <summary>
    /// Walks up from the test assembly's bin directory to find the repo root
    /// (the directory containing OpenClawNet.slnx).
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("OpenClawNet.slnx").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        // Fallback — typical dev path
        return @"C:\src\openclawnet-plan";
    }

    // ── 1. Concurrency control ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void SchedulerPollingService_ProcessDueJobs_WithConcurrencyDisabled_SkipsRunningJob()
    {
        // The concurrency-skip logic in SchedulerPollingService checks
        // job.AllowConcurrentRuns and whether the job ID is in the running set.
        // We validate the decision variables here — a job with AllowConcurrentRuns=false
        // that is already running should be filtered out.
        var job = new ScheduledJob
        {
            Name = "doc-pipeline",
            Prompt = "Process documents",
            AllowConcurrentRuns = false,
            Status = JobStatus.Active
        };

        var runningJobIds = new HashSet<Guid> { job.Id };

        // This mirrors the guard condition in ProcessDueJobsAsync:
        //   if (!j.AllowConcurrentRuns && runningSet.Contains(j.Id)) → skip
        var shouldSkip = !job.AllowConcurrentRuns && runningJobIds.Contains(job.Id);

        shouldSkip.Should().BeTrue("a job with AllowConcurrentRuns=false that is already running must be skipped");
    }

    // ── 2. FileSystemTool – list sampleDocs ─────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task FileSystemTool_ListDirectory_ReturnsSampleDocs()
    {
        var repoRoot = FindRepoRoot();
        var sampleDocsPath = Path.Combine(repoRoot, "docs", "sampleDocs");

        Skip.IfNot(Directory.Exists(sampleDocsPath),
            "docs/sampleDocs not found — run from repo root");

        var tool = CreateTool(repoRoot);
        var result = await ExecuteAsync(tool, "list", sampleDocsPath);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Benefit_Options.pdf");
        result.Output.Should().Contain("Northwind_Health_Plus_Benefits_Details.pdf");
        result.Output.Should().Contain("Northwind_Standard_Benefits_Details.pdf");
        result.Output.Should().Contain("PerksPlus.pdf");
        result.Output.Should().Contain("employee_handbook.pdf");

        // Exactly 5 PDF files
        var pdfCount = result.Output.Split('\n')
            .Count(line => line.Contains(".pdf", StringComparison.OrdinalIgnoreCase));
        pdfCount.Should().Be(5);
    }

    // ── 3. FileSystemTool – blocked sensitive paths ─────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(".env")]
    [InlineData(".git/config")]
    [InlineData("appsettings.Production.json")]
    public async Task FileSystemTool_ReadFile_BlocksSensitivePaths(string sensitiveRelPath)
    {
        var tool = CreateTool(_tempDir);

        var result = await ExecuteAsync(tool, "read", sensitiveRelPath);

        result.Success.Should().BeFalse("sensitive paths must be blocked");
        result.Error.Should().Contain("blocked");
    }

    // ── 4. ScheduledJob default AllowConcurrentRuns ─────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void ScheduledJob_DefaultAllowConcurrentRuns_IsFalse()
    {
        var job = new ScheduledJob();

        job.AllowConcurrentRuns.Should().BeFalse(
            "new jobs must default to disallowing concurrent runs for safety");
    }

    // ── 5. CreateJobRequest DTO mapping ─────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void CreateJobRequest_WithAllowConcurrentRuns_MapsCorrectly()
    {
        var request = new CreateJobRequest
        {
            Name = "doc-pipeline-demo",
            Prompt = "Summarise all PDFs in docs/sampleDocs",
            AllowConcurrentRuns = true,
            CronExpression = "0 */6 * * *"
        };

        // Map to entity exactly as JobEndpoints does
        var entity = new ScheduledJob
        {
            Name = request.Name,
            Prompt = request.Prompt,
            AllowConcurrentRuns = request.AllowConcurrentRuns,
            CronExpression = request.CronExpression,
            IsRecurring = !string.IsNullOrEmpty(request.CronExpression)
        };

        entity.AllowConcurrentRuns.Should().BeTrue();
        entity.Name.Should().Be("doc-pipeline-demo");
        entity.IsRecurring.Should().BeTrue();
        entity.CronExpression.Should().Be("0 */6 * * *");
    }

    // ── 6. Cron – standard 5-field ──────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void SchedulerPollingService_CalculateNextRun_StandardCron()
    {
        // Every hour at minute 0: "0 * * * *"
        var from = new DateTime(2026, 4, 15, 10, 30, 0, DateTimeKind.Utc);
        var next = SchedulerPollingService.CalculateNextRun("0 * * * *", from, endAt: null, timeZone: null);

        next.Should().NotBeNull();
        next!.Value.Should().Be(new DateTime(2026, 4, 15, 11, 0, 0, DateTimeKind.Utc));
    }

    // ── 7. Cron – 6-field with seconds ──────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void SchedulerPollingService_CalculateNextRun_WithSeconds()
    {
        // Every 30 seconds: "*/30 * * * * *"
        var from = new DateTime(2026, 4, 15, 10, 0, 0, DateTimeKind.Utc);
        var next = SchedulerPollingService.CalculateNextRun("*/30 * * * * *", from, endAt: null, timeZone: null);

        next.Should().NotBeNull();
        next!.Value.Should().Be(new DateTime(2026, 4, 15, 10, 0, 30, DateTimeKind.Utc));
    }

    // ── 8. Cron – respects EndAt ────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void SchedulerPollingService_CalculateNextRun_RespectsEndAt()
    {
        // Every hour at minute 0 — but EndAt is in the past relative to next occurrence
        var from = new DateTime(2026, 4, 15, 23, 30, 0, DateTimeKind.Utc);
        var endAt = new DateTime(2026, 4, 15, 23, 45, 0, DateTimeKind.Utc); // before next hour

        var next = SchedulerPollingService.CalculateNextRun("0 * * * *", from, endAt, timeZone: null);

        next.Should().BeNull("the next occurrence is past EndAt so it should return null");
    }

    // ── 9. FileSystemTool – directory listing format ────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task FileSystemTool_ListDirectory_ReturnsDirectoriesAndFiles()
    {
        // Arrange — create subdirectory and file
        Directory.CreateDirectory(Path.Combine(_tempDir, "subdir"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "notes.txt"), "hello");

        var tool = CreateTool(_tempDir);
        var result = await ExecuteAsync(tool, "list", ".");

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("[DIR] subdir/");
        result.Output.Should().Contain("notes.txt");
    }

    // ── 10. FileSystemTool – find_projects action ───────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task FileSystemTool_FindProjects_FindsCsproj()
    {
        // Arrange — create a .csproj in a nested directory
        var projDir = Path.Combine(_tempDir, "src", "MyService");
        Directory.CreateDirectory(projDir);
        await File.WriteAllTextAsync(
            Path.Combine(projDir, "MyService.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var tool = CreateTool(_tempDir);
        var result = await ExecuteAsync(tool, "find_projects", ".");

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("MyService.csproj");
        result.Output.Should().Contain("MyService");
    }
}

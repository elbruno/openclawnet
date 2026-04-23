using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Storage;

/// <summary>
/// Manages JobRunArtifact storage with inline (≤64KB) and disk-based spillover.
/// Handles path traversal protection and disk file lifecycle.
/// </summary>
public class ArtifactStorageService
{
    private readonly IDbContextFactory<OpenClawDbContext> _dbFactory;
    private readonly ILogger<ArtifactStorageService> _logger;
    private readonly string _artifactRootPath;
    private const int InlineThresholdBytes = 64 * 1024; // 64 KB

    public ArtifactStorageService(
        IDbContextFactory<OpenClawDbContext> dbFactory,
        IConfiguration configuration,
        ILogger<ArtifactStorageService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        
        var storageRoot = configuration.GetValue<string>("Storage:RootPath")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenClawNet");
        _artifactRootPath = Path.Combine(storageRoot, "artifacts");
        Directory.CreateDirectory(_artifactRootPath);
    }

    /// <summary>
    /// Creates an artifact from JobRun.Result or JobRun.Error with auto-type detection and storage selection.
    /// </summary>
    public async Task<JobRunArtifact> CreateArtifactFromJobRunAsync(JobRun run)
    {
        var content = !string.IsNullOrEmpty(run.Error) ? run.Error : run.Result ?? string.Empty;
        var artifactType = DetermineArtifactType(content, !string.IsNullOrEmpty(run.Error));
        var title = !string.IsNullOrEmpty(run.Error) ? "Execution Error" : null;

        var artifact = new JobRunArtifact
        {
            JobRunId = run.Id,
            JobId = run.JobId,
            Sequence = 0,
            ArtifactType = artifactType,
            Title = title,
            CreatedAt = DateTime.UtcNow
        };

        await StoreContentAsync(artifact, content);

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.JobRunArtifacts.Add(artifact);
        await db.SaveChangesAsync();

        _logger.LogInformation("Created artifact {ArtifactId} for JobRun {JobRunId} (type={Type}, size={Size})",
            artifact.Id, run.Id, artifactType, artifact.ContentSizeBytes);

        return artifact;
    }

    /// <summary>
    /// Creates a custom artifact with explicit type and content.
    /// </summary>
    public async Task<JobRunArtifact> CreateArtifactAsync(
        Guid jobId, Guid jobRunId, JobRunArtifactKind type, string? title, string content, int sequence = 0)
    {
        var artifact = new JobRunArtifact
        {
            JobRunId = jobRunId,
            JobId = jobId,
            Sequence = sequence,
            ArtifactType = type,
            Title = title,
            CreatedAt = DateTime.UtcNow
        };

        await StoreContentAsync(artifact, content);

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.JobRunArtifacts.Add(artifact);
        await db.SaveChangesAsync();

        _logger.LogInformation("Created artifact {ArtifactId} for JobRun {JobRunId} (type={Type}, size={Size})",
            artifact.Id, jobRunId, type, artifact.ContentSizeBytes);

        return artifact;
    }

    /// <summary>
    /// Retrieves the full content of an artifact, reading from disk if necessary.
    /// </summary>
    public async Task<string> GetArtifactContentAsync(JobRunArtifact artifact)
    {
        if (!string.IsNullOrEmpty(artifact.ContentInline))
            return artifact.ContentInline;

        if (!string.IsNullOrEmpty(artifact.ContentPath))
        {
            var fullPath = Path.Combine(_artifactRootPath, artifact.ContentPath);
            if (File.Exists(fullPath))
                return await File.ReadAllTextAsync(fullPath);
            
            _logger.LogWarning("Artifact {ArtifactId} ContentPath not found: {Path}", artifact.Id, fullPath);
        }

        return string.Empty;
    }

    /// <summary>
    /// Deletes artifacts older than the specified date and enforces per-job run limits.
    /// </summary>
    public async Task<int> CleanupOldArtifactsAsync(int maxRunsPerJob, int maxAgeDays)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var cutoffDate = DateTime.UtcNow.AddDays(-maxAgeDays);
        var deleted = 0;

        // Get all jobs that have artifacts
        var jobIds = await db.JobRunArtifacts
            .Select(a => a.JobId)
            .Distinct()
            .ToListAsync();

        foreach (var jobId in jobIds)
        {
            // Find runs to delete (keep last N, but respect age limit)
            var runsToDelete = await db.JobRuns
                .Where(r => r.JobId == jobId)
                .OrderByDescending(r => r.StartedAt)
                .Skip(maxRunsPerJob)
                .Select(r => r.Id)
                .ToListAsync();

            // Also delete runs older than cutoff
            var oldRuns = await db.JobRuns
                .Where(r => r.JobId == jobId && r.StartedAt < cutoffDate)
                .Select(r => r.Id)
                .ToListAsync();

            var allRunsToDelete = runsToDelete.Union(oldRuns).ToHashSet();

            // Get artifacts for these runs
            var artifactsToDelete = await db.JobRunArtifacts
                .Where(a => allRunsToDelete.Contains(a.JobRunId))
                .ToListAsync();

            foreach (var artifact in artifactsToDelete)
            {
                // Delete disk file if it exists
                if (!string.IsNullOrEmpty(artifact.ContentPath))
                {
                    var fullPath = Path.Combine(_artifactRootPath, artifact.ContentPath);
                    try
                    {
                        if (File.Exists(fullPath))
                            File.Delete(fullPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete artifact file: {Path}", fullPath);
                    }
                }

                db.JobRunArtifacts.Remove(artifact);
                deleted++;
            }
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("Cleaned up {Count} old artifacts", deleted);
        return deleted;
    }

    private async Task StoreContentAsync(JobRunArtifact artifact, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetByteCount(content);
        artifact.ContentSizeBytes = bytes;

        if (bytes <= InlineThresholdBytes)
        {
            artifact.ContentInline = content;
            artifact.ContentPath = null;
        }
        else
        {
            // Validate Guids to prevent path traversal
            var jobIdStr = artifact.JobId.ToString("N");
            var runIdStr = artifact.JobRunId.ToString("N");
            
            var relativePath = Path.Combine(jobIdStr, runIdStr, $"{artifact.Sequence}_{artifact.Id:N}.txt");
            var fullPath = Path.Combine(_artifactRootPath, relativePath);
            
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(fullPath, content);
            
            artifact.ContentInline = null;
            artifact.ContentPath = relativePath;
        }
    }

    private JobRunArtifactKind DetermineArtifactType(string content, bool isError)
    {
        if (isError)
            return JobRunArtifactKind.Error;

        if (string.IsNullOrWhiteSpace(content))
            return JobRunArtifactKind.Text;

        var trimmed = content.TrimStart();
        
        if (trimmed.StartsWith("#") || trimmed.Contains("```"))
            return JobRunArtifactKind.Markdown;
        
        if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
            return JobRunArtifactKind.Json;
        
        return JobRunArtifactKind.Text;
    }
}

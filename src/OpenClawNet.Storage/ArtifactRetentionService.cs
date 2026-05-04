using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenClawNet.Storage;

/// <summary>
/// Background service that enforces artifact retention policy (max runs per job + max age).
/// </summary>
public class ArtifactRetentionService : BackgroundService
{
    private readonly ArtifactStorageService _artifactStorage;
    private readonly ILogger<ArtifactRetentionService> _logger;
    private readonly ArtifactRetentionOptions _options;

    public ArtifactRetentionService(
        ArtifactStorageService artifactStorage,
        IOptions<ArtifactRetentionOptions> options,
        ILogger<ArtifactRetentionService> logger)
    {
        _artifactStorage = artifactStorage;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Artifact retention service started — maxRuns={MaxRuns}, maxAge={MaxAge}d, interval={Interval}h",
            _options.MaxRunsPerJob, _options.MaxAgeDays, _options.CleanupIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(_options.CleanupIntervalHours), stoppingToken);
                
                _logger.LogInformation("Running artifact retention cleanup");
                var deleted = await _artifactStorage.CleanupOldArtifactsAsync(
                    _options.MaxRunsPerJob, _options.MaxAgeDays);
                
                if (deleted > 0)
                    _logger.LogInformation("Retention cleanup deleted {Count} artifacts", deleted);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during artifact retention cleanup");
            }
        }

        _logger.LogInformation("Artifact retention service stopped");
    }
}

/// <summary>
/// Configuration for artifact retention policy.
/// </summary>
public class ArtifactRetentionOptions
{
    /// <summary>Keep last N runs per job (default: 100)</summary>
    public int MaxRunsPerJob { get; set; } = 100;
    
    /// <summary>Hard cap: delete artifacts older than N days (default: 30)</summary>
    public int MaxAgeDays { get; set; } = 30;
    
    /// <summary>How often to run cleanup (default: 24 hours)</summary>
    public double CleanupIntervalHours { get; set; } = 24.0;
}

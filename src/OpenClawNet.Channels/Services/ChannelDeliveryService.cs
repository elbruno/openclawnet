using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenClawNet.Channels.Adapters;
using OpenClawNet.Channels.Dtos;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Channels.Services;

/// <summary>
/// Multi-channel delivery orchestration service.
/// Coordinates adapter calls, captures results, and logs audit trail.
/// Fire-and-forget pattern: never throws on adapter failure.
/// </summary>
public sealed class ChannelDeliveryService : IChannelDeliveryService
{
    private readonly IChannelDeliveryAdapterFactory _factory;
    private readonly OpenClawDbContext _dbContext;
    private readonly ILogger<ChannelDeliveryService> _logger;

    public ChannelDeliveryService(
        IChannelDeliveryAdapterFactory factory,
        OpenClawDbContext dbContext,
        ILogger<ChannelDeliveryService> logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Dtos.DeliveryResult> DeliverAsync(
        ScheduledJob job,
        Guid artifactId,
        string artifactType,
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactType);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var stopwatch = Stopwatch.StartNew();
        var deliveryLogs = new List<AdapterDeliveryLog>();
        var failures = new List<DeliveryFailure>();

        _logger.LogInformation(
            "Starting multi-channel delivery for job {JobId} ({JobName}), artifact {ArtifactId}",
            job.Id, job.Name, artifactId);

        // Query enabled channel configurations for this job
        var channelConfigs = await _dbContext.JobChannelConfigurations
            .Where(c => c.JobId == job.Id && c.IsEnabled)
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Found {Count} enabled channel(s) for job {JobId}",
            channelConfigs.Count, job.Id);

        foreach (var config in channelConfigs)
        {
            var log = new AdapterDeliveryLog
            {
                JobId = job.Id,
                ChannelType = config.ChannelType,
                ChannelConfig = config.ChannelConfig,
                Status = DeliveryStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                _logger.LogDebug(
                    "Resolving adapter for channel type {ChannelType}",
                    config.ChannelType);

                // Resolve adapter from factory
                var adapter = _factory.CreateAdapter(config.ChannelType);

                _logger.LogDebug(
                    "Delivering to {ChannelType} via adapter {AdapterName}",
                    config.ChannelType, adapter.Name);

                // Call adapter (fire-and-forget: capture result, don't re-throw)
                var adapterResult = await adapter.DeliverAsync(
                    job.Id,
                    job.Name,
                    artifactId,
                    artifactType,
                    config.ChannelConfig,
                    cancellationToken);

                if (adapterResult.Success)
                {
                    log.Status = DeliveryStatus.Success;
                    log.DeliveredAt = DateTime.UtcNow;

                    _logger.LogInformation(
                        "Successfully delivered to {ChannelType} for job {JobId}, artifact {ArtifactId}",
                        config.ChannelType, job.Id, artifactId);
                }
                else
                {
                    log.Status = DeliveryStatus.Failed;
                    log.ErrorMessage = adapterResult.ErrorMessage ?? "Unknown error";

                    failures.Add(new DeliveryFailure
                    {
                        ChannelType = config.ChannelType,
                        ErrorMessage = log.ErrorMessage
                    });

                    _logger.LogWarning(
                        "Adapter reported failure for {ChannelType}: {Error}",
                        config.ChannelType, log.ErrorMessage);
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Unknown adapter"))
            {
                // Factory couldn't create adapter (unknown type)
                log.Status = DeliveryStatus.Failed;
                log.ErrorMessage = $"Unknown adapter type: {config.ChannelType}";

                failures.Add(new DeliveryFailure
                {
                    ChannelType = config.ChannelType,
                    ErrorMessage = log.ErrorMessage
                });

                _logger.LogError(
                    ex,
                    "Failed to create adapter for channel type {ChannelType}",
                    config.ChannelType);
            }
            catch (Exception ex)
            {
                // Adapter threw exception (network error, invalid config, etc.)
                log.Status = DeliveryStatus.Failed;
                log.ErrorMessage = ex.Message;

                failures.Add(new DeliveryFailure
                {
                    ChannelType = config.ChannelType,
                    ErrorMessage = ex.Message
                });

                _logger.LogError(
                    ex,
                    "Exception during delivery to {ChannelType} for job {JobId}",
                    config.ChannelType, job.Id);
            }

            deliveryLogs.Add(log);
        }

        // Persist all delivery logs to database
        if (deliveryLogs.Count > 0)
        {
            _dbContext.AdapterDeliveryLogs.AddRange(deliveryLogs);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Persisted {Count} delivery log(s) for job {JobId}",
                deliveryLogs.Count, job.Id);
        }

        stopwatch.Stop();

        var successCount = deliveryLogs.Count(l => l.Status == DeliveryStatus.Success);
        var failureCount = deliveryLogs.Count(l => l.Status == DeliveryStatus.Failed);

        _logger.LogInformation(
            "Multi-channel delivery complete for job {JobId}: {SuccessCount} succeeded, {FailureCount} failed, {Duration}ms",
            job.Id, successCount, failureCount, stopwatch.ElapsedMilliseconds);

        return new Dtos.DeliveryResult
        {
            JobId = job.Id.ToString(),
            TotalAttempted = deliveryLogs.Count,
            SuccessCount = successCount,
            FailureCount = failureCount,
            Failures = failures,
            Duration = stopwatch.Elapsed,
            CompletedAt = DateTime.UtcNow
        };
    }
}

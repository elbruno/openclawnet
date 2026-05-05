namespace OpenClawNet.Services.Scheduler.Services;

/// <summary>
/// Thread-safe singleton tracking the number of currently running jobs.
/// Shared between <see cref="SchedulerPollingService"/> and HTTP endpoints.
/// </summary>
public sealed class SchedulerRunState
{
    private int _runningCount;
    private DateTime? _lastPollAt;
    private bool _isRunning = true;

    public int RunningJobCount => _runningCount;

    public void Increment() => Interlocked.Increment(ref _runningCount);
    public void Decrement() => Interlocked.Decrement(ref _runningCount);

    public void UpdatePollTimestamp() => _lastPollAt = DateTime.UtcNow;

    public SchedulerStateInfo GetState() => new()
    {
        IsRunning = _isRunning,
        RunningJobCount = _runningCount,
        LastPollAt = _lastPollAt,
        PollingIntervalMs = 5000
    };
}

public sealed record SchedulerStateInfo
{
    public bool IsRunning { get; init; }
    public int RunningJobCount { get; init; }
    public DateTime? LastPollAt { get; init; }
    public int PollingIntervalMs { get; init; }
}

// Storage W-3 (Drummond AC2) — default IModelStorageQuota implementation.
//
// Defaults: 50 GB total under {models}/, 20 GB per-file ceiling. Both
// configurable via StorageOptions.{ModelMaxTotalBytes, ModelMaxPerFileBytes}.
// Current-total computation uses a directory walk cached for 30s — the
// models root is a slow, large directory and walking it on every check
// would dominate download latency. Cache invalidates after the TTL OR
// after a successful Add/Reserve call (the coordinator notifies the cache).
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace OpenClawNet.Storage;

/// <summary>
/// Default quota enforcer for the models root. Defaults: 50 GB total,
/// 20 GB per file. Thread-safe; safe to register as a singleton.
/// </summary>
public sealed class ModelStorageQuota : IModelStorageQuota
{
    /// <summary>Default total-quota ceiling: 50 GB.</summary>
    public const long DefaultMaxTotalBytes = 50L * 1024 * 1024 * 1024;

    /// <summary>Default per-file ceiling: 20 GB.</summary>
    public const long DefaultMaxPerFileBytes = 20L * 1024 * 1024 * 1024;

    /// <summary>Directory-walk cache TTL.</summary>
    public static readonly TimeSpan WalkCacheTtl = TimeSpan.FromSeconds(30);

    private readonly long _maxTotalBytes;
    private readonly long _maxPerFileBytes;
    private readonly ILogger<ModelStorageQuota> _logger;
    private readonly TimeProvider _clock;

    // Single-slot cache of the most recent walk result. Keyed by
    // modelsRoot so a test or AppHost reconfig that points at a different
    // root doesn't return a stale total.
    private readonly object _cacheLock = new();
    private string? _cachedRoot;
    private long _cachedTotalBytes;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    /// <summary>Test/factory-friendly ctor.</summary>
    internal ModelStorageQuota(
        long maxTotalBytes = DefaultMaxTotalBytes,
        long maxPerFileBytes = DefaultMaxPerFileBytes,
        ILogger<ModelStorageQuota>? logger = null,
        TimeProvider? clock = null)
    {
        if (maxTotalBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTotalBytes));
        if (maxPerFileBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPerFileBytes));

        _maxTotalBytes = maxTotalBytes;
        _maxPerFileBytes = maxPerFileBytes;
        _logger = logger ?? NullLogger<ModelStorageQuota>.Instance;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>DI ctor — binds limits from <see cref="StorageOptions"/>.</summary>
    [ActivatorUtilitiesConstructor]
    public ModelStorageQuota(
        IOptions<StorageOptions> options,
        ILogger<ModelStorageQuota> logger)
        : this(
            (options?.Value.ModelMaxTotalBytes ?? DefaultMaxTotalBytes),
            (options?.Value.ModelMaxPerFileBytes ?? DefaultMaxPerFileBytes),
            logger,
            clock: null)
    { }

    /// <inheritdoc />
    public Task<QuotaCheckResult> CheckAsync(
        string modelsRoot,
        long incomingBytes,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(modelsRoot))
            throw new ArgumentException("Models root must be non-empty.", nameof(modelsRoot));
        if (incomingBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(incomingBytes));

        ct.ThrowIfCancellationRequested();

        // Per-file ceiling — cheapest check, first.
        if (incomingBytes > _maxPerFileBytes)
        {
            return Task.FromResult(new QuotaCheckResult(
                Allowed: false,
                CurrentTotalBytes: -1,
                AvailableDiskBytes: -1,
                DenyReason: $"per-file limit ({_maxPerFileBytes} bytes) exceeded"));
        }

        // Current total under the models root (cached walk).
        long currentTotal;
        try
        {
            currentTotal = GetCurrentTotalBytes(modelsRoot);
        }
        catch (DirectoryNotFoundException)
        {
            // Root doesn't exist yet — total is 0. ResolveModelsRoot would
            // have created it, but the coordinator may call us before that.
            currentTotal = 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex,
                "Quota walk failed under '{Root}'. Treating as 0 total — fail-OPEN here is " +
                "acceptable because the per-file ceiling and AvailableFreeSpace check below " +
                "still enforce a hard ceiling.", modelsRoot);
            currentTotal = 0;
        }

        // Total-quota ceiling — current + incoming.
        if (currentTotal + incomingBytes > _maxTotalBytes)
        {
            return Task.FromResult(new QuotaCheckResult(
                Allowed: false,
                CurrentTotalBytes: currentTotal,
                AvailableDiskBytes: TryGetAvailableDiskBytes(modelsRoot),
                DenyReason: $"total quota ({_maxTotalBytes} bytes) exceeded"));
        }

        // Disk-space cross-check — DriveInfo.AvailableFreeSpace.
        var availableDisk = TryGetAvailableDiskBytes(modelsRoot);
        if (availableDisk >= 0 && incomingBytes > availableDisk)
        {
            return Task.FromResult(new QuotaCheckResult(
                Allowed: false,
                CurrentTotalBytes: currentTotal,
                AvailableDiskBytes: availableDisk,
                DenyReason: "insufficient disk space"));
        }

        return Task.FromResult(new QuotaCheckResult(
            Allowed: true,
            CurrentTotalBytes: currentTotal,
            AvailableDiskBytes: availableDisk,
            DenyReason: null));
    }

    /// <summary>
    /// Invalidates the directory-walk cache. Called by
    /// <c>ModelDownloadCoordinator</c> after a successful download so the
    /// next quota check sees the new file without waiting for the TTL.
    /// </summary>
    public void InvalidateWalkCache()
    {
        lock (_cacheLock)
        {
            _cachedAt = DateTimeOffset.MinValue;
            _cachedRoot = null;
            _cachedTotalBytes = 0;
        }
    }

    private long GetCurrentTotalBytes(string modelsRoot)
    {
        var now = _clock.GetUtcNow();

        lock (_cacheLock)
        {
            if (_cachedRoot is not null
                && string.Equals(_cachedRoot, modelsRoot, StringComparison.OrdinalIgnoreCase)
                && now - _cachedAt < WalkCacheTtl)
            {
                return _cachedTotalBytes;
            }
        }

        // Walk OUTSIDE the lock — it's slow and we don't want to serialize
        // concurrent quota checks against the same stale state.
        long total = 0;
        if (Directory.Exists(modelsRoot))
        {
            foreach (var f in Directory.EnumerateFiles(
                         modelsRoot, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; }
                catch (FileNotFoundException) { /* race: file deleted mid-walk */ }
                catch (UnauthorizedAccessException) { /* skip — best effort */ }
            }
        }

        lock (_cacheLock)
        {
            _cachedRoot = modelsRoot;
            _cachedTotalBytes = total;
            _cachedAt = now;
        }

        return total;
    }

    private static long TryGetAvailableDiskBytes(string modelsRoot)
    {
        try
        {
            var driveLetter = Path.GetPathRoot(Path.GetFullPath(modelsRoot));
            if (string.IsNullOrEmpty(driveLetter))
                return -1;
            return new DriveInfo(driveLetter).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return -1;
        }
    }
}

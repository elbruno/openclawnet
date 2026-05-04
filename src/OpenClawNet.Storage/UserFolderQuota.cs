// Storage W-4 (Drummond W-4 AC2) — default IUserFolderQuota implementation.
//
// Defaults: 5 GB per folder, 25 GB total under the user-folder surface
// (the storage root, excluding agents/, models/, skills/, binary/,
// dataprotection-keys/, audit/). Both configurable via StorageOptions.
//
// Cache strategy mirrors ModelStorageQuota with one W-4-specific twist:
// per-folder cache slots so upstream invalidation hits a single folder
// without busting the total cache. The total is computed by summing the
// per-folder slots, so per-folder invalidation correctly cascades to the
// total.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace OpenClawNet.Storage;

/// <summary>
/// Default quota enforcer for user folders. Defaults: 5 GB per folder,
/// 25 GB total. Thread-safe; safe to register as a singleton.
/// </summary>
public sealed class UserFolderQuota : IUserFolderQuota
{
    /// <summary>Default per-folder ceiling: 5 GB.</summary>
    public const long DefaultMaxPerFolderBytes = 5L * 1024 * 1024 * 1024;

    /// <summary>Default total ceiling across all user folders: 25 GB.</summary>
    public const long DefaultMaxTotalBytes = 25L * 1024 * 1024 * 1024;

    /// <summary>Per-folder directory-walk cache TTL.</summary>
    public static readonly TimeSpan WalkCacheTtl = TimeSpan.FromSeconds(30);

    // Same exclusion set as UserFolderHealthCheck — the "what counts as a
    // user folder" definition lives in one place per file (not great, but
    // safer than introducing a shared static dependency that could be
    // mutated). Keep these two in sync if you add a scope subfolder.
    private static readonly HashSet<string> ExcludedFolderNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "agents", "models", "skills", "binary",
            "dataprotection-keys", "audit",
        };

    private static readonly StringComparison PathComparison =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private readonly long _maxPerFolderBytes;
    private readonly long _maxTotalBytes;
    private readonly ILogger<UserFolderQuota> _logger;
    private readonly TimeProvider _clock;

    // Per-folder cache slot: folderName → (bytes, sampledAt).
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, (long Bytes, DateTimeOffset At)> _folderCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Test/factory-friendly ctor.</summary>
    public UserFolderQuota(
        long maxPerFolderBytes = DefaultMaxPerFolderBytes,
        long maxTotalBytes = DefaultMaxTotalBytes,
        ILogger<UserFolderQuota>? logger = null,
        TimeProvider? clock = null)
    {
        if (maxPerFolderBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPerFolderBytes));
        if (maxTotalBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTotalBytes));

        _maxPerFolderBytes = maxPerFolderBytes;
        _maxTotalBytes = maxTotalBytes;
        _logger = logger ?? NullLogger<UserFolderQuota>.Instance;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>DI ctor — binds limits from <see cref="StorageOptions"/>.</summary>
    [ActivatorUtilitiesConstructor]
    public UserFolderQuota(
        IOptions<StorageOptions> options,
        ILogger<UserFolderQuota> logger)
        : this(
            (options?.Value.UserMaxPerFolderBytes ?? DefaultMaxPerFolderBytes),
            (options?.Value.UserMaxTotalBytes ?? DefaultMaxTotalBytes),
            logger,
            clock: null)
    { }

    /// <inheritdoc />
    public Task<UserQuotaCheckResult> CheckAsync(
        string folderName,
        long incomingBytes,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folderName))
            throw new ArgumentException("Folder name must be non-empty.", nameof(folderName));
        if (incomingBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(incomingBytes));

        ct.ThrowIfCancellationRequested();

        // Per-folder ceiling — cheapest "fail outright" check first. We
        // need the current folder bytes to layer the ceiling over.
        var (rootPath, _) = OpenClawNetPaths.ResolveRoot();
        var folderBytes = GetCurrentFolderBytes(rootPath, folderName);

        if (folderBytes + incomingBytes > _maxPerFolderBytes)
        {
            return Task.FromResult(new UserQuotaCheckResult(
                Allowed: false,
                FolderBytes: folderBytes,
                TotalBytes: -1,
                AvailableDiskBytes: TryGetAvailableDiskBytes(rootPath),
                DenyReason: $"per-folder limit ({_maxPerFolderBytes} bytes) exceeded"));
        }

        // Total quota across all user folders.
        var totalBytes = GetCurrentTotalBytes(rootPath);

        if (totalBytes + incomingBytes > _maxTotalBytes)
        {
            return Task.FromResult(new UserQuotaCheckResult(
                Allowed: false,
                FolderBytes: folderBytes,
                TotalBytes: totalBytes,
                AvailableDiskBytes: TryGetAvailableDiskBytes(rootPath),
                DenyReason: $"total quota ({_maxTotalBytes} bytes) exceeded"));
        }

        // Disk-space cross-check.
        var availableDisk = TryGetAvailableDiskBytes(rootPath);
        if (availableDisk >= 0 && incomingBytes > availableDisk)
        {
            return Task.FromResult(new UserQuotaCheckResult(
                Allowed: false,
                FolderBytes: folderBytes,
                TotalBytes: totalBytes,
                AvailableDiskBytes: availableDisk,
                DenyReason: "insufficient disk space"));
        }

        return Task.FromResult(new UserQuotaCheckResult(
            Allowed: true,
            FolderBytes: folderBytes,
            TotalBytes: totalBytes,
            AvailableDiskBytes: availableDisk,
            DenyReason: null));
    }

    /// <inheritdoc />
    public void InvalidateWalkCache(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return;
        lock (_cacheLock)
        {
            _folderCache.Remove(folderName);
        }
    }

    private long GetCurrentFolderBytes(string storageRoot, string folderName)
    {
        var now = _clock.GetUtcNow();

        lock (_cacheLock)
        {
            if (_folderCache.TryGetValue(folderName, out var slot)
                && now - slot.At < WalkCacheTtl)
            {
                return slot.Bytes;
            }
        }

        // Walk OUTSIDE the lock — slow, and we don't want to serialize
        // concurrent quota checks against the same stale state.
        var folderPath = Path.Combine(storageRoot, folderName);
        long bytes = 0;
        if (Directory.Exists(folderPath))
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(
                             folderPath, "*", SearchOption.AllDirectories))
                {
                    try { bytes += new FileInfo(f).Length; }
                    catch (FileNotFoundException) { /* race: deleted mid-walk */ }
                    catch (UnauthorizedAccessException) { /* skip */ }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                _logger.LogWarning(ex,
                    "User-folder quota walk failed under '{Folder}'. Treating as 0 — " +
                    "the per-folder ceiling and AvailableFreeSpace check still bound writes.",
                    folderPath);
                bytes = 0;
            }
        }

        lock (_cacheLock)
        {
            _folderCache[folderName] = (bytes, now);
        }

        return bytes;
    }

    private long GetCurrentTotalBytes(string storageRoot)
    {
        // Total = sum of every direct subfolder under storageRoot that
        // isn't a scope subfolder. Each sub-walk is cached per-folder so
        // a hot folder doesn't pay the walk cost twice.
        if (!Directory.Exists(storageRoot)) return 0;

        long total = 0;
        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(storageRoot);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex,
                "User-folder total quota walk could not enumerate '{Root}'. " +
                "Treating as 0 — disk-space cross-check still bounds writes.",
                storageRoot);
            return 0;
        }

        foreach (var child in children)
        {
            var name = Path.GetFileName(child);
            if (string.IsNullOrEmpty(name) || ExcludedFolderNames.Contains(name))
                continue;
            total += GetCurrentFolderBytes(storageRoot, name);
        }
        return total;
    }

    private static long TryGetAvailableDiskBytes(string storageRoot)
    {
        try
        {
            var driveLetter = Path.GetPathRoot(Path.GetFullPath(storageRoot));
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

// Storage W-4 (Drummond W-4 AC2) — quota enforcement seam for user folders.
//
// Mirrors the IModelStorageQuota contract from W-3 with two W-4-specific
// shapes:
//
//   1. Per-folder ceiling (5 GB default) — the operator's UI-visible cap on
//      a single folder. The total ceiling (25 GB default) is the global
//      brake against runaway usage across all user folders.
//
//   2. InvalidateWalkCache(string folderName) is on the interface from day
//      one (lesson learned from W-3 deviation #4 — we shipped the equivalent
//      method on the implementation and ate the `if (_quota is concrete)`
//      cast in the coordinator). Per-folder invalidation lets upstream
//      write paths invalidate just one folder's cache without busting the
//      total-walk cache.
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawNet.Storage;

/// <summary>
/// Pre-flight quota check for writes landing under a user folder.
/// Implementations MUST be cheap to call repeatedly (every upload routes
/// through this seam). Use a short-lived cache for the directory-walk
/// result; never block the upload stream itself.
/// </summary>
public interface IUserFolderQuota
{
    /// <summary>
    /// Decides whether a write of <paramref name="incomingBytes"/> may
    /// proceed into <paramref name="folderName"/>. The check enforces:
    ///   - per-folder ceiling (default 5 GB)
    ///   - total quota across all user folders (default 25 GB)
    ///   - <see cref="System.IO.DriveInfo.AvailableFreeSpace"/> on the
    ///     storage drive
    /// in that order.
    /// </summary>
    /// <param name="folderName">
    /// The bare folder name (validated by
    /// <see cref="OpenClawNetPaths.ResolveSafeUserFolderPath"/>) the
    /// upload will land in.
    /// </param>
    /// <param name="incomingBytes">Expected size of the incoming write.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<UserQuotaCheckResult> CheckAsync(
        string folderName,
        long incomingBytes,
        CancellationToken ct = default);

    /// <summary>
    /// Invalidates the directory-walk cache for a single folder.
    /// Upstream write paths (gateway upload endpoint, future
    /// <c>UserFolderWriteCoordinator</c>) MUST call this after every
    /// successful write so the next quota check sees the new bytes
    /// without waiting for the TTL.
    /// </summary>
    /// <remarks>
    /// On the interface from day one — Drummond W-4 deviation-prevention
    /// (W-3 deviation #4 sunset). Implementations whose cache is global
    /// MAY treat this as a full invalidation; the contract only requires
    /// that the named folder's next check be accurate.
    /// </remarks>
    void InvalidateWalkCache(string folderName);
}

/// <summary>
/// Outcome of <see cref="IUserFolderQuota.CheckAsync"/>. On
/// <c>Allowed=false</c>, <see cref="DenyReason"/> carries one of the
/// canonical strings: <c>"per-folder limit (N) exceeded"</c>,
/// <c>"total quota (N) exceeded"</c>, or <c>"insufficient disk space"</c>.
/// </summary>
public sealed record UserQuotaCheckResult(
    bool Allowed,
    long FolderBytes,
    long TotalBytes,
    long AvailableDiskBytes,
    string? DenyReason);

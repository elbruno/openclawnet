// Storage W-3 (Drummond AC2) — quota enforcement seam for the models root.
//
// The models root is the single largest write surface in the product.
// Without enforcement, a runaway HuggingFace pull or a misbehaving Ollama
// adapter can brick a developer laptop within minutes. This seam is checked
// BEFORE the destination stream is opened by ModelDownloadCoordinator
// (fail-closed): no allowance, no .tmp file is even created.
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawNet.Storage;

/// <summary>
/// Pre-flight quota check for downloads landing under the models root.
/// Implementations MUST be cheap to call repeatedly (the coordinator may
/// invoke this on every download). Use a short-lived cache for the
/// directory-walk result; never block the download stream itself.
/// </summary>
public interface IModelStorageQuota
{
    /// <summary>
    /// Decides whether a download of <paramref name="incomingBytes"/> may
    /// proceed under <paramref name="modelsRoot"/>.
    /// </summary>
    /// <param name="modelsRoot">
    /// Absolute path to the models root (typically <see cref="OpenClawNetPaths.ResolveModelsRoot"/>).
    /// </param>
    /// <param name="incomingBytes">
    /// Expected size of the incoming download. The check enforces both a
    /// per-file ceiling and a total-quota ceiling, and cross-checks
    /// <see cref="System.IO.DriveInfo.AvailableFreeSpace"/> for the drive.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<QuotaCheckResult> CheckAsync(
        string modelsRoot,
        long incomingBytes,
        CancellationToken ct = default);
}

/// <summary>
/// Outcome of <see cref="IModelStorageQuota.CheckAsync"/>. On
/// <c>Allowed=false</c>, <see cref="DenyReason"/> carries one of the
/// canonical strings: <c>"per-file limit (N) exceeded"</c>,
/// <c>"total quota (N) exceeded"</c>, or <c>"insufficient disk space"</c>.
/// </summary>
public sealed record QuotaCheckResult(
    bool Allowed,
    long CurrentTotalBytes,
    long AvailableDiskBytes,
    string? DenyReason);

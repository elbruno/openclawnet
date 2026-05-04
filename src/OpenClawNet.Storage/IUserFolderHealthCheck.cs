// Storage W-4 (Drummond W-4 AC4 / P1 #4) — boot-time reparse-point sweep
// over existing user folders.
//
// W-3 deviation #2 carried forward: ResolveSafeUserFolderPath routes through
// ISafePathResolver.ResolveSafePath on every call, which runs
// EnsureNoReparsePointEscape on the resolved path — that's the per-call
// guard.
//
// This seam is the BOOT-TIME complement: at startup, walk every existing
// subfolder under {Root} (excluding agents/, models/, skills/) and verify
// none of them is a reparse point pointing outside the storage root. The
// SMB-share threat model recorded in Drummond's W-3 verdict ("an SMB share
// or container volume mounted at {storage} that the operator doesn't fully
// trust") becomes concrete in W-4 because user folders are now operator-
// reachable via the gateway endpoints.
//
// Boot-time semantics (mirrors IStorageAclVerifier):
//   * WARN per finding, NEVER deletes.
//   * Implementation MUST NOT throw on a hostile reparse point — the
//     verdict belongs in HealthCheckResult.Findings so the caller can
//     decide whether the failure is fatal (today: WARN-and-continue).
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawNet.Storage;

/// <summary>
/// Result of a single <see cref="IUserFolderHealthCheck.SweepAsync"/> run.
/// </summary>
/// <param name="StorageRoot">Resolved storage root that was swept.</param>
/// <param name="FoldersInspected">
/// Count of subfolders under <paramref name="StorageRoot"/> that were
/// inspected (excludes <c>agents/</c>, <c>models/</c>, <c>skills/</c>,
/// <c>binary/</c>).
/// </param>
/// <param name="Findings">
/// Human-readable WARN-level diagnostics, one per suspicious folder.
/// MUST NOT echo unredacted user input — the resolved subfolder paths
/// are operator-supplied and safe to log; anything attacker-controlled
/// belongs at DEBUG only.
/// </param>
public sealed record UserFolderHealthCheckResult(
    string StorageRoot,
    int FoldersInspected,
    IReadOnlyList<string> Findings)
{
    public bool HasFindings => Findings.Count > 0;

    public static UserFolderHealthCheckResult Clean(string storageRoot, int foldersInspected) =>
        new(storageRoot, foldersInspected, Array.Empty<string>());
}

/// <summary>
/// Boot-time sweep that verifies no existing user folder under the
/// storage root is a reparse point pointing outside the root. Drummond
/// W-4 AC4 binding criterion — closes the residual filesystem-state
/// attack class recorded in W-3 deviation #2 for the user-folder surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>Boot wiring:</b> a single call against the resolved storage root
/// runs in <c>Program.cs</c> AFTER <see cref="IStorageAclVerifier"/>
/// (the ACL probe is the gating check; this sweep is advisory).
/// </para>
/// <para>
/// <b>Threading:</b> implementations MUST be safe to call from the
/// host's startup thread. The <see cref="Task"/> return is for future
/// probes that may need async I/O.
/// </para>
/// </remarks>
public interface IUserFolderHealthCheck
{
    /// <summary>
    /// Sweeps every direct subfolder of <paramref name="storageRoot"/>
    /// (excluding the well-known scope subfolders <c>agents/</c>,
    /// <c>models/</c>, <c>skills/</c>, <c>binary/</c>) and records a
    /// finding per subfolder whose <see cref="System.IO.FileAttributes.ReparsePoint"/>
    /// flag is set. Implementations MUST NOT throw on a bad finding —
    /// the verdict belongs in <see cref="UserFolderHealthCheckResult.Findings"/>.
    /// </summary>
    Task<UserFolderHealthCheckResult> SweepAsync(string storageRoot, CancellationToken ct = default);
}

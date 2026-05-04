// Storage W-2 — H-7 ACL verifier seam (Drummond W-1 verdict, P0 #1).
//
// Boot-time semantics (Q2, locked):
//   * Auto-create + warn-and-continue on the storage ROOT itself (a wrong
//     ACL on the root is operator-fixable and shouldn't block startup).
//   * Refuse to start credential services on a bad
//     `dataprotection-keys/` ACL (the implementation that ships in a
//     future wave will throw from the boot path so the host fails fast).
//
// W-2 ships the contract + a no-op Windows stub + DI registration + the
// boot-time call site. The real ACL probe is a future wave.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawNet.Storage;

/// <summary>
/// Result of a single <see cref="IStorageAclVerifier.VerifyAsync"/> probe.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IsSecure"/> is the boolean the boot path keys off. When the
/// caller is the root storage directory, a <c>false</c> result triggers a
/// WARN-and-continue (the operator can re-tighten ACLs on the running
/// host). When the caller is a credential subdirectory (e.g.
/// <c>dataprotection-keys/</c>), a <c>false</c> result MUST cause the
/// host to fail fast.
/// </para>
/// <para>
/// <see cref="Findings"/> carries human-readable diagnostics suitable for
/// emission at WARN/ERROR. Items MUST NOT echo unredacted user input —
/// the resolved <see cref="ScopeRoot"/> is operator-supplied and safe to
/// log; anything attacker-controlled belongs at DEBUG only.
/// </para>
/// </remarks>
public sealed record AclVerificationResult(
    bool IsSecure,
    IReadOnlyList<string> Findings,
    string ScopeRoot)
{
    /// <summary>Convenience factory for a clean, no-findings success result.</summary>
    public static AclVerificationResult Secure(string scopeRoot) =>
        new(IsSecure: true, Findings: Array.Empty<string>(), ScopeRoot: scopeRoot);
}

/// <summary>
/// Verifies that the on-disk ACL of a storage scope matches the locked
/// security policy (current user has full control; inheritance disabled
/// where appropriate; no world-writable bits on POSIX).
/// </summary>
/// <remarks>
/// <para>
/// <b>Boot wiring:</b> a single call against the resolved storage root
/// runs in <c>Program.cs</c> BEFORE
/// <c>AddDataProtection().PersistKeysToFileSystem(...)</c>. Future waves
/// will add a second call against the <c>dataprotection-keys/</c>
/// subdirectory whose <see cref="AclVerificationResult.IsSecure"/>
/// failure path throws (per Q2 — refuse to start on bad credential ACL).
/// </para>
/// <para>
/// <b>Threading:</b> implementations MUST be safe to call from the
/// host's startup thread. They SHOULD complete synchronously (the
/// <see cref="Task"/> return is for future probes that may need async
/// I/O).
/// </para>
/// </remarks>
public interface IStorageAclVerifier
{
    /// <summary>
    /// Probes the ACL on <paramref name="scopeRoot"/> and returns the
    /// verdict. Implementations MUST NOT throw on a bad ACL — the verdict
    /// belongs in <see cref="AclVerificationResult.IsSecure"/> so the
    /// caller can decide whether the failure is fatal (credential dirs)
    /// or recoverable (root dir, WARN-and-continue).
    /// </summary>
    /// <param name="scopeRoot">
    /// Absolute path to the directory whose ACL is being probed. Must
    /// already exist on disk.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<AclVerificationResult> VerifyAsync(string scopeRoot, CancellationToken ct = default);
}

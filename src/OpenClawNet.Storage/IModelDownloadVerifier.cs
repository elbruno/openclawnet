// Storage W-3 (Drummond AC1) — SHA-256 verification seam for model downloads.
//
// Every byte that lands under OpenClawNetPaths.ResolveModelsRoot() MUST pass
// through an IModelDownloadVerifier. No digest = no download (fail-closed).
// On mismatch, IsValid is false and FailureReason carries the audit string —
// the caller (ModelDownloadCoordinator) is responsible for ensuring the
// verified bytes never land at the final path on a failed verification.
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawNet.Storage;

/// <summary>
/// Verifies a model download stream against an operator-supplied (or
/// registry-supplied) SHA-256 digest and expected byte length. Implementations
/// MUST be fail-closed: a non-matching digest, byte-count mismatch, or any
/// stream read failure produces <c>IsValid=false</c> with a populated
/// <see cref="ModelDownloadVerificationResult.FailureReason"/>.
/// </summary>
public interface IModelDownloadVerifier
{
    /// <summary>
    /// Hashes <paramref name="content"/> end-to-end (consuming it), compares
    /// against <paramref name="expectedSha256Hex"/> (case-insensitive 64-char
    /// hex) and <paramref name="expectedBytes"/>, and returns the verdict.
    /// </summary>
    /// <param name="content">
    /// The download stream. The verifier reads to end; the caller is
    /// responsible for disposing the stream.
    /// </param>
    /// <param name="expectedSha256Hex">
    /// 64-character lowercase or uppercase hex digest. Whitespace is rejected.
    /// </param>
    /// <param name="expectedBytes">
    /// Expected length, used for the cross-check that pairs with quota
    /// enforcement.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<ModelDownloadVerificationResult> VerifyAsync(
        Stream content,
        string expectedSha256Hex,
        long expectedBytes,
        CancellationToken ct = default);
}

/// <summary>
/// Outcome of <see cref="IModelDownloadVerifier.VerifyAsync"/>. On
/// <c>IsValid=true</c> the actual hash and byte count match the expected
/// values. On <c>IsValid=false</c>, <see cref="FailureReason"/> carries a
/// short, audit-safe message (no raw bytes, no PII) — for example
/// <c>"sha256 mismatch: expected X, got Y"</c> or
/// <c>"byte count mismatch: expected N, got M"</c>.
/// </summary>
/// <param name="IsValid">True when both digest and byte count match.</param>
/// <param name="ActualSha256Hex">Lowercase hex digest computed over the stream.</param>
/// <param name="ActualBytes">Total bytes read from the stream.</param>
/// <param name="FailureReason">
/// Null on success; a short audit string on failure. NEVER contains raw
/// downloaded bytes or any user-controlled URL.
/// </param>
public sealed record ModelDownloadVerificationResult(
    bool IsValid,
    string ActualSha256Hex,
    long ActualBytes,
    string? FailureReason);

// Storage W-3 (Drummond AC1) — default SHA-256 verifier for model downloads.
//
// Streams the input through SHA256.Create() in 64KiB chunks, compares the
// resulting digest case-insensitively against the expected hex string, and
// also cross-checks the actual byte count against the expected value (paired
// with quota enforcement in ModelDownloadCoordinator).
//
// Fail-closed: ANY mismatch returns IsValid=false. The caller (the
// coordinator) MUST NEVER promote bytes from .tmp to the final path on a
// failed verification — that contract is enforced at the call site.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenClawNet.Storage;

/// <summary>
/// Default <see cref="IModelDownloadVerifier"/> implementation using SHA-256.
/// Stateless and thread-safe; safe to register as a singleton.
/// </summary>
public sealed class Sha256ModelDownloadVerifier : IModelDownloadVerifier
{
    private const int BufferSize = 64 * 1024;

    private readonly ILogger<Sha256ModelDownloadVerifier> _logger;

    /// <summary>Parameterless ctor — uses NullLogger. Convenient for tests.</summary>
    public Sha256ModelDownloadVerifier()
        : this(NullLogger<Sha256ModelDownloadVerifier>.Instance) { }

    public Sha256ModelDownloadVerifier(ILogger<Sha256ModelDownloadVerifier> logger)
    {
        _logger = logger ?? NullLogger<Sha256ModelDownloadVerifier>.Instance;
    }

    /// <inheritdoc />
    public async Task<ModelDownloadVerificationResult> VerifyAsync(
        Stream content,
        string expectedSha256Hex,
        long expectedBytes,
        CancellationToken ct = default)
    {
        if (content is null)
            return new ModelDownloadVerificationResult(false, "", 0, "content stream was null");

        if (string.IsNullOrWhiteSpace(expectedSha256Hex))
            return new ModelDownloadVerificationResult(false, "", 0, "expected sha256 was null/empty");

        // Strict shape: 64 hex characters. Anything else is fail-closed.
        if (expectedSha256Hex.Length != 64 || !IsHex(expectedSha256Hex))
            return new ModelDownloadVerificationResult(
                false, "", 0,
                $"expected sha256 is not a 64-char hex string (length={expectedSha256Hex.Length})");

        using var sha = SHA256.Create();
        var buffer = new byte[BufferSize];
        long total = 0;

        try
        {
            int read;
            while ((read = await content.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
            {
                sha.TransformBlock(buffer, 0, read, null, 0);
                total += read;
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SHA-256 verification failed: stream read error after {Bytes} bytes.", total);
            return new ModelDownloadVerificationResult(
                false, "", total, $"stream read failure: {ex.GetType().Name}");
        }

        var actualHex = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        var expectedHexLower = expectedSha256Hex.ToLowerInvariant();

        if (!string.Equals(actualHex, expectedHexLower, StringComparison.Ordinal))
        {
            // Audit message: short, no raw bytes, no URL. Hashes are safe to
            // log — they're not secret in this product.
            return new ModelDownloadVerificationResult(
                false, actualHex, total,
                $"sha256 mismatch: expected {expectedHexLower}, got {actualHex}");
        }

        if (total != expectedBytes)
        {
            return new ModelDownloadVerificationResult(
                false, actualHex, total,
                $"byte count mismatch: expected {expectedBytes}, got {total}");
        }

        return new ModelDownloadVerificationResult(true, actualHex, total, null);
    }

    private static bool IsHex(string s)
    {
        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9')
                  || (c >= 'a' && c <= 'f')
                  || (c >= 'A' && c <= 'F')))
                return false;
        }
        return true;
    }
}

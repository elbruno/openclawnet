// Storage W-3 (Drummond AC1+AC2) — orchestrator that combines name allowlist,
// quota check, atomic temp-write, and SHA-256 verification into a single
// sanctioned write path for downloads landing under the models root.
//
// Flow:
//   1. ResolveSafeModelPath(fileName)           — name + extension allowlist
//   2. IModelStorageQuota.CheckAsync(...)        — pre-flight quota
//   3. Stream → "{name}.tmp" (under models root) — atomic temp-file write
//   4. IModelDownloadVerifier.VerifyAsync(...)   — SHA-256 + byte cross-check
//   5a. valid    → File.Move(.tmp → final, overwrite: true)
//   5b. invalid  → delete .tmp; return failure with audit reason
//
// Fail-closed at every step. The .tmp staging area is the ONLY pre-final
// write surface; no caller should bypass this orchestrator and write
// directly into the models root. (Enforced by review per Drummond's W-3
// gate; future wave will add a static check.)
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenClawNet.Storage;

/// <summary>
/// Orchestrates a single sanctioned download into the models root with
/// quota, name, and SHA-256 enforcement. Stateless — safe as a singleton.
/// </summary>
public sealed class ModelDownloadCoordinator
{
    private readonly IModelDownloadVerifier _verifier;
    private readonly IModelStorageQuota _quota;
    private readonly ILogger<ModelDownloadCoordinator> _logger;

    public ModelDownloadCoordinator(
        IModelDownloadVerifier verifier,
        IModelStorageQuota quota,
        ILogger<ModelDownloadCoordinator>? logger = null)
    {
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _quota = quota ?? throw new ArgumentNullException(nameof(quota));
        _logger = logger ?? NullLogger<ModelDownloadCoordinator>.Instance;
    }

    /// <summary>
    /// Downloads <paramref name="sourceStream"/> into the models root under
    /// <paramref name="fileName"/>, enforcing the W-3 contract. Returns a
    /// <see cref="ModelDownloadResult"/> with the final path on success or
    /// a populated <see cref="ModelDownloadResult.FailureReason"/> on any
    /// failure. Throws <see cref="UnsafePathException"/> only for name-allowlist
    /// rejections (the contract treats those as caller bugs, not download
    /// failures).
    /// </summary>
    public async Task<ModelDownloadResult> DownloadAsync(
        string fileName,
        Stream sourceStream,
        string expectedSha256Hex,
        long expectedBytes,
        CancellationToken ct = default)
    {
        if (sourceStream is null) throw new ArgumentNullException(nameof(sourceStream));

        // Step 1 — name allowlist + extension. Throws UnsafePathException
        // (contract: caller bug). On success, the models root is created
        // and ACL-hardened as a side-effect.
        var finalPath = OpenClawNetPaths.ResolveSafeModelPath(fileName, _logger);
        var modelsRoot = Path.GetDirectoryName(finalPath)!;
        var tempPath = finalPath + ".tmp";

        // Step 2 — pre-flight quota.
        var quotaResult = await _quota.CheckAsync(modelsRoot, expectedBytes, ct).ConfigureAwait(false);
        if (!quotaResult.Allowed)
        {
            _logger.LogWarning(
                "Model download '{FileName}' DENIED by quota: {Reason} " +
                "(currentTotal={Current}, available={Available}).",
                fileName, quotaResult.DenyReason,
                quotaResult.CurrentTotalBytes, quotaResult.AvailableDiskBytes);
            return new ModelDownloadResult(false, null, quotaResult.DenyReason);
        }

        // If a stale .tmp from a prior crashed attempt is sitting there,
        // remove it. The whole point of the .tmp suffix is that it's
        // disposable scratch; never trust its contents.
        if (File.Exists(tempPath))
        {
            try { File.Delete(tempPath); }
            catch (IOException ex)
            {
                _logger.LogWarning(ex,
                    "Could not remove stale temp file '{TempPath}'. Continuing — " +
                    "the open-with-Truncate below will overwrite it.", tempPath);
            }
        }

        var sw = Stopwatch.StartNew();
        _logger.LogInformation(
            "Model download started: '{FileName}' (expectedBytes={Bytes}).",
            fileName, expectedBytes);

        // Step 3 — stream to .tmp.
        long bytesWritten = 0;
        try
        {
            using (var dest = new FileStream(
                       tempPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 64 * 1024,
                       useAsync: true))
            {
                bytesWritten = await sourceStream.CopyToAsyncCounted(dest, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            SafeDelete(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            SafeDelete(tempPath);
            _logger.LogError(ex,
                "Model download failed during stream-to-temp for '{FileName}' " +
                "(bytesWritten={Bytes}).", fileName, bytesWritten);
            return new ModelDownloadResult(false, null,
                $"stream-to-temp failure: {ex.GetType().Name}");
        }

        // Step 4 — SHA-256 + byte cross-check, reading the freshly written
        // temp file. We re-read so the hash is computed over the bytes
        // ACTUALLY landed on disk — defends against in-flight corruption
        // between source and disk.
        ModelDownloadVerificationResult verification;
        try
        {
            using var verifyStream = new FileStream(
                tempPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                useAsync: true);
            verification = await _verifier
                .VerifyAsync(verifyStream, expectedSha256Hex, expectedBytes, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            SafeDelete(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            SafeDelete(tempPath);
            _logger.LogError(ex,
                "Model download verifier threw for '{FileName}'. Treating as invalid (fail-closed).",
                fileName);
            return new ModelDownloadResult(false, null,
                $"verifier failure: {ex.GetType().Name}");
        }

        if (!verification.IsValid)
        {
            SafeDelete(tempPath);
            // Audit: log the structured reason but NEVER the bytes themselves.
            _logger.LogError(
                "Model download verification FAILED for '{FileName}': {Reason}.",
                fileName, verification.FailureReason);
            return new ModelDownloadResult(false, null, verification.FailureReason);
        }

        // Step 5 — atomic rename.
        try
        {
            File.Move(tempPath, finalPath, overwrite: true);
        }
        catch (Exception ex)
        {
            SafeDelete(tempPath);
            _logger.LogError(ex,
                "Model download atomic rename failed for '{FileName}' " +
                "({TempPath} → {FinalPath}).", fileName, tempPath, finalPath);
            return new ModelDownloadResult(false, null,
                $"atomic rename failure: {ex.GetType().Name}");
        }

        // Invalidate the quota cache so the next CheckAsync sees this
        // file's bytes against the total ceiling.
        if (_quota is ModelStorageQuota concrete)
            concrete.InvalidateWalkCache();

        sw.Stop();
        _logger.LogInformation(
            "Model download completed: '{FileName}' ({Bytes} bytes in {Ms} ms).",
            fileName, verification.ActualBytes, sw.ElapsedMilliseconds);

        return new ModelDownloadResult(true, finalPath, null);
    }

    private void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to clean up temp file '{Path}'. Manual cleanup may be required.", path);
        }
    }
}

/// <summary>
/// Outcome of <see cref="ModelDownloadCoordinator.DownloadAsync"/>.
/// On <c>Success=true</c>, <see cref="FinalPath"/> is the absolute on-disk
/// path of the verified file. On failure, <see cref="FailureReason"/> carries
/// a short audit string (no PII, no raw bytes).
/// </summary>
public sealed record ModelDownloadResult(
    bool Success,
    string? FinalPath,
    string? FailureReason);

/// <summary>Internal helpers for stream copy with byte counting.</summary>
internal static class CoordinatorStreamExtensions
{
    public static async Task<long> CopyToAsyncCounted(
        this Stream source, Stream dest, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            total += read;
        }
        return total;
    }
}

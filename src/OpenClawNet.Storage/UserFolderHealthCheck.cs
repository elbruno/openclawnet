// Storage W-4 (Drummond W-4 AC4) — default IUserFolderHealthCheck.
//
// Walks the immediate children of {storageRoot}, excluding the well-known
// scope subfolders (agents/, models/, skills/, binary/). For each direct
// subfolder, checks the FileAttributes.ReparsePoint flag and resolves the
// link target if set, recording a finding when the target escapes the
// storage root.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenClawNet.Storage;

/// <summary>
/// Default <see cref="IUserFolderHealthCheck"/> implementation.
/// </summary>
public sealed class UserFolderHealthCheck : IUserFolderHealthCheck
{
    // Scope subfolders we skip — they have their own checks.
    // (agents/ → per-agent ACL; models/ → ResolveSafeModelPath; skills/ →
    // FileSkillLoader bundle scan; binary/ → tool-output scratch area.)
    private static readonly HashSet<string> ExcludedFolderNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "agents",
            "models",
            "skills",
            "binary",
            // Internal credentials directory — owned by DataProtection, not
            // a user folder; sweep should not touch it.
            "dataprotection-keys",
            // Audit drop-folder added in W-4 commit #4. Treat like a scope
            // subfolder so the sweep doesn't false-positive against itself.
            "audit",
        };

    private static readonly StringComparison PathComparison =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private readonly ILogger<UserFolderHealthCheck> _logger;

    public UserFolderHealthCheck() : this(NullLogger<UserFolderHealthCheck>.Instance) { }

    public UserFolderHealthCheck(ILogger<UserFolderHealthCheck> logger)
    {
        _logger = logger ?? NullLogger<UserFolderHealthCheck>.Instance;
    }

    /// <inheritdoc />
    public Task<UserFolderHealthCheckResult> SweepAsync(string storageRoot, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(storageRoot))
            throw new ArgumentException("Storage root must be non-empty.", nameof(storageRoot));

        ct.ThrowIfCancellationRequested();

        if (!Directory.Exists(storageRoot))
        {
            // Nothing to sweep yet — first boot. Not an error.
            return Task.FromResult(UserFolderHealthCheckResult.Clean(storageRoot, foldersInspected: 0));
        }

        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(storageRoot));
        var findings = new List<string>();
        int inspected = 0;

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(normalizedRoot);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex,
                "User-folder health sweep could not enumerate '{Root}'. Skipping.",
                normalizedRoot);
            return Task.FromResult(UserFolderHealthCheckResult.Clean(normalizedRoot, foldersInspected: 0));
        }

        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();

            var name = Path.GetFileName(child);
            if (string.IsNullOrEmpty(name) || ExcludedFolderNames.Contains(name))
                continue;

            inspected++;

            DirectoryInfo info;
            try
            {
                info = new DirectoryInfo(child);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex,
                    "User-folder health sweep could not stat '{Path}'. Skipping.", child);
                continue;
            }

            if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
                continue;

            // Reparse point — resolve the target and check containment.
            string finding;
            try
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target is null)
                {
                    finding = $"Reparse point at '{child}' has no resolvable target.";
                }
                else
                {
                    var targetNormalized = Path.TrimEndingDirectorySeparator(
                        Path.GetFullPath(target.FullName));
                    var sep = Path.DirectorySeparatorChar;
                    var inside = string.Equals(targetNormalized, normalizedRoot, PathComparison)
                        || targetNormalized.StartsWith(normalizedRoot + sep, PathComparison);
                    if (inside)
                    {
                        // Reparse point pointing back inside the root — strange
                        // but not an escape. Record at INFO via the logger but
                        // don't add to findings.
                        _logger.LogInformation(
                            "User-folder '{Name}' is a reparse point pointing inside the storage root: '{Target}'.",
                            name, targetNormalized);
                        continue;
                    }

                    finding = $"User-folder '{name}' is a reparse point whose target escapes the storage root: '{targetNormalized}'.";
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                finding = $"User-folder '{name}' is a reparse point whose target could not be resolved: {ex.GetType().Name}.";
            }

            _logger.LogWarning(
                "User-folder health sweep finding (storage root '{Root}'): {Finding}",
                normalizedRoot, finding);
            findings.Add(finding);
        }

        return Task.FromResult(new UserFolderHealthCheckResult(normalizedRoot, inspected, findings));
    }
}

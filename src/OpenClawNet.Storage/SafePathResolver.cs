// Storage W-1 — ISafePathResolver implementation.
// Satisfies hardening invariants H-1..H-7 from the locked Wave 1 spec.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenClawNet.Storage;

/// <summary>
/// Single, unit-tested resolver for every tool path that derives from
/// untrusted input (LLM, HTTP body, AGENTS.md). Satisfies invariants
/// H-1 (fail-closed containment), H-2 (single sanitizer), H-3 (no
/// reparse-point escapes), H-4 (boundary-safe prefix check), and H-6
/// (per-agent scoping seam — caller passes the scope root, not just
/// <c>StorageOptions.RootPath</c>).
/// </summary>
public interface ISafePathResolver
{
    /// <summary>
    /// Resolves <paramref name="requestedPath"/> against
    /// <paramref name="scopeRoot"/>. Returns the absolute, fully-resolved
    /// path on success. Throws <see cref="UnsafePathException"/> if the
    /// resolution would escape <paramref name="scopeRoot"/> for any reason
    /// (traversal, reparse point, prefix collision, reserved name, etc.).
    /// </summary>
    string ResolveSafePath(string scopeRoot, string requestedPath);

    /// <summary>
    /// Non-throwing variant of <see cref="ResolveSafePath"/>. Returns
    /// <c>true</c> + resolved path on success, <c>false</c> + empty string
    /// on any failure. Must NEVER throw for an unsafe input — that's the
    /// whole point of the API.
    /// </summary>
    bool TryResolveSafePath(string scopeRoot, string requestedPath, out string resolved);
}

/// <summary>
/// Default <see cref="ISafePathResolver"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>H-2 single resolver:</b> no other code in the system should call
/// <see cref="Path.GetFullPath(string)"/> on user-controlled input — route it
/// through here.
/// </para>
/// <para>
/// <b>H-6 per-scope:</b> the <c>scopeRoot</c> argument is intentionally NOT
/// hard-coded to <see cref="StorageOptions.RootPath"/>. Callers pin requests
/// to a per-agent / per-tool subtree (e.g. <c>{Root}/agents/{name}</c>) so
/// one bad caller cannot read another agent's data even if the resolver
/// itself is correct.
/// </para>
/// <para>
/// All failures throw <see cref="UnsafePathException"/> — never coerce,
/// truncate, or rewrite (H-1).
/// </para>
/// </remarks>
public sealed class SafePathResolver : ISafePathResolver
{
    // H-5: safe segment charset. 1..64 chars, must start with [A-Za-z0-9].
    private static readonly Regex SafeSegmentRegex = new(
        @"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // H-5: Windows-reserved device names (case-insensitive, with or without extension).
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    // Path comparison is case-insensitive on Windows, case-sensitive elsewhere.
    private static readonly StringComparison PathComparison =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private readonly ILogger<SafePathResolver> _logger;

    /// <summary>Parameterless constructor — uses a no-op logger. Convenient for tests and callers without DI.</summary>
    public SafePathResolver() : this(NullLogger<SafePathResolver>.Instance) { }

    public SafePathResolver(ILogger<SafePathResolver> logger)
    {
        _logger = logger ?? NullLogger<SafePathResolver>.Instance;
    }

    /// <inheritdoc />
    public string ResolveSafePath(string scopeRoot, string requestedPath)
    {
        // ---- input validation ------------------------------------------------
        if (string.IsNullOrWhiteSpace(scopeRoot))
            throw new UnsafePathException(
                "Scope root must be a non-empty rooted path.",
                UnsafePathReason.EmptyOrWhitespace,
                scopeRoot: scopeRoot,
                requestedPath: requestedPath);

        if (requestedPath is null)
            throw new UnsafePathException(
                "Requested path is null.",
                UnsafePathReason.EmptyOrWhitespace,
                scopeRoot: scopeRoot,
                requestedPath: null);

        if (string.IsNullOrWhiteSpace(requestedPath))
            throw new UnsafePathException(
                "Requested path must be non-empty.",
                UnsafePathReason.EmptyOrWhitespace,
                scopeRoot: scopeRoot,
                requestedPath: requestedPath);

        // Reject control characters in raw input (NUL + everything < 0x20).
        // Done BEFORE Path.GetFullPath because the runtime may throw a less
        // descriptive ArgumentException, and we want fail-closed semantics.
        foreach (var ch in requestedPath)
        {
            if (ch < 0x20)
                throw new UnsafePathException(
                    "Requested path contains a control character.",
                    UnsafePathReason.InvalidName,
                    scopeRoot: scopeRoot,
                    requestedPath: requestedPath);
        }

        // ---- H-5 RAW segment validation (must precede normalization) --------
        // Path.GetFullPath on Windows silently TRIMS trailing dots and spaces
        // ("foo." → "foo", "foo " → "foo"), so the post-normalize check is
        // blind to those bypasses. We have to validate the raw segments first.
        // "." and ".." are allowed here because GetFullPath collapses them
        // safely; any actual escape is caught by the containment check below.
        //
        // Skip raw-segment validation for absolute/rooted paths: drive
        // letters ("C:") and UNC prefixes ("\\?\") fail the segment regex
        // by design, but those rejections belong to the containment check
        // (they'll fail H-1 / H-4 with a more accurate AbsolutePathOutsideScope
        // reason). For absolute paths the post-normalize segment check on
        // segments BELOW the scope still runs.
        if (!Path.IsPathRooted(requestedPath))
        {
            ValidateRawSegments(requestedPath, scopeRoot);
        }

        // ---- normalize the scope root once -----------------------------------
        string normalizedScope;
        try
        {
            normalizedScope = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scopeRoot));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            throw new UnsafePathException(
                $"Scope root is not a valid path: {ex.Message}",
                ex,
                UnsafePathReason.Other,
                scopeRoot: scopeRoot,
                requestedPath: requestedPath);
        }

        if (!Path.IsPathRooted(normalizedScope))
            throw new UnsafePathException(
                "Scope root must be an absolute (rooted) path.",
                UnsafePathReason.Other,
                scopeRoot: scopeRoot,
                requestedPath: requestedPath);

        // ---- combine + normalize the requested path --------------------------
        // Note: Path.GetFullPath collapses "..", ".", and mixed separators. If
        // requestedPath is absolute, GetFullPath ignores the scope root — that's
        // fine, we re-check containment immediately afterwards (H-1: fail closed).
        string combined;
        try
        {
            combined = Path.GetFullPath(Path.Combine(normalizedScope, requestedPath));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            throw new UnsafePathException(
                $"Requested path is not valid: {ex.Message}",
                ex,
                UnsafePathReason.InvalidName,
                scopeRoot: normalizedScope,
                requestedPath: requestedPath);
        }

        var normalizedCombined = Path.TrimEndingDirectorySeparator(combined);

        // ---- H-4 containment (separator-or-end check) ------------------------
        if (!IsWithinScope(normalizedScope, normalizedCombined))
        {
            // Distinguish absolute-input rejections (operator/LLM gave a fully
            // rooted path that landed elsewhere) from relative traversal — both
            // are containment failures, but audit triage cares about the
            // difference.
            var reason = Path.IsPathRooted(requestedPath)
                ? UnsafePathReason.AbsolutePathOutsideScope
                : (requestedPath.Contains("..") ? UnsafePathReason.Traversal : UnsafePathReason.OutsideScope);

            throw new UnsafePathException(
                "Requested path resolves outside the scope root.",
                reason,
                scopeRoot: normalizedScope,
                requestedPath: requestedPath);
        }

        // ---- H-5 segment-name validation -------------------------------------
        ValidateSegmentsBelowScope(normalizedScope, normalizedCombined, requestedPath);

        // ---- H-3 reparse-point escape check ----------------------------------
        EnsureNoReparsePointEscape(normalizedScope, normalizedCombined, requestedPath);

        return normalizedCombined;
    }

    /// <inheritdoc />
    public bool TryResolveSafePath(string scopeRoot, string requestedPath, out string resolved)
    {
        try
        {
            resolved = ResolveSafePath(scopeRoot, requestedPath);
            return true;
        }
        catch (UnsafePathException ex)
        {
            // Per H-8: log raw input at DEBUG (we're inside the resolver — no
            // user-facing surface here), return safe empty string.
            _logger.LogDebug(ex,
                "TryResolveSafePath rejected '{RequestedPath}' under '{ScopeRoot}'.",
                requestedPath, scopeRoot);
            resolved = string.Empty;
            return false;
        }
    }

    // ------------------------------------------------------------------ helpers

    private static bool IsWithinScope(string normalizedScope, string normalizedCandidate)
    {
        if (string.Equals(normalizedScope, normalizedCandidate, PathComparison))
            return true;

        // Both inputs are already trimmed of trailing separators. The candidate
        // must equal the scope OR start with "scope + separator".
        var sep = Path.DirectorySeparatorChar;
        return normalizedCandidate.Length > normalizedScope.Length
            && normalizedCandidate[normalizedScope.Length] == sep
            && normalizedCandidate.AsSpan(0, normalizedScope.Length).Equals(
                   normalizedScope.AsSpan(), PathComparison);
    }

    private static void ValidateRawSegments(string requestedPath, string? scopeRoot)
    {
        var segments = requestedPath.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var seg in segments)
        {
            // "." and ".." are OS-handled traversal segments — allow them
            // here; Path.GetFullPath collapses them, and the containment
            // check below catches any actual escape.
            if (seg == "." || seg == "..") continue;

            ValidateSegmentName(seg, scopeRoot, requestedPath);
        }
    }

    private static void ValidateSegmentsBelowScope(string normalizedScope, string normalizedCombined, string requestedPath)
    {
        if (string.Equals(normalizedScope, normalizedCombined, PathComparison))
            return;

        // Tail = portion AFTER scope + separator.
        var tail = normalizedCombined[(normalizedScope.Length + 1)..];
        var segments = tail.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var seg in segments)
        {
            ValidateSegmentName(seg, normalizedScope, requestedPath);
        }
    }

    /// <summary>
    /// Throws <see cref="UnsafePathException"/> with a precise
    /// <see cref="UnsafePathReason"/> if <paramref name="name"/> fails the
    /// safe-name policy. Distinguishes <see cref="UnsafePathReason.ReservedName"/>
    /// from generic <see cref="UnsafePathReason.InvalidName"/> for audit triage.
    /// </summary>
    private static void ValidateSegmentName(string name, string? scopeRoot, string? requestedPath)
    {
        if (string.IsNullOrEmpty(name))
            throw new UnsafePathException(
                "Path segment is empty.",
                UnsafePathReason.InvalidName,
                scopeRoot: scopeRoot,
                requestedPath: requestedPath);

        // H-5: leading/trailing dot or space — Windows trims these silently
        // which is a classic bypass vector.
        if (name[0] == '.' || name[0] == ' ' || name[^1] == '.' || name[^1] == ' ')
            throw new UnsafePathException(
                $"Path segment '{name}' violates the safe-name policy.",
                UnsafePathReason.InvalidName,
                scopeRoot: scopeRoot,
                requestedPath: requestedPath);

        if (!SafeSegmentRegex.IsMatch(name))
            throw new UnsafePathException(
                $"Path segment '{name}' violates the safe-name policy.",
                UnsafePathReason.InvalidName,
                scopeRoot: scopeRoot,
                requestedPath: requestedPath);

        // Reserved-name check applies to the stem (before the first dot).
        var dot = name.IndexOf('.');
        var stem = dot < 0 ? name : name[..dot];
        if (ReservedNames.Contains(stem))
            throw new UnsafePathException(
                $"Path segment '{name}' uses a reserved Windows device name.",
                UnsafePathReason.ReservedName,
                scopeRoot: scopeRoot,
                requestedPath: requestedPath);
    }

    /// <summary>
    /// Boolean variant retained for legacy callers (kept for API stability).
    /// New code paths should use <see cref="ValidateSegmentName"/> so the
    /// throw carries a precise <see cref="UnsafePathReason"/>.
    /// </summary>
    private static bool IsValidSegmentName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name[0] == '.' || name[0] == ' ') return false;
        if (name[^1] == '.' || name[^1] == ' ') return false;
        if (!SafeSegmentRegex.IsMatch(name)) return false;

        var dot = name.IndexOf('.');
        var stem = dot < 0 ? name : name[..dot];
        return !ReservedNames.Contains(stem);
    }

    private static void EnsureNoReparsePointEscape(string normalizedScope, string normalizedCombined, string requestedPath)
    {
        if (string.Equals(normalizedScope, normalizedCombined, PathComparison))
            return;

        var tail = normalizedCombined[(normalizedScope.Length + 1)..];
        var segments = tail.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        var current = normalizedScope;
        foreach (var seg in segments)
        {
            current = Path.Combine(current, seg);

            // Only existing segments can be reparse points. Non-existing
            // segments will be created later by the caller.
            FileSystemInfo? info = null;
            try
            {
                if (Directory.Exists(current))
                    info = new DirectoryInfo(current);
                else if (File.Exists(current))
                    info = new FileInfo(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Probe failure: don't open an escape window — skip and let the
                // caller's later I/O fail closed if the segment is hostile.
                continue;
            }

            if (info is null) continue;
            if ((info.Attributes & FileAttributes.ReparsePoint) == 0) continue;

            // It IS a reparse point. Resolve to the FINAL target and verify
            // containment within the scope root (H-3).
            FileSystemInfo? finalTarget;
            try
            {
                finalTarget = info.ResolveLinkTarget(returnFinalTarget: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new UnsafePathException(
                    $"Reparse point at '{current}' could not be resolved.",
                    ex,
                    UnsafePathReason.ReparsePointEscape,
                    scopeRoot: normalizedScope,
                    requestedPath: requestedPath);
            }

            if (finalTarget is null)
            {
                throw new UnsafePathException(
                    $"Reparse point at '{current}' has no resolvable target.",
                    UnsafePathReason.ReparsePointEscape,
                    scopeRoot: normalizedScope,
                    requestedPath: requestedPath);
            }

            string finalNormalized;
            try
            {
                finalNormalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(finalTarget.FullName));
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
            {
                throw new UnsafePathException(
                    $"Reparse-point target at '{current}' is not a valid path: {ex.Message}",
                    ex,
                    UnsafePathReason.ReparsePointEscape,
                    scopeRoot: normalizedScope,
                    requestedPath: requestedPath);
            }

            if (!IsWithinScope(normalizedScope, finalNormalized))
            {
                throw new UnsafePathException(
                    $"Reparse point at '{current}' escapes the scope root.",
                    UnsafePathReason.ReparsePointEscape,
                    scopeRoot: normalizedScope,
                    requestedPath: requestedPath);
            }
        }
    }
}

/// <summary>
/// Categorizes WHY a path was rejected by <see cref="ISafePathResolver"/>.
/// Carried on <see cref="UnsafePathException.Reason"/> so audit emission
/// (W-2 H-8) can group rejections without parsing exception messages,
/// which are PII-shaped (they may echo attacker-controlled raw input).
/// </summary>
public enum UnsafePathReason
{
    /// <summary>Default / legacy / unspecified — pre-W-2 callers and
    /// anything that doesn't fit the more specific buckets.</summary>
    Other = 0,

    /// <summary>The resolved absolute path landed outside the scope root
    /// (containment check, H-1 / H-4).</summary>
    OutsideScope = 1,

    /// <summary>The input contained <c>..</c> traversal that escaped the
    /// scope root after normalization.</summary>
    Traversal = 2,

    /// <summary>A junction or symlink in the resolved path pointed
    /// outside the scope root (H-3).</summary>
    ReparsePointEscape = 3,

    /// <summary>A path segment failed the safe-name allowlist
    /// (charset, leading/trailing dot, control character, length, H-5).</summary>
    InvalidName = 4,

    /// <summary>A path segment matched a Windows reserved device name
    /// (CON, PRN, AUX, NUL, COM1-9, LPT1-9, H-5).</summary>
    ReservedName = 5,

    /// <summary>The input was null, empty, or whitespace.</summary>
    EmptyOrWhitespace = 6,

    /// <summary>The input was an absolute path that resolved outside the
    /// scope root (a common rejection class kept distinct from generic
    /// <see cref="OutsideScope"/> for audit triage).</summary>
    AbsolutePathOutsideScope = 7,

    /// <summary>W-3: a model file name failed the strict model-name
    /// allowlist (charset + extension allowlist of
    /// <c>gguf|safetensors|onnx|bin</c>). Distinct from generic
    /// <see cref="InvalidName"/> so the W-3 audit story can group
    /// model-naming rejections separately from H-5 segment rejections.</summary>
    InvalidModelName = 8,

    /// <summary>W-4 (Drummond W-4 AC1): a user-folder name failed the
    /// strict per-user-folder allowlist
    /// (<c>^[a-z0-9][a-z0-9._-]{0,63}$</c> — lowercase-first, no
    /// extension, segment-cap 64). Distinct from generic
    /// <see cref="InvalidName"/> and from <see cref="InvalidModelName"/>
    /// so the W-4 audit story can group user-folder rejections — the
    /// first allowlist surface that's directly reachable from web-user
    /// input via the gateway endpoints — separately from operator-shaped
    /// H-5 / model-allowlist rejections.</summary>
    InvalidUserFolderName = 9,
}

/// <summary>
/// Thrown by <see cref="ISafePathResolver.ResolveSafePath"/> when the
/// requested path cannot be safely resolved under the supplied scope
/// root. The message MUST NOT echo the unresolved input verbatim into
/// user-facing surfaces (per H-8 — log the raw input at WARN, return a
/// safe message to callers).
/// </summary>
/// <remarks>
/// W-2: now carries <see cref="Reason"/>, <see cref="ScopeRoot"/>, and
/// <see cref="RequestedPath"/> so audit emission can categorize
/// rejections and reconstruct the failed call site without parsing
/// exception messages.
/// </remarks>
public class UnsafePathException : Exception
{
    /// <summary>Why this path was rejected. Defaults to
    /// <see cref="UnsafePathReason.Other"/> for legacy 2-arg constructors.</summary>
    public UnsafePathReason Reason { get; }

    /// <summary>The scope root that <see cref="ISafePathResolver"/> was
    /// resolving against, or <c>null</c> if not available (e.g. the
    /// rejection happened before the scope was normalized).</summary>
    public string? ScopeRoot { get; }

    /// <summary>The original, untrusted requested path that was
    /// rejected. May be <c>null</c> for rejections that happen before the
    /// requested-path argument is even read. Audit emitters MUST treat
    /// this as attacker-controlled (do not log to user-facing surfaces;
    /// log to operator surfaces redacted or at DEBUG only).</summary>
    public string? RequestedPath { get; }

    public UnsafePathException(string message)
        : this(message, UnsafePathReason.Other, scopeRoot: null, requestedPath: null) { }

    public UnsafePathException(string message, Exception inner)
        : this(message, inner, UnsafePathReason.Other, scopeRoot: null, requestedPath: null) { }

    public UnsafePathException(
        string message,
        UnsafePathReason reason,
        string? scopeRoot = null,
        string? requestedPath = null)
        : base(message)
    {
        Reason = reason;
        ScopeRoot = scopeRoot;
        RequestedPath = requestedPath;
    }

    public UnsafePathException(
        string message,
        Exception inner,
        UnsafePathReason reason,
        string? scopeRoot = null,
        string? requestedPath = null)
        : base(message, inner)
    {
        Reason = reason;
        ScopeRoot = scopeRoot;
        RequestedPath = requestedPath;
    }
}

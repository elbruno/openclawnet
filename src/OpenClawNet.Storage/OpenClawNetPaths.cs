// Storage W-1 — locked contract (Q1, Q3, Q5).
//   * Default root: C:\openclawnet (Windows) — NO trailing /storage suffix.
//   * Env var:  OPENCLAWNET_STORAGE_ROOT  — overrides default.
//   * appsettings:  Storage:RootPath  — overrides default but NOT env var.
//   * Legacy var:  OPENCLAW_STORAGE_DIR  — explicitly IGNORED (one-time WARN).
//   * Resolved root + source label MUST be logged at INFO on startup.
// Storage W-2 — per-scope subfolder helpers (Drummond P1 #6):
//   * ResolveAgentRoot(name)  → {Root}/agents/{name}/
//   * ResolveModelsRoot()     → {Root}/models/
//   * ResolveUserRoot(folder) → {Root}/{folder}/
//   * All three: H-5 segment-name validation, ensure dir exists, restrictive
//     DACL on Windows (current user FullControl, inheritance disabled).
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenClawNet.Storage;

/// <summary>
/// Indicates where <see cref="OpenClawNetPaths.ResolveRoot"/> derived
/// the active storage root. Used in the startup log line so operators
/// can confirm the precedence chain without re-reading source.
/// </summary>
public enum StorageRootSource
{
    Default = 0,
    AppSettings = 1,
    EnvironmentVariable = 2,
}

/// <summary>
/// Resolves the OpenClawNet storage root with the locked precedence:
/// env var &gt; appsettings &gt; default. Single source of truth — every
/// caller (StorageOptions, AppHost, gateway boot) goes through here.
/// </summary>
public static class OpenClawNetPaths
{
    /// <summary>The supported environment variable name (Q5).</summary>
    public const string EnvironmentVariableName = "OPENCLAWNET_STORAGE_ROOT";

    /// <summary>
    /// The legacy variable that <b>must be ignored</b> (Q5). Exposed only
    /// so tests can assert it really is ignored.
    /// </summary>
    public const string LegacyEnvironmentVariableName = "OPENCLAW_STORAGE_DIR";

    /// <summary>
    /// Default root when neither env nor appsettings supply one.
    /// Windows: <c>C:\openclawnet</c> (Q1, Q3 — no /storage suffix).
    /// Other:   <c>~/openclawnet</c>.
    /// </summary>
    public static string DefaultRoot =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? @"C:\openclawnet"
            : System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "openclawnet");

    /// <summary>
    /// Resolves the active storage root and returns the source label.
    /// Logs <c>"Storage root resolved: '{Path}' (source: {Source})"</c> at
    /// INFO if <paramref name="logger"/> is supplied. If the legacy variable
    /// <see cref="LegacyEnvironmentVariableName"/> is set, a one-time WARN is
    /// emitted; the legacy value is NOT honored.
    /// </summary>
    /// <param name="appSettingsRootPath">
    /// Value of <c>Storage:RootPath</c> from configuration, or <c>null</c>/empty
    /// if not set.
    /// </param>
    /// <param name="logger">Optional logger for the boot-time INFO line.</param>
    public static (string Path, StorageRootSource Source) ResolveRoot(
        string? appSettingsRootPath = null,
        ILogger? logger = null)
    {
        // Legacy env var → one-time WARN. Value is NOT honored.
        var legacy = Environment.GetEnvironmentVariable(LegacyEnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(legacy))
        {
            logger?.LogWarning(
                "Legacy environment variable '{LegacyEnvVar}' is set but is no longer honored. " +
                "Use '{NewEnvVar}' instead. Ignored value: '{Value}'.",
                LegacyEnvironmentVariableName, EnvironmentVariableName, legacy);
        }

        string path;
        StorageRootSource source;

        // 1. Environment variable (highest precedence).
        var envValue = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            path = Normalize(envValue);
            source = StorageRootSource.EnvironmentVariable;
        }
        // 2. appsettings (Storage:RootPath).
        else if (!string.IsNullOrWhiteSpace(appSettingsRootPath))
        {
            path = Normalize(appSettingsRootPath);
            source = StorageRootSource.AppSettings;
        }
        // 3. Built-in default.
        else
        {
            path = Normalize(DefaultRoot);
            source = StorageRootSource.Default;
        }

        logger?.LogInformation(
            "Storage root resolved: '{Root}' (source: {Source})",
            path, source);

        return (path, source);
    }

    /// <summary>
    /// Normalizes a root path: trims trailing separators so callers comparing
    /// paths with <c>StringComparison.OrdinalIgnoreCase</c> on Windows get
    /// consistent results. Does NOT resolve <c>..</c> here (the safe-path
    /// resolver is the only sanctioned place to call <c>Path.GetFullPath</c>
    /// — H-2). Whitespace-only or null inputs throw <see cref="ArgumentException"/>.
    /// </summary>
    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Root path must be non-empty.", nameof(raw));

        var trimmed = raw.Trim();
        // Trim a single trailing separator so 'C:\openclawnet\' and 'C:\openclawnet'
        // hash to the same thing in our containment checks.
        return System.IO.Path.TrimEndingDirectorySeparator(trimmed);
    }

    // ====================================================================
    // W-2 — per-scope subfolder helpers (Drummond P1 #6)
    // ====================================================================

    // Mirrors SafePathResolver's H-5 allowlist. Held here too so name
    // validation can run against an in-process string without paying the
    // cost of a full ResolveSafePath envelope (we already know the parent).
    private static readonly Regex SafeSegmentRegex = new(
        @"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly System.Collections.Generic.HashSet<string> ReservedNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

    /// <summary>
    /// Returns (and creates if missing) the per-agent scope root:
    /// <c>{ResolvedRoot}/agents/{validatedName}/</c>. The directory is
    /// created with a restrictive DACL on Windows (current user
    /// FullControl, inheritance disabled). On non-Windows the dir is
    /// created without ACL hardening (a log INFO records the skip).
    /// </summary>
    /// <param name="agentName">
    /// The agent name to scope by. Validated with the H-5 allowlist (same
    /// regex used by <see cref="SafePathResolver"/>); reserved names and
    /// invalid characters throw <see cref="UnsafePathException"/> with the
    /// matching <see cref="UnsafePathReason"/>.
    /// </param>
    public static string ResolveAgentRoot(string agentName, ILogger? logger = null)
    {
        ValidateName(agentName, nameof(agentName));
        var (rootPath, _) = ResolveRoot();
        var agentRoot = System.IO.Path.Combine(rootPath, "agents", agentName);
        EnsureDirectoryWithRestrictiveAcl(agentRoot, logger);
        return agentRoot;
    }

    /// <summary>
    /// Returns (and creates if missing) the shared models cache root:
    /// <c>{ResolvedRoot}/models/</c>. W-3 prep — Ollama / HF caches will
    /// land here. ACL hardening per <see cref="ResolveAgentRoot"/>.
    /// </summary>
    public static string ResolveModelsRoot(ILogger? logger = null)
    {
        var (rootPath, _) = ResolveRoot();
        var modelsRoot = System.IO.Path.Combine(rootPath, "models");
        EnsureDirectoryWithRestrictiveAcl(modelsRoot, logger);
        return modelsRoot;
    }

    // ====================================================================
    // K-1b — Skills 3-layer storage roots (system / installed / agents)
    // ====================================================================

    /// <summary>
    /// K-1b — Returns (and creates if missing) the system-skills layer root:
    /// <c>{ResolvedRoot}/skills/system/</c>. SystemSkillsSeeder copies
    /// the bundled <c>memory</c> + <c>doc-processor</c> SKILL.md folders
    /// here at boot. ACL hardening per <see cref="ResolveAgentRoot"/>.
    /// </summary>
    public static string ResolveSkillsSystemRoot(ILogger? logger = null)
    {
        var (rootPath, _) = ResolveRoot();
        var path = System.IO.Path.Combine(rootPath, "skills", "system");
        EnsureDirectoryWithRestrictiveAcl(path, logger);
        return path;
    }

    /// <summary>
    /// K-1b — Returns (and creates if missing) the installed-skills layer root:
    /// <c>{ResolvedRoot}/skills/installed/</c>. Operator-imported skills
    /// (awesome-copilot, manual authoring, etc.) land here.
    /// </summary>
    public static string ResolveSkillsInstalledRoot(ILogger? logger = null)
    {
        var (rootPath, _) = ResolveRoot();
        var path = System.IO.Path.Combine(rootPath, "skills", "installed");
        EnsureDirectoryWithRestrictiveAcl(path, logger);
        return path;
    }

    /// <summary>
    /// K-1b — Returns (and creates if missing) the per-agent skills overlay root:
    /// <c>{ResolvedRoot}/skills/agents/{validatedAgentName}/</c>. The
    /// <paramref name="agentName"/> is validated through the same H-5
    /// allowlist as <see cref="ResolveAgentRoot"/>. Throws
    /// <see cref="UnsafePathException"/> on invalid names.
    /// </summary>
    public static string ResolveSkillsAgentRoot(string agentName, ILogger? logger = null)
    {
        ValidateName(agentName, nameof(agentName));
        var (rootPath, _) = ResolveRoot();
        var path = System.IO.Path.Combine(rootPath, "skills", "agents", agentName);
        EnsureDirectoryWithRestrictiveAcl(path, logger);
        return path;
    }

    // ====================================================================
    // W-3 — model file name allowlist (Drummond AC3)
    // ====================================================================

    /// <summary>
    /// Strict model-file-name allowlist:
    /// <c>^[a-z0-9][a-z0-9._-]{0,127}\.(gguf|safetensors|onnx|bin)$</c>.
    /// Stricter than H-5 — extension MUST be one of the four whitelisted
    /// model formats and the stem may be up to 128 chars (vs H-5's 64) so
    /// real-world model identifiers like
    /// <c>llama-3.1-70b-instruct-q4_k_m.gguf</c> are admissible.
    /// Case-insensitive; the resolved on-disk name is preserved verbatim.
    /// </summary>
    private static readonly Regex SafeModelFileNameRegex = new(
        @"^[a-z0-9][a-z0-9._-]{0,127}\.(gguf|safetensors|onnx|bin)$",
        RegexOptions.Compiled
        | RegexOptions.CultureInvariant
        | RegexOptions.IgnoreCase);

    /// <summary>
    /// W-3 (Drummond AC3) — Resolves the absolute on-disk path for a model
    /// file under <see cref="ResolveModelsRoot"/>, enforcing the strict
    /// model-file-name allowlist. Throws <see cref="UnsafePathException"/>
    /// with <see cref="UnsafePathReason.InvalidModelName"/> if the file
    /// name fails the allowlist (charset, leading/trailing punctuation,
    /// reserved-name, or extension not in
    /// <c>gguf|safetensors|onnx|bin</c>). Containment is enforced
    /// post-normalize (defense in depth: the regex already rejects every
    /// segment-bearing input, but we still re-verify).
    /// </summary>
    /// <param name="fileName">
    /// Bare file name (no directory components). Must match the strict
    /// model allowlist regex.
    /// </param>
    /// <returns>The absolute file path under the models root.</returns>
    public static string ResolveSafeModelPath(string fileName, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new UnsafePathException(
                "Model file name must be non-empty.",
                UnsafePathReason.InvalidModelName,
                scopeRoot: null,
                requestedPath: fileName);

        // Reject any path-like input — this method takes a bare file name.
        // (The allowlist regex would catch these anyway, but a precise
        // up-front error helps audit triage.)
        if (fileName.IndexOfAny(new[] { '/', '\\' }) >= 0
            || fileName.Contains("..", StringComparison.Ordinal))
        {
            throw new UnsafePathException(
                $"Model file name '{fileName}' contains path separators or traversal.",
                UnsafePathReason.InvalidModelName,
                scopeRoot: null,
                requestedPath: fileName);
        }

        if (!SafeModelFileNameRegex.IsMatch(fileName))
        {
            throw new UnsafePathException(
                $"Model file name '{fileName}' violates the model-name policy " +
                "(allowed: lowercase alnum start, [a-z0-9._-]{0,127}, extension in " +
                "{gguf,safetensors,onnx,bin}).",
                UnsafePathReason.InvalidModelName,
                scopeRoot: null,
                requestedPath: fileName);
        }

        // Reserved-name check on the stem (CON.gguf etc. would be allowed
        // by the regex but reserved on Windows).
        var dot = fileName.IndexOf('.');
        var stem = dot < 0 ? fileName : fileName[..dot];
        if (ReservedNames.Contains(stem))
            throw new UnsafePathException(
                $"Model file name '{fileName}' uses a reserved Windows device name.",
                UnsafePathReason.ReservedName,
                scopeRoot: null,
                requestedPath: fileName);

        var modelsRoot = ResolveModelsRoot(logger);
        var combined = System.IO.Path.GetFullPath(System.IO.Path.Combine(modelsRoot, fileName));

        // Defense in depth: re-verify containment. The regex already rules
        // out separators / traversal, so this should never fire — but if it
        // ever does, fail closed with the exact same audit category.
        var normalizedRoot = System.IO.Path.TrimEndingDirectorySeparator(modelsRoot);
        var sep = System.IO.Path.DirectorySeparatorChar;
        var pathComparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!combined.StartsWith(normalizedRoot + sep, pathComparison))
        {
            throw new UnsafePathException(
                $"Resolved model path escaped the models root.",
                UnsafePathReason.OutsideScope,
                scopeRoot: normalizedRoot,
                requestedPath: fileName);
        }

        return combined;
    }

    /// <summary>
    /// Returns (and creates if missing) an arbitrary user-named scope:
    /// <c>{ResolvedRoot}/{validatedName}/</c>. W-4 prep — examples include
    /// <c>mysamplefiles</c> or <c>workspace</c>. The folder name is
    /// validated against the H-5 allowlist. ACL hardening per
    /// <see cref="ResolveAgentRoot"/>.
    /// </summary>
    public static string ResolveUserRoot(string folderName, ILogger? logger = null)
    {
        ValidateName(folderName, nameof(folderName));
        var (rootPath, _) = ResolveRoot();
        var userRoot = System.IO.Path.Combine(rootPath, folderName);
        EnsureDirectoryWithRestrictiveAcl(userRoot, logger);
        return userRoot;
    }

    // ====================================================================
    // W-4 — user-folder name allowlist (Drummond W-4 AC1)
    // ====================================================================

    /// <summary>
    /// Strict user-folder name allowlist (W-4 Drummond AC1):
    /// <c>^[a-z0-9][a-z0-9._-]{0,63}$</c>. Lowercase-first, max 64 chars,
    /// NO extension. Stricter than H-5 (which admits uppercase) because
    /// user-folder names are reachable from web-user input via the
    /// gateway endpoints — keeping them lowercase eliminates an entire
    /// case-collision attack class on case-insensitive filesystems.
    /// </summary>
    private static readonly Regex SafeUserFolderRegex = new(
        @"^[a-z0-9][a-z0-9._-]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// W-4 (Drummond W-4 AC1) — Resolves the absolute on-disk path for a
    /// user folder under <see cref="ResolveRoot"/>:
    /// <c>{ResolvedRoot}/{validatedFolderName}/</c>. Enforces the strict
    /// user-folder allowlist
    /// (<c>^[a-z0-9][a-z0-9._-]{0,63}$</c>) and routes the post-validation
    /// containment check through <see cref="ISafePathResolver.ResolveSafePath"/>
    /// — which means the H-3 reparse-point sweep runs on every call,
    /// closing the residual gap recorded in Drummond's W-3 deviation #2
    /// for the user-folder surface (W-4 AC4 binding criterion).
    /// </summary>
    /// <param name="folderName">
    /// Bare folder name (no directory components, no extension). Must
    /// match the strict user-folder allowlist regex.
    /// </param>
    /// <param name="logger">Optional logger for ACL hardening.</param>
    /// <param name="resolver">
    /// Optional <see cref="ISafePathResolver"/> for the containment +
    /// reparse-point check. Defaults to a fresh <see cref="SafePathResolver"/>
    /// so static callers (boot path, AppHost) work without DI; tests and
    /// gateway endpoints SHOULD pass the DI-resolved instance so a custom
    /// resolver / spy can be injected.
    /// </param>
    /// <returns>The absolute folder path under the storage root.</returns>
    public static string ResolveSafeUserFolderPath(
        string folderName,
        ILogger? logger = null,
        ISafePathResolver? resolver = null)
    {
        // Empty / whitespace.
        if (string.IsNullOrWhiteSpace(folderName))
            throw new UnsafePathException(
                "User-folder name must be non-empty.",
                UnsafePathReason.InvalidUserFolderName,
                scopeRoot: null,
                requestedPath: folderName);

        // Reject any path-like input — this method takes a bare folder
        // name. The allowlist regex would catch these anyway; a precise
        // up-front error helps audit triage.
        if (folderName.IndexOfAny(new[] { '/', '\\' }) >= 0
            || folderName.Contains("..", StringComparison.Ordinal))
        {
            throw new UnsafePathException(
                $"User-folder name '{folderName}' contains path separators or traversal.",
                UnsafePathReason.InvalidUserFolderName,
                scopeRoot: null,
                requestedPath: folderName);
        }

        // Strict user-folder allowlist — lowercase-first, [a-z0-9._-], max 64.
        if (!SafeUserFolderRegex.IsMatch(folderName))
        {
            throw new UnsafePathException(
                $"User-folder name '{folderName}' violates the user-folder name policy " +
                "(allowed: lowercase alnum start, [a-z0-9._-]{0,63}, no extension, max 64 chars).",
                UnsafePathReason.InvalidUserFolderName,
                scopeRoot: null,
                requestedPath: folderName);
        }

        // Reserved-name check — applies to the whole name (no extension).
        if (ReservedNames.Contains(folderName))
            throw new UnsafePathException(
                $"User-folder name '{folderName}' uses a reserved Windows device name.",
                UnsafePathReason.ReservedName,
                scopeRoot: null,
                requestedPath: folderName);

        var (rootPath, _) = ResolveRoot();

        // Drummond W-4 AC4 binding: route containment through ISafePathResolver
        // so EnsureNoReparsePointEscape walks the resolved path. The H-5 cap
        // (64) matches our cap, so the seam's segment validation is a strict
        // no-op on top of our already-validated lowercase-only input.
        resolver ??= new SafePathResolver();
        var resolved = resolver.ResolveSafePath(rootPath, folderName);

        EnsureDirectoryWithRestrictiveAcl(resolved, logger);
        return resolved;
    }

    /// <summary>
    /// H-5 name validation for the per-scope helpers above. Throws
    /// <see cref="UnsafePathException"/> with a precise
    /// <see cref="UnsafePathReason"/> so audit emission can categorize the
    /// rejection without parsing exception messages.
    /// </summary>
    private static void ValidateName(string? name, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new UnsafePathException(
                $"{parameterName} must be non-empty.",
                UnsafePathReason.EmptyOrWhitespace,
                scopeRoot: null,
                requestedPath: name);

        // No path separators or traversal — these helpers take a SEGMENT,
        // not a path fragment. Anything with a separator is inherently
        // invalid here.
        if (name.IndexOfAny(new[] { '/', '\\' }) >= 0
            || name.Contains("..", StringComparison.Ordinal))
        {
            throw new UnsafePathException(
                $"{parameterName} '{name}' contains path separators or traversal.",
                UnsafePathReason.InvalidName,
                scopeRoot: null,
                requestedPath: name);
        }

        // Leading/trailing dot or space — Windows trims these silently.
        if (name[0] == '.' || name[0] == ' ' || name[^1] == '.' || name[^1] == ' ')
            throw new UnsafePathException(
                $"{parameterName} '{name}' violates the safe-name policy.",
                UnsafePathReason.InvalidName,
                scopeRoot: null,
                requestedPath: name);

        if (!SafeSegmentRegex.IsMatch(name))
            throw new UnsafePathException(
                $"{parameterName} '{name}' violates the safe-name policy.",
                UnsafePathReason.InvalidName,
                scopeRoot: null,
                requestedPath: name);

        // Reserved-name check applies to the stem (before the first dot).
        var dot = name.IndexOf('.');
        var stem = dot < 0 ? name : name[..dot];
        if (ReservedNames.Contains(stem))
            throw new UnsafePathException(
                $"{parameterName} '{name}' uses a reserved Windows device name.",
                UnsafePathReason.ReservedName,
                scopeRoot: null,
                requestedPath: name);
    }

    /// <summary>
    /// Creates (if missing) the directory at <paramref name="path"/> and,
    /// on Windows, applies a restrictive DACL: current user gets
    /// FullControl, inherited ACEs are removed. On non-Windows the dir is
    /// created without ACL changes (FAT/NTFS are the only platforms where
    /// the locked W-2 model has a meaningful enforcement story today).
    /// </summary>
    private static void EnsureDirectoryWithRestrictiveAcl(string path, ILogger? logger)
    {
        Directory.CreateDirectory(path);

        if (!OperatingSystem.IsWindows())
        {
            (logger ?? NullLogger.Instance).LogInformation(
                "Skipping ACL hardening on non-Windows for '{Path}'.", path);
            return;
        }

        try
        {
            ApplyWindowsRestrictiveDacl(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                   or InvalidOperationException
                                   or PlatformNotSupportedException
                                   or IdentityNotMappedException)
        {
            // ACL hardening is best-effort during the boot path. The W-2
            // ACL verifier seam (H-7) will catch the misconfiguration on
            // its own pass. Don't crash startup over an ACL-only failure.
            (logger ?? NullLogger.Instance).LogWarning(ex,
                "Failed to apply restrictive ACL on '{Path}'. " +
                "Continuing — the H-7 ACL verifier will report this on its next probe.",
                path);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindowsRestrictiveDacl(string path)
    {
        var dirInfo = new DirectoryInfo(path);
        var security = new DirectorySecurity();

        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current Windows identity has no SID.");

        // Disable inheritance, do NOT preserve inherited rules — we want a
        // clean slate so only the owner ACE we add below applies.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        security.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        dirInfo.SetAccessControl(security);
    }
}

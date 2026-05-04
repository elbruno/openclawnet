using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace OpenClawNet.Storage;

/// <summary>
/// Filesystem layout for local OpenClawNet artifacts: model caches, generated
/// binaries (PNGs, WAVs, etc.), and any other tool output that must persist
/// across runs but is not appropriate for the SQLite store.
/// </summary>
/// <remarks>
/// Bound from the <c>Storage</c> configuration section. Tools should depend on
/// <see cref="StorageOptions"/> rather than hard-coding paths so the user can
/// relocate everything (including multi-GB ONNX model caches) by changing one
/// setting.
/// </remarks>
public sealed class StorageOptions
{
    private ILogger<StorageOptions>? _logger;

    public const string SectionName = "Storage";

    /// <summary>
    /// Root directory for all local OpenClawNet storage. Defaults to
    /// <c>C:\openclawnet</c> on Windows and <c>~/openclawnet</c> elsewhere
    /// (W-1 Q3 — legacy <c>/storage</c> suffix removed). Override via
    /// the <c>OPENCLAWNET_STORAGE_ROOT</c> environment variable or the
    /// <c>Storage:RootPath</c> configuration key.
    /// </summary>
    public string RootPath { get; set; } = DefaultRootPath();

    /// <summary>
    /// Subfolder name (under <see cref="RootPath"/>) for binary artifacts
    /// produced by tools (images, audio, videos, etc.). Default: <c>binary</c>.
    /// </summary>
    public string BinaryFolderName { get; set; } = "binary";

    /// <summary>
    /// Subfolder name (under <see cref="RootPath"/>) for downloaded model
    /// caches (Stable Diffusion ONNX, Whisper, embeddings, etc.). Default: <c>models</c>.
    /// </summary>
    public string ModelsFolderName { get; set; } = "models";

    /// <summary>
    /// Subfolder name (under <see cref="RootPath"/>) for agent output files,
    /// notes, and state. Default: <c>agents</c>.
    /// </summary>
    public string AgentsFolderName { get; set; } = "agents";

    /// <summary>
    /// Subfolder name (under <see cref="RootPath"/>) for user-imported skills.
    /// Default: <c>skills</c>.
    /// </summary>
    public string SkillsFolderName { get; set; } = "skills";

    /// <summary>
    /// W-3 (Drummond AC2) — total-quota ceiling under <see cref="ModelsPath"/>.
    /// Default: 50 GB. Configured via <c>Storage:ModelMaxTotalBytes</c>.
    /// </summary>
    public long ModelMaxTotalBytes { get; set; } = ModelStorageQuota.DefaultMaxTotalBytes;

    /// <summary>
    /// W-3 (Drummond AC2) — per-file ceiling under <see cref="ModelsPath"/>.
    /// Default: 20 GB. Configured via <c>Storage:ModelMaxPerFileBytes</c>.
    /// </summary>
    public long ModelMaxPerFileBytes { get; set; } = ModelStorageQuota.DefaultMaxPerFileBytes;

    /// <summary>
    /// W-4 (Drummond W-4 AC2) — per-folder ceiling under each user folder.
    /// Default: 5 GB. Configured via <c>Storage:UserMaxPerFolderBytes</c>.
    /// </summary>
    public long UserMaxPerFolderBytes { get; set; } = UserFolderQuota.DefaultMaxPerFolderBytes;

    /// <summary>
    /// W-4 (Drummond W-4 AC2) — total ceiling across all user folders
    /// under <see cref="RootPath"/> (excluding scope subfolders agents/,
    /// models/, skills/, binary/, dataprotection-keys/, audit/).
    /// Default: 25 GB. Configured via <c>Storage:UserMaxTotalBytes</c>.
    /// </summary>
    public long UserMaxTotalBytes { get; set; } = UserFolderQuota.DefaultMaxTotalBytes;

    /// <summary>Absolute path for binary artifact outputs.</summary>
    public string BinaryArtifactsPath => Path.Combine(RootPath, BinaryFolderName);

    /// <summary>Absolute path for downloaded model caches.</summary>
    public string ModelsPath => Path.Combine(RootPath, ModelsFolderName);

    /// <summary>Absolute path for agent outputs: {RootPath}/agents</summary>
    public string AgentsPath => Path.Combine(RootPath, AgentsFolderName);

    /// <summary>Absolute path for user-imported skills: {RootPath}/skills</summary>
    public string SkillsPath => Path.Combine(RootPath, SkillsFolderName);

    /// <summary>
    /// Returns (and creates if missing) a per-tool subfolder under the binary
    /// artifacts path. Example: <c>BinaryFolderForTool("text-to-image")</c> →
    /// <c>{RootPath}/binary/text-to-image/</c>.
    /// </summary>
    public string BinaryFolderForTool(string toolName)
    {
        var folder = Path.Combine(BinaryArtifactsPath, toolName);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// Returns (and creates if missing) a per-agent subfolder: {RootPath}/agents/{agentName}/.
    /// Attempts primary path first, falls back to LocalApplicationData if primary fails.
    /// Sanitizes agent name to prevent path traversal attacks.
    /// </summary>
    /// <param name="agentName">The name of the agent (will be sanitized)</param>
    /// <returns>The full path to the agent's folder</returns>
    /// <exception cref="ArgumentException">If the agent name is invalid after sanitization</exception>
    public string AgentFolderForName(string agentName)
    {
        // Sanitize agent name to prevent path traversal attacks
        var sanitizedName = SanitizeAgentName(agentName);

        var primaryPath = Path.Combine(AgentsPath, sanitizedName);
        try
        {
            Directory.CreateDirectory(primaryPath);
            _logger?.LogDebug("Created agent folder at primary path: {PrimaryPath}", primaryPath);
            return primaryPath;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogWarning(ex, "Cannot create agents directory at {PrimaryPath}; falling back to LocalApplicationData", primaryPath);

            var fallbackPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenClawNet", "agents", sanitizedName);
            Directory.CreateDirectory(fallbackPath);
            _logger?.LogInformation("Using fallback agent folder at: {FallbackPath}", fallbackPath);
            return fallbackPath;
        }
    }

    /// <summary>
    /// Sanitizes an agent name to prevent path traversal attacks.
    /// Removes/replaces: .., /, \, and null characters.
    /// </summary>
    /// <param name="agentName">The original agent name</param>
    /// <returns>The sanitized agent name</returns>
    /// <exception cref="ArgumentException">If the name is empty or becomes invalid after sanitization</exception>
    private static string SanitizeAgentName(string agentName)
    {
        if (string.IsNullOrWhiteSpace(agentName))
            throw new ArgumentException("Agent name cannot be null or whitespace", nameof(agentName));

        // Remove any path traversal characters: .. / \ and null
        var sanitized = agentName
            .Replace("..", "_")
            .Replace("/", "_")
            .Replace("\\", "_")
            .Replace("\0", "_");

        // Reject if becomes empty or only dots/underscores after sanitization
        if (string.IsNullOrWhiteSpace(sanitized) || sanitized.All(c => c == '_' || c == '.'))
            throw new ArgumentException($"Invalid agent name (becomes empty after sanitization): {agentName}", nameof(agentName));

        return sanitized;
    }

    /// <summary>
    /// Internal method to set the logger instance. Called during DI configuration.
    /// </summary>
    internal void SetLogger(ILogger<StorageOptions> logger)
    {
        _logger = logger;
    }

    /// <summary>Ensures <see cref="RootPath"/>, binary, models, agents, and skills directories exist.</summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(BinaryArtifactsPath);
        Directory.CreateDirectory(ModelsPath);
        Directory.CreateDirectory(AgentsPath);
        Directory.CreateDirectory(SkillsPath);
    }

    // W-1 (Q3): default root no longer carries the legacy '/storage' suffix.
    // Single source of truth lives in OpenClawNetPaths.DefaultRoot.
    private static string DefaultRootPath() => OpenClawNetPaths.DefaultRoot;
}

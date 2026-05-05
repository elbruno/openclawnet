using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using OpenClawNet.Gateway.Configuration;
using OpenClawNet.Storage.Services;

namespace OpenClawNet.Gateway.Services;

/// <summary>
/// Centralized provider for agent output storage directories.
/// Resolution order: OPENCLAW_STORAGE_DIR env var → appsettings StorageDir → platform default.
/// </summary>
public sealed class StorageDirectoryProvider : IStorageDirectoryProvider
{
    private readonly OpenClawNetOptions _options;
    private readonly ILogger<StorageDirectoryProvider> _logger;
    private readonly string _basePath;

    public StorageDirectoryProvider(
        IOptions<OpenClawNetOptions> options,
        ILogger<StorageDirectoryProvider> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Validate configuration
        _options.Validate();

        // Resolve base path: env var → config → platform default
        var envOverride = Environment.GetEnvironmentVariable("OPENCLAW_STORAGE_DIR");
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            _basePath = envOverride;
            _logger.LogInformation("Using storage directory from OPENCLAW_STORAGE_DIR: {Path}", _basePath);
        }
        else if (!string.IsNullOrWhiteSpace(_options.StorageDir))
        {
            _basePath = _options.StorageDir;
            _logger.LogInformation("Using storage directory from configuration: {Path}", _basePath);
        }
        else
        {
            _basePath = GetDefaultStorageDir();
            _logger.LogInformation("Using platform default storage directory: {Path}", _basePath);
        }
    }

    /// <inheritdoc />
    public string GetStorageDirectory(string agentName)
    {
        if (string.IsNullOrWhiteSpace(agentName))
            throw new ArgumentNullException(nameof(agentName), "Agent name cannot be null or empty.");

        var fullPath = Path.Combine(_basePath, agentName);

        try
        {
            Directory.CreateDirectory(fullPath);
            _logger.LogDebug("Ensured storage directory exists: {Path}", fullPath);
            return fullPath;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex,
                "Permission denied creating storage directory {Path}. Falling back to temp directory.",
                fullPath);
            
            var tempFallback = Path.Combine(Path.GetTempPath(), "OpenClawNet", agentName);
            Directory.CreateDirectory(tempFallback);
            return tempFallback;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error creating storage directory {Path}. Falling back to temp directory.",
                fullPath);
            
            var tempFallback = Path.Combine(Path.GetTempPath(), "OpenClawNet", agentName);
            Directory.CreateDirectory(tempFallback);
            return tempFallback;
        }
    }

    /// <summary>
    /// Returns platform-specific default storage directory.
    /// </summary>
    private static string GetDefaultStorageDir()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "OpenClawNet");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Prefer user home directory over system-wide /var for non-root users
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return !string.IsNullOrEmpty(homeDir)
                ? Path.Combine(homeDir, ".openclawnet")
                : "/var/openclawnet";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                "Library",
                "OpenClawNet");
        }
        
        throw new PlatformNotSupportedException(
            $"Platform {RuntimeInformation.OSDescription} is not supported for storage directory defaults.");
    }
}

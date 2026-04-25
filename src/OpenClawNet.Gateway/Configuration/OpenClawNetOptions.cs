using System.ComponentModel.DataAnnotations;

namespace OpenClawNet.Gateway.Configuration;

/// <summary>
/// Configuration options for OpenClawNet core settings.
/// Binds from appsettings.json "OpenClawNet" section.
/// </summary>
public sealed class OpenClawNetOptions
{
    /// <summary>
    /// Base directory for agent output storage.
    /// If null, StorageDirectoryProvider uses platform-specific defaults.
    /// Can be overridden via OPENCLAW_STORAGE_DIR environment variable.
    /// </summary>
    public string? StorageDir { get; set; }

    /// <summary>
    /// Configuration for automatic storage cleanup.
    /// </summary>
    public StorageRetentionOptions StorageRetention { get; set; } = new();

    /// <summary>
    /// Validates the configuration at startup.
    /// Throws if StorageDir contains invalid path characters.
    /// </summary>
    public void Validate()
    {
        if (StorageDir != null)
        {
            try
            {
                // Validate path characters but don't require it to exist
                _ = Path.GetFullPath(StorageDir);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                throw new ArgumentException(
                    $"StorageDir '{StorageDir}' contains invalid path characters.", ex);
            }
        }
    }
}

/// <summary>
/// Configuration for automatic storage retention and cleanup.
/// </summary>
public sealed class StorageRetentionOptions
{
    /// <summary>
    /// Whether automatic cleanup of old storage is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Maximum age in days for storage artifacts before cleanup.
    /// </summary>
    [Range(1, 365)]
    public int MaxAgeInDays { get; set; } = 30;
}

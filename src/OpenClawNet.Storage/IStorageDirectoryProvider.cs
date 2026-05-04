namespace OpenClawNet.Storage.Services;

/// <summary>
/// Provides centralized agent output directory management with cross-platform defaults.
/// </summary>
public interface IStorageDirectoryProvider
{
    /// <summary>
    /// Gets the full storage directory path for a specific agent.
    /// Creates the directory if it doesn't exist.
    /// </summary>
    /// <param name="agentName">Name of the agent (used as subdirectory name).</param>
    /// <returns>Full path like C:\Users\bruno\OpenClawNet\orchestrator</returns>
    /// <exception cref="ArgumentNullException">If agentName is null or empty.</exception>
    string GetStorageDirectory(string agentName);
}

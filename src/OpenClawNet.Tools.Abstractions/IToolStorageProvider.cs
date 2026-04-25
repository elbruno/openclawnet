namespace OpenClawNet.Tools.Abstractions;

/// <summary>
/// Centralized location for tools to persist artifacts they produce
/// (transcripts, downloaded markdown, generated images, etc.).
///
/// The host (Gateway) provides the implementation, typically wrapping
/// <c>IStorageDirectoryProvider</c> so artifacts land under the configured
/// <c>OPENCLAW_STORAGE_DIR</c> / <c>StorageDir</c> root.
/// </summary>
public interface IToolStorageProvider
{
    /// <summary>
    /// Returns the directory a tool should write to, creating it if it
    /// does not already exist. Implementations sanitize <paramref name="toolName"/>
    /// so the segment is filesystem-safe.
    /// </summary>
    string GetToolStorageDirectory(string toolName);
}

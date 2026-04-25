using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Gateway.Services;

/// <summary>
/// Adapter that satisfies <see cref="IToolStorageProvider"/> (consumed by tools
/// in the OpenClawNet.Tools.* projects, which can't reference Gateway types) by
/// delegating to the existing <see cref="IStorageDirectoryProvider"/>.
/// </summary>
public sealed class ToolStorageProviderAdapter : IToolStorageProvider
{
    private readonly IStorageDirectoryProvider _inner;

    public ToolStorageProviderAdapter(IStorageDirectoryProvider inner)
    {
        _inner = inner;
    }

    public string GetToolStorageDirectory(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException("Tool name is required.", nameof(toolName));
        }

        // Reuse the agent-name slot for tool names. The provider already
        // creates the directory and applies cross-platform defaults.
        return _inner.GetStorageDirectory(toolName);
    }
}

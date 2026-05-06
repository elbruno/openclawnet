using Octokit;

namespace OpenClawNet.Tools.GitHub;

/// <summary>
/// Factory for creating IGitHubClient instances with optional custom base URI.
/// Enables hermetic testing with WireMock by allowing dependency injection of client configuration.
/// </summary>
public interface IGitHubClientFactory
{
    /// <summary>
    /// Creates a new IGitHubClient instance.
    /// </summary>
    IGitHubClient CreateClient();
}

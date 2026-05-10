using Microsoft.Extensions.Configuration;
using Octokit;

namespace OpenClawNet.Tools.GitHub;

public sealed class GitHubClientFactory : IGitHubClientFactory
{
    private const string ProductName = "OpenClawNet";
    private readonly Uri? _baseAddress;

    public GitHubClientFactory(IConfiguration configuration)
    {
        // Allow tests to override GitHub API base URL via configuration
        var baseUrl = configuration["GitHub:ApiBaseUrl"] ?? Environment.GetEnvironmentVariable("GITHUB_API_BASE_URL");
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            _baseAddress = new Uri(baseUrl);
        }
    }

    public IGitHubClient CreateClient()
    {
        var productHeader = new ProductHeaderValue(ProductName);
        
        if (_baseAddress is not null)
        {
            // Treat configured URLs as exact API endpoints for WireMock/GitHub Enterprise tests.
            return new GitHubClient(new Connection(productHeader, _baseAddress));
        }
        
        // Default GitHub API endpoint
        return new GitHubClient(productHeader);
    }
}

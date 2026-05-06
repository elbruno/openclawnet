using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Tools.GitHub;

public static class GitHubToolServiceCollectionExtensions
{
    public static IServiceCollection AddGitHubTool(this IServiceCollection services)
    {
        services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
        services.AddSingleton<ITool, GitHubTool>();
        return services;
    }
}

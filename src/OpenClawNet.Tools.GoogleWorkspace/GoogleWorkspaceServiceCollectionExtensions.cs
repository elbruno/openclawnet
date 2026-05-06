using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Tools.GoogleWorkspace;

/// <summary>
/// Dependency injection extensions for Google Workspace tools.
/// </summary>
public static class GoogleWorkspaceServiceCollectionExtensions
{
    /// <summary>
    /// Registers Google Workspace tool infrastructure (factory, options, token store) and concrete tools.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration root containing GoogleWorkspace section.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGoogleWorkspaceTools(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind configuration options
        services.Configure<GoogleWorkspaceOptions>(
            configuration.GetSection(GoogleWorkspaceOptions.SectionName));

        // S5-5: OAuth token store is now registered in OpenClawNet.Storage as EncryptedSqliteOAuthTokenStore
        // InMemoryGoogleOAuthTokenStore is kept for test fixtures but NOT registered here

        // Register OAuth flow state store (10-minute TTL, in-memory)
        services.AddSingleton<IOAuthFlowStateStore, InMemoryOAuthFlowStateStore>();

        // Register HttpClient for Google token endpoint calls (uses Aspire resilience)
        services.AddHttpClient("GoogleOAuth");

        // Register factory for creating authenticated Google service instances.
        // Production passes no test transport, preserving the Google SDK default HTTP path.
        services.AddSingleton<IGoogleClientFactory>(sp => new GoogleClientFactory(
            sp.GetRequiredService<IGoogleOAuthTokenStore>(),
            sp.GetRequiredService<IOptions<GoogleWorkspaceOptions>>(),
            sp.GetRequiredService<ILogger<GoogleClientFactory>>(),
            sp.GetRequiredService<System.Net.Http.IHttpClientFactory>(),
            messageHandler: null));

        // Register tools
        services.AddSingleton<ITool, GmailSummarizeTool>();
        services.AddSingleton<ITool, CalendarCreateEventTool>();

        return services;
    }
}

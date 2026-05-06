using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Tools.Dashboard;

/// <summary>
/// Dependency injection extensions for the Dashboard publisher tool.
/// </summary>
public static class DashboardServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Dashboard publisher tool and its dependencies.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration root containing Dashboard section.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDashboardTool(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind configuration options
        services.Configure<DashboardOptions>(
            configuration.GetSection(DashboardOptions.SectionName));

        // Register named HttpClient for dashboard API calls
        // Aspire ServiceDefaults provides standard resilience handler globally
        services.AddHttpClient("dashboard", (sp, client) =>
        {
            var options = configuration
                .GetSection(DashboardOptions.SectionName)
                .Get<DashboardOptions>();
            
            if (options?.TimeoutSeconds > 0)
            {
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            }
        });

        // Register publisher and tool
        services.AddSingleton<IDashboardPublisher, DashboardPublisher>();
        services.AddSingleton<ITool, DashboardPublisherTool>();

        return services;
    }
}

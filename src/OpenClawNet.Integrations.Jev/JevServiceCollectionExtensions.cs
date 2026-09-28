using ElBruno.AI.Jev;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenClawNet.Integrations.Jev;

public static class JevServiceCollectionExtensions
{
    public static IServiceCollection AddOptionalJevAgentProfileDecisions(
        this IServiceCollection services,
        JevRoutingOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton(Options.Create(options));

        if (!options.Enabled)
        {
            services.AddSingleton<IAgentProfileDecisionService>(
                new FallbackAgentProfileDecisionService(AgentProfileDecisionStatus.Disabled, "disabled"));
            return services;
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey) || options.ApiKey.Any(char.IsWhiteSpace))
        {
            services.AddSingleton<IAgentProfileDecisionService, MisconfiguredAgentProfileDecisionService>();
            return services;
        }

        if (options.Timeout <= TimeSpan.Zero ||
            options.Timeout > TimeSpan.FromDays(1) ||
            options.Endpoint is null ||
            !options.Endpoint.IsAbsoluteUri ||
            options.Endpoint.Scheme != Uri.UriSchemeHttps ||
            options.Endpoint.AbsolutePath != "/" ||
            options.Endpoint.Query.Length != 0 ||
            options.Endpoint.Fragment.Length != 0 ||
            options.Endpoint.UserInfo.Length != 0 ||
            options.DefaultModel is not null && string.IsNullOrWhiteSpace(options.DefaultModel))
        {
            services.AddSingleton<IAgentProfileDecisionService>(
                new FallbackAgentProfileDecisionService(
                    AgentProfileDecisionStatus.Misconfigured,
                    "invalid_configuration"));
            return services;
        }

        services.AddJev(jev =>
        {
            jev.ApiKey = options.ApiKey;
            jev.Endpoint = options.Endpoint;
            jev.Timeout = options.Timeout;
            jev.MaxRetries = 0;
            if (!string.IsNullOrWhiteSpace(options.DefaultModel))
            {
                jev.DefaultModel = options.DefaultModel;
            }
        });
        services.AddSingleton<IAgentProfileDecisionService, JevAgentProfileDecisionService>();
        return services;
    }

    private sealed class MisconfiguredAgentProfileDecisionService(
        ILogger<MisconfiguredAgentProfileDecisionService> logger)
        : IAgentProfileDecisionService
    {
        public Task<AgentProfileDecisionResult> DecideAsync(
            AgentProfileDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            logger.LogWarning(
                "JEV routing is enabled but Jev:ApiKey is missing; deterministic routing will be used");
            return Task.FromResult(new AgentProfileDecisionResult(
                AgentProfileDecisionStatus.Misconfigured,
                null,
                null,
                TimeSpan.Zero,
                "missing_api_key"));
        }
    }

    private sealed class FallbackAgentProfileDecisionService(
        AgentProfileDecisionStatus status,
        string failureCode)
        : IAgentProfileDecisionService
    {
        public Task<AgentProfileDecisionResult> DecideAsync(
            AgentProfileDecisionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentProfileDecisionResult(
                status,
                null,
                null,
                TimeSpan.Zero,
                failureCode));
    }
}

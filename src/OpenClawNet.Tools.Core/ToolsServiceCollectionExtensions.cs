using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Tools.Core;

public static class ToolsServiceCollectionExtensions
{
    public static IServiceCollection AddToolFramework(this IServiceCollection services)
    {
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddScoped<IToolExecutor, ToolExecutor>();
        services.AddSingleton<IToolApprovalPolicy, AlwaysApprovePolicy>();
        services.TryAddSingletonAgentContextAccessor();
        return services;
    }

    private static void TryAddSingletonAgentContextAccessor(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(IAgentContextAccessor)))
        {
            services.AddSingleton<IAgentContextAccessor, AsyncLocalAgentContextAccessor>();
        }
    }
    
    public static IServiceCollection AddTool<TTool>(this IServiceCollection services)
        where TTool : class, ITool
    {
        services.AddSingleton<ITool, TTool>();
        return services;
    }
}

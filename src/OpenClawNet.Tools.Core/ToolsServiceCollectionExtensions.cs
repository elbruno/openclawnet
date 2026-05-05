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
        return services;
    }
    
    public static IServiceCollection AddTool<TTool>(this IServiceCollection services)
        where TTool : class, ITool
    {
        services.AddSingleton<ITool, TTool>();
        return services;
    }
}

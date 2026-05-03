using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Tools.Memory;

/// <summary>
/// DI helper for the memory tools (issues #100, #113). Registers
/// <see cref="RememberTool"/>, <see cref="RecallTool"/>, and <see cref="ForgetTool"/>
/// against <see cref="ITool"/> so they're discoverable by <c>DefaultAgentRuntime</c>.
/// Caller is responsible for registering an
/// <see cref="OpenClawNet.Memory.IAgentMemoryStore"/> implementation (e.g. via
/// <c>AddMemory(...)</c>).
/// </summary>
public static class MemoryToolsServiceCollectionExtensions
{
    public static IServiceCollection AddMemoryTools(this IServiceCollection services)
    {
        services.AddSingleton<RememberTool>();
        services.AddSingleton<ITool>(sp => sp.GetRequiredService<RememberTool>());
        services.AddSingleton<RecallTool>();
        services.AddSingleton<ITool>(sp => sp.GetRequiredService<RecallTool>());
        services.AddSingleton<ForgetTool>();
        services.AddSingleton<ITool>(sp => sp.GetRequiredService<ForgetTool>());
        return services;
    }
}

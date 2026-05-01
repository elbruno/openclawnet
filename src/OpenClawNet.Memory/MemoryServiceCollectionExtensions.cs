using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ElBruno.LocalEmbeddings.Extensions;

namespace OpenClawNet.Memory;

public static class MemoryServiceCollectionExtensions
{
    public static IServiceCollection AddMemory(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddScoped<IMemoryService, DefaultMemoryService>();
        services.AddScoped<IEmbeddingsService, DefaultEmbeddingsService>();

        // Register agent memory store (stub for #99, replaced by MempalaceNet in #98)
#pragma warning disable CS0618 // Type or member is obsolete
        services.AddScoped<IAgentMemoryStore, StubAgentMemoryStore>();
#pragma warning restore CS0618 // Type or member is obsolete

        if (configuration is not null)
        {
            services.AddLocalEmbeddings(configuration.GetSection("LocalEmbeddings"));
        }
        else
        {
            services.AddLocalEmbeddings(_ => { });
        }

        return services;
    }
}


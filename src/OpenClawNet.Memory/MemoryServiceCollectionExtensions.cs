using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ElBruno.LocalEmbeddings.Extensions;
using OpenClawNet.Storage;

namespace OpenClawNet.Memory;

public static class MemoryServiceCollectionExtensions
{
    public static IServiceCollection AddMemory(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddScoped<IMemoryService, DefaultMemoryService>();
        services.AddScoped<IEmbeddingsService, DefaultEmbeddingsService>();

        if (configuration is not null)
        {
            services.AddLocalEmbeddings(configuration.GetSection("LocalEmbeddings"));
        }
        else
        {
            services.AddLocalEmbeddings(_ => { });
        }

        // Register MempalaceNet-backed agent memory store (issue #98 replaces the #99 stub).
        services.AddMempalaceAgentMemoryStore();

        return services;
    }

    /// <summary>
    /// Registers <see cref="MempalaceAgentMemoryStore"/> as the singleton implementation
    /// of <see cref="IAgentMemoryStore"/>. Requires <see cref="StorageOptions"/> and
    /// <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> to be already registered
    /// (the latter is provided by <c>AddLocalEmbeddings</c>).
    /// </summary>
    public static IServiceCollection AddMempalaceAgentMemoryStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Bind StorageOptions if no one else has — needed for AgentFolderForName fallback.
        services.AddOptions<StorageOptions>();

        services.RemoveAll<IAgentMemoryStore>();
        services.AddSingleton<IAgentMemoryStore>(sp =>
        {
            var storageOptions = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
            storageOptions.EnsureDirectories();
            var generator = sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
            var logger = sp.GetService<ILogger<MempalaceAgentMemoryStore>>();
            return new MempalaceAgentMemoryStore(storageOptions, generator, logger);
        });

        return services;
    }
}


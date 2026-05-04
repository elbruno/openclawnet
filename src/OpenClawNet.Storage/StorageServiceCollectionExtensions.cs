using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenClawNet.Storage;

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddOpenClawStorage(this IServiceCollection services, string? connectionString = null)
    {
        services.AddDbContextFactory<OpenClawDbContext>((sp, options) =>
        {
            // IConfiguration is a singleton — safe to resolve from the root provider.
            // Aspire injects ConnectionStrings:openclawnet-db as an env var; this reads it correctly.
            var config = sp.GetService<IConfiguration>();
            var connStr = config?.GetConnectionString("openclawnet-db")
                ?? connectionString
                ?? "Data Source=openclawnet.db";
            options.UseSqlite(connStr);
        });

        // Bind the local-filesystem layout (root dir, binary artifacts, model cache, agents).
        // W-1 (Q5): RootPath is resolved with the locked precedence env > appsettings > default
        // by OpenClawNetPaths.ResolveRoot, which also emits the boot-time INFO log line.
        services.AddOptions<StorageOptions>()
            .Configure<IConfiguration>((opts, cfg) => cfg.GetSection(StorageOptions.SectionName).Bind(opts))
            .PostConfigure<ILoggerFactory>((opts, loggerFactory) =>
            {
                var logger = loggerFactory.CreateLogger(typeof(OpenClawNetPaths).FullName!);
                var (resolvedRoot, _) = OpenClawNetPaths.ResolveRoot(opts.RootPath, logger);
                opts.RootPath = resolvedRoot;

                opts.SetLogger(loggerFactory.CreateLogger<StorageOptions>());
                opts.EnsureDirectories();
            });
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<StorageOptions>>().Value);

        // W-1: ISafePathResolver — the single sanctioned entry point for
        // turning agent/tool-supplied path fragments into safe absolute paths
        // (H-2). Wave 2 wires FileSystemTool callers through this seam.
        services.AddSingleton<ISafePathResolver, SafePathResolver>();

        // W-2: IStorageAclVerifier — H-7 seam. Currently a no-op stub that
        // logs WARN; a real Windows DACL probe will replace it in a future
        // wave. DI registration is real so the boot-time call site in the
        // gateway resolves a live instance through the container.
        services.AddSingleton<IStorageAclVerifier, NoopStorageAclVerifier>();

        // W-3 (Drummond AC1): IModelDownloadVerifier — single sanctioned
        // SHA-256 verification seam for downloads landing under the models
        // root. Stateless + thread-safe → singleton.
        services.AddSingleton<IModelDownloadVerifier, Sha256ModelDownloadVerifier>();

        // W-3 (Drummond AC2): IModelStorageQuota — pre-flight quota check.
        // Defaults 50 GB total / 20 GB per file (overridable via StorageOptions).
        // Singleton holds the 30s directory-walk cache.
        services.AddSingleton<IModelStorageQuota, ModelStorageQuota>();

        // W-3 (Drummond AC1+AC2): ModelDownloadCoordinator — single sanctioned
        // write path into the models root. Combines name allowlist + quota +
        // atomic .tmp staging + SHA-256 verification.
        services.AddSingleton<ModelDownloadCoordinator>();

        // W-4 (Drummond W-4 AC4): IUserFolderHealthCheck — boot-time
        // reparse-point sweep over existing user folders (the operator-
        // visible scope). Stateless; safe as a singleton.
        services.AddSingleton<IUserFolderHealthCheck, UserFolderHealthCheck>();

        // W-4 (Drummond W-4 AC2): IUserFolderQuota — pre-flight quota
        // check for user-folder writes. Defaults 5 GB per folder /
        // 25 GB total (overridable via StorageOptions). Singleton holds
        // the per-folder 30s walk cache. Factory uses ActivatorUtilities
        // so [ActivatorUtilitiesConstructor] is honored (resolves the
        // ambiguity with the test-friendly ctor).
        services.AddSingleton<IUserFolderQuota>(sp =>
            ActivatorUtilities.CreateInstance<UserFolderQuota>(sp));

        services.AddScoped<IConversationStore, ConversationStore>();
        services.AddScoped<IAgentProfileStore, AgentProfileStore>();
        services.AddScoped<IModelProviderDefinitionStore, ModelProviderDefinitionStore>();
        services.AddScoped<IToolTestRecordStore, ToolTestRecordStore>();
        services.AddScoped<ISecretsStore, SecretsStore>();
        services.AddSingleton<OpenClawNet.Mcp.Abstractions.IMcpServerCatalog, McpServerCatalog>();

        return services;
    }
}

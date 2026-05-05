using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace OpenClawNet.Skills;

/// <summary>
/// K-1b — DI registration for the real <see cref="ISkillsRegistry"/>
/// (replaces the K-1a <c>AddOpenClawNetSkillsStub</c>).
/// </summary>
public static class SkillsServiceCollectionExtensions
{
    /// <summary>
    /// K-1b — Registers <see cref="OpenClawNetSkillsRegistry"/> as a
    /// singleton implementation of <see cref="ISkillsRegistry"/>, runs the
    /// <see cref="SystemSkillsSeeder"/> at boot (idempotent), and registers
    /// the K-1b #4 scoped <see cref="OpenClawNetSkillsProvider"/>.
    /// </summary>
    public static IServiceCollection AddOpenClawNetSkills(this IServiceCollection services)
    {
        // Seed the system layer from bundled SystemSkills/** content. Done
        // eagerly during DI registration so the first registry build sees
        // the system-layer files. Idempotent; safe to call repeatedly.
        // Wrapped in a hosted service so it runs after configuration but
        // before the registry's first scan? — registry rebuilds on watcher
        // fire anyway, so seeding once at registration is sufficient for
        // the eager initial build.
        services.AddSingleton<OpenClawNetSkillsRegistry>(sp =>
        {
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var seederLogger = loggerFactory?.CreateLogger(typeof(SystemSkillsSeeder).FullName!);
            try
            {
                SystemSkillsSeeder.Seed(seederLogger);
            }
            catch (Exception ex)
            {
                // Seeding failure is recoverable — operator can manually
                // copy the system skills. Don't crash boot.
                seederLogger?.LogWarning(ex, "SystemSkillsSeeder failed; continuing without system layer seed.");
            }

            var registryLogger = loggerFactory?.CreateLogger<OpenClawNetSkillsRegistry>();
            return new OpenClawNetSkillsRegistry(registryLogger);
        });

        services.AddSingleton<ISkillsRegistry>(sp => sp.GetRequiredService<OpenClawNetSkillsRegistry>());

        // K-1b #3 — the registry now owns the FileSystemWatcher itself
        // (debounce + snapshot diff + change notification). Bind the
        // ISkillsSnapshotChangeNotifier interface to the same singleton.
        services.AddSingleton<ISkillsSnapshotChangeNotifier>(
            sp => sp.GetRequiredService<OpenClawNetSkillsRegistry>());

        // K-1b #3 — per-chat-turn snapshot pin. Scoped so each request
        // has its own pin holder; the K-1b #4 OpenClawNetSkillsProvider
        // calls Pin() on its first turn-context callback.
        services.AddScoped<SkillsTurnPin>();

        // K-1b #4 — scoped MAF AIContextProvider. One instance per request;
        // DefaultAgentRuntime resolves it from the request scope and adds
        // it to ChatClientAgentOptions.AIContextProviders.
        services.AddScoped<OpenClawNetSkillsProvider>();

        // K-4 — external skill import (preview/confirm). Registers the
        // named "github-raw" HttpClient (BaseAddress + 30s timeout), the
        // import service singleton (carries an in-memory preview cache),
        // and a no-op audit logger which Petey's K-2 will replace.
        services.AddHttpClient(SkillImportService.HttpClientName, c =>
        {
            c.BaseAddress = new Uri("https://raw.githubusercontent.com/");
            c.Timeout = TimeSpan.FromSeconds(30);
        });
        services.TryAddSingleton<ISkillImportLogger>(NullSkillImportLogger.Instance);
        services.AddSingleton<ISkillImportService, SkillImportService>();

        return services;
    }
}

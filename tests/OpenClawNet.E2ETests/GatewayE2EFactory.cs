using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Gateway;
using OpenClawNet.Storage;

namespace OpenClawNet.E2ETests;

/// <summary>
/// E2E WebApplicationFactory that boots the real Gateway with Azure OpenAI as
/// the live model provider, an isolated storage root (so tests don't pollute
/// <c>C:\openclawnet</c>), and an in-memory EF Core database.
/// </summary>
/// <remarks>
/// <para>
/// The factory sets <c>OPENCLAWNET_STORAGE_ROOT</c> on the process before the
/// host is built so <see cref="OpenClawNet.Storage.OpenClawNetPaths.ResolveRoot"/>
/// resolves to the per-instance temp folder. The folder is created eagerly and
/// torn down in <see cref="Dispose(bool)"/>.
/// </para>
/// <para>
/// Tests that require Azure OpenAI gate themselves with
/// <see cref="E2EEnvironment.HasAzureOpenAi"/> + <c>Skip.IfNot</c>.
/// </para>
/// </summary>
public sealed class GatewayE2EFactory : WebApplicationFactory<GatewayProgramMarker>
{
    public string StorageRoot { get; }

    private readonly string? _previousStorageRoot;

    public GatewayE2EFactory()
    {
        // Per-instance temp root keeps tests hermetic.
        StorageRoot = Path.Combine(
            Path.GetTempPath(),
            "openclawnet-e2e",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(StorageRoot);

        // Snapshot any pre-existing value so we restore it on dispose. The
        // Gateway reads OPENCLAWNET_STORAGE_ROOT through OpenClawNetPaths,
        // which is a static helper — there's no DI seam to override.
        _previousStorageRoot = Environment.GetEnvironmentVariable(
            OpenClawNetPaths.EnvironmentVariableName);
        Environment.SetEnvironmentVariable(
            OpenClawNetPaths.EnvironmentVariableName, StorageRoot);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            var (endpoint, apiKey, deployment, authMode) = E2EEnvironment.ReadAzureOpenAi();

            var dict = new Dictionary<string, string?>
            {
                // EF Core wiring — we replace with in-memory below, but the
                // string still has to parse for the boot-time SqliteConnection.
                ["ConnectionStrings:openclawnet-db"] = "Data Source=:memory:",
                ["Teams:Enabled"] = "false",
                // Azure OpenAI provider config — when secrets are absent these
                // remain null and the provider stays unconfigured. Tests that
                // need a live model gate themselves with E2EEnvironment.HasAzureOpenAi.
                ["Model:Provider"] = "azure-openai",
                ["Model:Endpoint"] = endpoint,
                ["Model:ApiKey"] = apiKey,
                ["Model:DeploymentName"] = deployment,
                ["Model:AuthMode"] = authMode ?? "key",
            };

            cfg.AddInMemoryCollection(dict);
        });

        builder.ConfigureServices(services =>
        {
            // Same DB-swap pattern the IntegrationTests project uses: the
            // gateway registers a Sqlite IDbContextFactory; we replace it with
            // an InMemory one so tests don't hit disk.
            var dbDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<OpenClawDbContext>)
                         || d.ServiceType == typeof(IDbContextFactory<OpenClawDbContext>))
                .ToList();
            foreach (var d in dbDescriptors) services.Remove(d);

            var opts = new DbContextOptionsBuilder<OpenClawDbContext>()
                .UseInMemoryDatabase($"e2e-{Guid.NewGuid()}")
                .Options;
            services.AddSingleton<IDbContextFactory<OpenClawDbContext>>(
                new E2EDbContextFactory(opts));

            // JobExecutor is required by the job endpoints; the integration
            // tests register it manually for the same reason.
            services.AddScoped<OpenClawNet.Gateway.Services.JobExecutor>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        // Restore (or clear) the env var so we don't leak state across tests.
        Environment.SetEnvironmentVariable(
            OpenClawNetPaths.EnvironmentVariableName, _previousStorageRoot);

        try
        {
            if (Directory.Exists(StorageRoot))
                Directory.Delete(StorageRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; a leaked temp folder is not fatal.
        }
    }

    /// <summary>
    /// Forces the default agent profile to fall through <see cref="OpenClawNet.Gateway.Services.ProviderResolver"/>
    /// to <see cref="OpenClawNet.Gateway.Services.RuntimeModelSettings"/> (which we
    /// configured to <c>azure-openai</c>). The seeded profile defaults to
    /// <c>Provider="ollama-default"</c> which would otherwise route the chat to
    /// a local Ollama instance instead of Azure OpenAI.
    /// </summary>
    /// <remarks>
    /// Idempotent: callable once per test, no-op on second invocation.
    /// </remarks>
    public async Task ConfigureDefaultProfileForAzureOpenAiAsync(CancellationToken ct = default)
    {
        // Make sure the host is started so the profile has been seeded.
        _ = CreateClient();

        using var scope = Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAgentProfileStore>();
        var profile = await store.GetDefaultAsync(ct);

        // Provider="" → ProviderResolver falls back to RuntimeModelSettings.Current
        // (case 3 in ProviderResolver.ResolveAsync). That's our Azure OpenAI config.
        var updated = new OpenClawNet.Models.Abstractions.AgentProfile
        {
            Name = profile.Name,
            DisplayName = profile.DisplayName,
            IsDefault = true,
            IsEnabled = true,
            Provider = null,
            Endpoint = null,
            ApiKey = null,
            DeploymentName = null,
            AuthMode = null,
            Instructions = profile.Instructions,
            EnabledTools = profile.EnabledTools,
            RequireToolApproval = profile.RequireToolApproval,
            Kind = profile.Kind,
            CreatedAt = profile.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
        };
        await store.SaveAsync(updated, ct);
    }

    private sealed class E2EDbContextFactory(DbContextOptions<OpenClawDbContext> options)
        : IDbContextFactory<OpenClawDbContext>
    {
        public OpenClawDbContext CreateDbContext() => new(options);
    }
}

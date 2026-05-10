using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Storage;

namespace OpenClawNet.UnitTests.Storage;

public sealed class VaultConfigBackendsTests
{
    [Fact]
    public void WhenBackendsAbsent_DefaultsToSqliteSecretsStore()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddOpenClawStorage("Data Source=:memory:");
        using var sp = services.BuildServiceProvider();

        var store = sp.GetRequiredService<ISecretsStore>();

        Assert.IsType<SecretsStore>(store);
    }

    [Fact]
    public async Task BackendsConfig_BuildsChainedStore_InConfiguredOrder()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vault:Backends:0"] = "Environment",
                ["Vault:Backends:1"] = "Sqlite"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddDataProtection();
        services.AddSingleton<IConfiguration>(config);
        services.AddOpenClawStorage("Data Source=:memory:");
        using var sp = services.BuildServiceProvider();

        await EnsureDbCreatedAsync(sp);

        var envName = $"CHAIN_{Guid.NewGuid():N}";
        var envKey = $"{EnvironmentSecretsStoreOptions.DefaultPrefix}{NormalizeEnvKey(envName)}";
        try
        {
            Environment.SetEnvironmentVariable(envKey, "env-value");
            var sqlite = sp.GetRequiredService<SecretsStore>();
            await sqlite.SetAsync(envName, "sqlite-value");

            var store = sp.GetRequiredService<ISecretsStore>();
            var value = await store.GetAsync(envName);

            Assert.IsType<ChainedSecretsStore>(store);
            Assert.Equal("env-value", value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, null);
        }
    }

    private static async Task EnsureDbCreatedAsync(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        await SchemaMigrator.MigrateAsync(db);
    }

    private static string NormalizeEnvKey(string name)
    {
        return string.Concat(name.Select(ch => char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_'));
    }
}

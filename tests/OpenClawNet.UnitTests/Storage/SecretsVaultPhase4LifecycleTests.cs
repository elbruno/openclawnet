using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.UnitTests.Storage;

public sealed class SecretsVaultPhase4LifecycleTests
{
    private static (SecretsStore Store, IDbContextFactory<OpenClawDbContext> Factory) CreateStore()
    {
        var dbName = $"secrets-phase4-{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDataProtection();
        services.AddDbContextFactory<OpenClawDbContext>(o => o.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
        var store = new SecretsStore(factory, sp.GetRequiredService<IDataProtectionProvider>());
        return (store, factory);
    }

    [Fact]
    public async Task RotateAsync_CreatesNewVersion_AndMovesCurrentAtomically()
    {
        var (store, factory) = CreateStore();
        await store.SetAsync("GitHub/Token", "v1", "token");

        await store.RotateAsync("GitHub/Token", "v2");

        Assert.Equal("v2", await store.GetAsync("GitHub/Token"));
        var postRotateReads = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => store.GetAsync("GitHub/Token")));
        Assert.All(postRotateReads, value => Assert.Equal("v2", value));
        Assert.Equal([1, 2], await store.ListVersionsAsync("GitHub/Token"));
        await using var db = await factory.CreateDbContextAsync();
        var versions = await db.SecretVersions
            .Where(v => v.SecretName == "GitHub/Token")
            .OrderBy(v => v.Version)
            .ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.False(versions[0].IsCurrent);
        Assert.NotNull(versions[0].SupersededAt);
        Assert.True(versions[1].IsCurrent);
        Assert.Equal(1, versions.Count(v => v.IsCurrent));
    }

    [Fact]
    public async Task GetAsync_LatestAndExplicitVersions_ReturnExpectedValues()
    {
        var (store, _) = CreateStore();
        await store.SetAsync("Provider/ApiKey", "version-one");
        await store.RotateAsync("Provider/ApiKey", "version-two");
        await store.RotateAsync("Provider/ApiKey", "version-three");

        Assert.Equal("version-three", await store.GetAsync("Provider/ApiKey"));
        Assert.Equal("version-one", await store.GetAsync("Provider/ApiKey", version: 1));
        Assert.Equal("version-two", await store.GetAsync("Provider/ApiKey", version: 2));
        Assert.Equal("version-three", await store.GetAsync("Provider/ApiKey", version: 3));
        Assert.Null(await store.GetAsync("Provider/ApiKey", version: 4));
    }

    [Fact]
    public async Task SoftDeleteRecoverAndPurge_EnforceLifecycleAccess()
    {
        var (store, factory) = CreateStore();
        await store.SetAsync("Lifecycle/Secret", "active");
        await store.RotateAsync("Lifecycle/Secret", "rotated");

        Assert.True(await store.DeleteAsync("Lifecycle/Secret"));
        Assert.Null(await store.GetAsync("Lifecycle/Secret"));
        Assert.Null(await store.GetAsync("Lifecycle/Secret", version: 1));

        Assert.True(await store.RecoverAsync("Lifecycle/Secret"));
        Assert.Equal("rotated", await store.GetAsync("Lifecycle/Secret"));
        Assert.Equal("active", await store.GetAsync("Lifecycle/Secret", version: 1));

        Assert.True(await store.PurgeAsync("Lifecycle/Secret"));
        Assert.Null(await store.GetAsync("Lifecycle/Secret"));
        Assert.Empty(await store.ListVersionsAsync("Lifecycle/Secret"));
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.Secrets.AnyAsync(s => s.Name == "Lifecycle/Secret"));
        Assert.False(await db.SecretVersions.AnyAsync(v => v.SecretName == "Lifecycle/Secret"));
    }

    [Fact]
    public async Task AuditHashChain_VerifyDetectsTampering()
    {
        var (_, factory) = CreateStore();
        var auditor = new SecretAccessAuditor(factory, NullLogger<SecretAccessAuditor>.Instance);
        await auditor.RecordAsync("Audit/Secret", new VaultCallerContext(VaultCallerType.Tool, "tool-a", "s1"), success: true);
        await auditor.RecordAsync("Audit/Secret", new VaultCallerContext(VaultCallerType.Tool, "tool-a", "s2"), success: false);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await SecretAccessAuditHashChain.VerifyAsync(db));

        var second = await db.SecretAccessAudit
            .OrderBy(a => a.Sequence ?? 0)
            .ThenBy(a => a.AccessedAt)
            .ThenBy(a => a.Id)
            .Skip(1)
            .FirstAsync();
        second.Success = true;
        await db.SaveChangesAsync();

        Assert.False(await SecretAccessAuditHashChain.VerifyAsync(db));
    }

    [Fact]
    public async Task ConcurrentRotation_ProducesSequentialVersionsWithSingleCurrent()
    {
        var (store, factory) = CreateStore();
        await store.SetAsync("Concurrent/Token", "initial");

        // Simulate 10 concurrent rotations
        var rotations = Enumerable.Range(1, 10)
            .Select(i => Task.Run(async () => await store.RotateAsync("Concurrent/Token", $"rotated-{i}")))
            .ToArray();
        await Task.WhenAll(rotations);

        // Verify: exactly one current version exists
        await using var db = await factory.CreateDbContextAsync();
        var versions = await db.SecretVersions
            .Where(v => v.SecretName == "Concurrent/Token")
            .OrderBy(v => v.Version)
            .ToListAsync();

        Assert.Equal(11, versions.Count); // initial + 10 rotations
        Assert.Single(versions.Where(v => v.IsCurrent));
        var currentVersion = versions.Single(v => v.IsCurrent);
        Assert.Equal(11, currentVersion.Version);

        // Verify: all reads return the same current value
        var reads = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => store.GetAsync("Concurrent/Token")));
        var currentValue = reads[0];
        Assert.All(reads, value => Assert.Equal(currentValue, value));

        // Verify: versions are sequential from 1..11
        var versionNumbers = versions.Select(v => v.Version).ToList();
        Assert.Equal(Enumerable.Range(1, 11).ToList(), versionNumbers);
    }
}

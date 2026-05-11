using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OpenClawNet.Storage;

public sealed class VaultConfigurationResolver : IVaultCacheInvalidator
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _versions = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ttl;

    public VaultConfigurationResolver() : this(TimeProvider.System, DefaultTtl) { }

    internal VaultConfigurationResolver(TimeProvider timeProvider, TimeSpan ttl)
    {
        _timeProvider = timeProvider;
        _ttl = ttl;
    }

    public async Task<IReadOnlyDictionary<string, string?>> ResolveReferencesAsync(
        IConfiguration configuration,
        IVault vault,
        CancellationToken ct = default)
    {
        var overlay = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in configuration.AsEnumerable())
        {
            if (!TryParseVaultReference(pair.Value, out var secretName))
                continue;

            overlay[pair.Key] = await ResolveSecretAsync(secretName, vault, ct).ConfigureAwait(false);
        }

        return overlay;
    }

    internal async Task<string?> ResolveSecretAsync(string secretName, IVault vault, CancellationToken ct = default)
    {
        while (true)
        {
            var now = _timeProvider.GetUtcNow();
            var version = GetVersion(secretName);
            if (_cache.TryGetValue(secretName, out var cached) && cached.Version == version && cached.ExpiresAt > now)
                return cached.Value;

            var value = await vault.ResolveAsync(
                secretName,
                new VaultCallerContext(VaultCallerType.Configuration, "IConfiguration", null),
                ct).ConfigureAwait(false);

            var currentVersion = GetVersion(secretName);
            if (currentVersion != version)
                continue;

            _cache[secretName] = new CacheEntry(value, _timeProvider.GetUtcNow().Add(_ttl), currentVersion);
            return value;
        }
    }

    public void Invalidate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        _versions.AddOrUpdate(name, 1, (_, current) => unchecked(current + 1));
        _cache.TryRemove(name, out _);
    }

    private long GetVersion(string name) => _versions.TryGetValue(name, out var version) ? version : 0;

    public static bool TryParseVaultReference(string? value, out string name)
    {
        const string Prefix = "vault://";
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        name = value[Prefix.Length..].Trim();
        return name.Length > 0;
    }

    private sealed record CacheEntry(string? Value, DateTimeOffset ExpiresAt, long Version);
}

public static class VaultConfigurationExtensions
{
    public static async Task AddResolvedVaultReferencesAsync(
        this IConfigurationManager configuration,
        IServiceProvider services,
        CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<VaultConfigurationResolver>();
        var vault = scope.ServiceProvider.GetRequiredService<IVault>();
        var overlay = await resolver.ResolveReferencesAsync(configuration, vault, ct).ConfigureAwait(false);
        if (overlay.Count > 0)
            configuration.AddInMemoryCollection(overlay);
    }
}

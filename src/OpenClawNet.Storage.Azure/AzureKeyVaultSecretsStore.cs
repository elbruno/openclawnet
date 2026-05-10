using System.Collections.Concurrent;
using System.Text;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using OpenClawNet.Storage;

namespace OpenClawNet.Storage.Azure;

public sealed class AzureKeyVaultSecretsStoreOptions
{
    public const string SectionName = "Storage:Azure:KeyVault";
    public int CacheTtlMinutes { get; set; } = 15;
}

/// <summary>Read-only Key Vault-backed secrets store.</summary>
public sealed class AzureKeyVaultSecretsStore : ISecretsStore
{
    private readonly SecretClient _client;
    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public AzureKeyVaultSecretsStore(SecretClient client, IOptions<AzureKeyVaultSecretsStoreOptions> options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        var ttlMinutes = options?.Value?.CacheTtlMinutes ?? 15;
        _ttl = TimeSpan.FromMinutes(ttlMinutes <= 0 ? 15 : ttlMinutes);
    }

    public async Task<string?> GetAsync(string name, CancellationToken ct = default)
        => await GetAsync(name, version: null, ct).ConfigureAwait(false);

    public async Task<string?> GetAsync(string name, int? version, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        if (!TryMapName(name, out var mapped))
            throw new ArgumentException("Secret name contains unsupported characters for Azure Key Vault.", nameof(name));

        var cacheKey = version is null ? name : $"{name}@{version.Value}";
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return cached.Value;

        try
        {
            var response = await _client.GetSecretAsync(mapped, version?.ToString(), cancellationToken: ct).ConfigureAwait(false);
            var value = response.Value.Value;
            _cache[cacheKey] = new CacheEntry(value, DateTimeOffset.UtcNow.Add(_ttl));
            return value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _cache.TryRemove(cacheKey, out _);
            return null;
        }
        catch (Exception ex)
        {
            throw new VaultException(ex);
        }
    }

    public async Task<IReadOnlyList<int>> ListVersionsAsync(string name, CancellationToken ct = default)
    {
        if (!TryMapName(name, out var mapped))
            throw new ArgumentException("Secret name contains unsupported characters for Azure Key Vault.", nameof(name));

        var versions = new List<int>();
        await foreach (var properties in _client.GetPropertiesOfSecretVersionsAsync(mapped, ct).ConfigureAwait(false))
        {
            if (int.TryParse(properties.Version, out var version))
                versions.Add(version);
        }

        return versions.Order().ToList();
    }

    public async Task<IReadOnlyList<SecretSummary>> ListAsync(CancellationToken ct = default)
    {
        var results = new List<SecretSummary>();
        await foreach (var properties in _client.GetPropertiesOfSecretsAsync(ct))
        {
            var updatedAt = properties.UpdatedOn?.UtcDateTime ?? DateTime.UtcNow;
            results.Add(new SecretSummary(properties.Name, null, updatedAt));
        }

        return results
            .OrderBy(summary => summary.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task SetAsync(string name, string value, string? description = null, CancellationToken ct = default)
    {
        if (!TryMapName(name, out var mapped))
            throw new ArgumentException("Secret name contains unsupported characters for Azure Key Vault.", nameof(name));

        try
        {
            await _client.SetSecretAsync(mapped, value, ct).ConfigureAwait(false);
            // Invalidate all version-specific cache entries
            var keysToRemove = _cache.Keys.Where(k => k == name || k.StartsWith($"{name}@", StringComparison.Ordinal)).ToList();
            foreach (var key in keysToRemove)
                _cache.TryRemove(key, out _);
        }
        catch (Exception ex)
        {
            throw new VaultException(ex);
        }
    }

    public async Task RotateAsync(string name, string newValue, CancellationToken ct = default)
    {
        if (!TryMapName(name, out var mapped))
            throw new ArgumentException("Secret name contains unsupported characters for Azure Key Vault.", nameof(name));

        try
        {
            await _client.SetSecretAsync(mapped, newValue, ct).ConfigureAwait(false);
            // Invalidate all version-specific cache entries
            var keysToRemove = _cache.Keys.Where(k => k == name || k.StartsWith($"{name}@", StringComparison.Ordinal)).ToList();
            foreach (var key in keysToRemove)
                _cache.TryRemove(key, out _);
        }
        catch (Exception ex)
        {
            throw new VaultException(ex);
        }
    }

    public async Task<bool> DeleteAsync(string name, CancellationToken ct = default)
    {
        if (!TryMapName(name, out var mapped))
            throw new ArgumentException("Secret name contains unsupported characters for Azure Key Vault.", nameof(name));

        try
        {
            await _client.StartDeleteSecretAsync(mapped, ct).ConfigureAwait(false);
            _cache.TryRemove(name, out _);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _cache.TryRemove(name, out _);
            return false;
        }
        catch (Exception ex)
        {
            throw new VaultException(ex);
        }
    }

    public async Task<bool> RecoverAsync(string name, CancellationToken ct = default)
    {
        if (!TryMapName(name, out var mapped))
            throw new ArgumentException("Secret name contains unsupported characters for Azure Key Vault.", nameof(name));

        try
        {
            await _client.StartRecoverDeletedSecretAsync(mapped, ct).ConfigureAwait(false);
            _cache.TryRemove(name, out _);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _cache.TryRemove(name, out _);
            return false;
        }
        catch (Exception ex)
        {
            throw new VaultException(ex);
        }
    }

    public async Task<bool> PurgeAsync(string name, CancellationToken ct = default)
    {
        if (!TryMapName(name, out var mapped))
            throw new ArgumentException("Secret name contains unsupported characters for Azure Key Vault.", nameof(name));

        try
        {
            await _client.PurgeDeletedSecretAsync(mapped, ct).ConfigureAwait(false);
            _cache.TryRemove(name, out _);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _cache.TryRemove(name, out _);
            return false;
        }
        catch (Exception ex)
        {
            throw new VaultException(ex);
        }
    }

    private static bool TryMapName(string name, out string mapped)
    {
        mapped = string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-')
            {
                builder.Append(ch);
            }
            else if (ch == '.' || ch == '_')
            {
                builder.Append('-');
            }
            else
            {
                return false;
            }
        }

        if (builder.Length == 0)
            return false;

        mapped = builder.ToString();
        return true;
    }

    private sealed record CacheEntry(string Value, DateTimeOffset ExpiresAt);
}

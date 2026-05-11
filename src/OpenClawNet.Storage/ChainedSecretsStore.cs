namespace OpenClawNet.Storage;

/// <summary>Chain-of-responsibility over ordered secret stores.</summary>
public sealed class ChainedSecretsStore : ISecretsStore
{
    private readonly IReadOnlyList<ISecretsStore> _stores;

    public ChainedSecretsStore(IReadOnlyList<ISecretsStore> stores)
    {
        if (stores is null)
            throw new ArgumentNullException(nameof(stores));
        if (stores.Count == 0)
            throw new ArgumentException("At least one secrets store is required.", nameof(stores));
        _stores = stores;
    }

    public async Task<string?> GetAsync(string name, CancellationToken ct = default)
    {
        foreach (var store in _stores)
        {
            var value = await store.GetAsync(name, ct).ConfigureAwait(false);
            if (value is not null)
                return value;
        }

        return null;
    }

    public async Task<string?> GetAsync(string name, int? version, CancellationToken ct = default)
    {
        foreach (var store in _stores)
        {
            var value = await store.GetAsync(name, version, ct).ConfigureAwait(false);
            if (value is not null)
                return value;
        }

        return null;
    }

    public async Task<IReadOnlyList<SecretSummary>> ListAsync(CancellationToken ct = default)
    {
        var results = new Dictionary<string, SecretSummary>(StringComparer.Ordinal);
        foreach (var store in _stores)
        {
            var entries = await store.ListAsync(ct).ConfigureAwait(false);
            foreach (var summary in entries)
            {
                if (!results.ContainsKey(summary.Name))
                    results[summary.Name] = summary;
            }
        }

        return results.Values
            .OrderBy(summary => summary.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task SetAsync(string name, string value, string? description = null, CancellationToken ct = default)
    {
        foreach (var store in _stores)
        {
            try
            {
                await store.SetAsync(name, value, description, ct).ConfigureAwait(false);
                return;
            }
            catch (NotSupportedException)
            {
                // Move to next writable store.
            }
        }

        throw new NotSupportedException("No writable secrets store is configured.");
    }

    public async Task<bool> DeleteAsync(string name, CancellationToken ct = default)
    {
        foreach (var store in _stores)
        {
            try
            {
                return await store.DeleteAsync(name, ct).ConfigureAwait(false);
            }
            catch (NotSupportedException)
            {
                // Move to next writable store.
            }
        }

        throw new NotSupportedException("No writable secrets store is configured.");
    }

    public async Task<IReadOnlyList<int>> ListVersionsAsync(string name, CancellationToken ct = default)
    {
        foreach (var store in _stores)
        {
            try
            {
                return await store.ListVersionsAsync(name, ct).ConfigureAwait(false);
            }
            catch (NotSupportedException)
            {
            }
        }

        throw new NotSupportedException("No versioned secrets store is configured.");
    }

    public async Task RotateAsync(string name, string newValue, CancellationToken ct = default)
    {
        foreach (var store in _stores)
        {
            try
            {
                await store.RotateAsync(name, newValue, ct).ConfigureAwait(false);
                return;
            }
            catch (NotSupportedException)
            {
            }
        }

        throw new NotSupportedException("No writable secrets store is configured.");
    }

    public async Task<bool> RecoverAsync(string name, CancellationToken ct = default)
    {
        foreach (var store in _stores)
        {
            try
            {
                return await store.RecoverAsync(name, ct).ConfigureAwait(false);
            }
            catch (NotSupportedException)
            {
            }
        }

        throw new NotSupportedException("No recoverable secrets store is configured.");
    }

    public async Task<bool> PurgeAsync(string name, CancellationToken ct = default)
    {
        foreach (var store in _stores)
        {
            try
            {
                return await store.PurgeAsync(name, ct).ConfigureAwait(false);
            }
            catch (NotSupportedException)
            {
            }
        }

        throw new NotSupportedException("No purgeable secrets store is configured.");
    }
}

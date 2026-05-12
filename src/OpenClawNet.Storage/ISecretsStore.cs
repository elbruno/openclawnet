namespace OpenClawNet.Storage;

/// <summary>
/// CRUD over the encrypted Secrets table. Plaintext values never round-trip to
/// disk; the implementation handles DataProtection encryption transparently.
/// </summary>
public interface ISecretsStore
{
    /// <summary>Returns the plaintext value, or <c>null</c> when the secret is not present.</summary>
    Task<string?> GetAsync(string name, CancellationToken ct = default);

    /// <summary>Returns the plaintext value for a specific version, or the current version when <paramref name="version"/> is <c>null</c>.</summary>
    Task<string?> GetAsync(string name, int? version, CancellationToken ct = default) =>
        version is null ? GetAsync(name, ct) : throw new NotSupportedException("This secrets store does not support versioned reads.");

    /// <summary>Lists secret names + descriptions (no values returned, by design).</summary>
    Task<IReadOnlyList<SecretSummary>> ListAsync(CancellationToken ct = default);

    /// <summary>Insert or update a secret. The plaintext is encrypted before persistence.</summary>
    Task SetAsync(string name, string value, string? description = null, CancellationToken ct = default);

    /// <summary>Lists available version numbers for a secret.</summary>
    Task<IReadOnlyList<int>> ListVersionsAsync(string name, CancellationToken ct = default) =>
        throw new NotSupportedException("This secrets store does not support version listing.");

    /// <summary>Creates a new secret version and makes it current.</summary>
    Task RotateAsync(string name, string newValue, CancellationToken ct = default) =>
        SetAsync(name, newValue, description: null, ct);

    /// <summary>Soft-deletes a secret by name. Returns true when an active row was deleted.</summary>
    Task<bool> DeleteAsync(string name, CancellationToken ct = default);

    /// <summary>Recovers a soft-deleted secret.</summary>
    Task<bool> RecoverAsync(string name, CancellationToken ct = default) =>
        throw new NotSupportedException("This secrets store does not support recovery.");

    /// <summary>Permanently removes a secret and all of its versions.</summary>
    Task<bool> PurgeAsync(string name, CancellationToken ct = default) =>
        DeleteAsync(name, ct);

    /// <summary>
    /// Atomically sets multiple secrets (template bundle). Validates all fields before any writes occur.
    /// The default implementation validates the bundle then iterates <see cref="SetAsync"/>; transactional
    /// stores (e.g. <c>SecretsStore</c>) override this to wrap the writes in a single DB transaction.
    /// Read-only stores will surface <see cref="NotSupportedException"/> from <see cref="SetAsync"/>.
    /// </summary>
    async Task SetBundleAsync(IReadOnlyDictionary<string, string> secrets, CancellationToken ct = default)
    {
        if (secrets is null || secrets.Count == 0)
            throw new ArgumentException("Secret bundle cannot be empty.", nameof(secrets));

        foreach (var (name, value) in secrets)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Secret name is required in bundle.", nameof(secrets));
            if (value is null)
                throw new ArgumentException($"Secret value is required for '{name}'.", nameof(secrets));
        }

        foreach (var (name, value) in secrets)
        {
            await SetAsync(name, value, description: null, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>Metadata-only projection (no plaintext value) suitable for UI listings.</summary>
public sealed record SecretSummary(string Name, string? Description, DateTime UpdatedAt);

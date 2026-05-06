using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Storage;

/// <summary>
/// SQLite + ASP.NET Core DataProtection-backed secret store.
/// Plaintext values are encrypted with a purpose-bound protector before they
/// are written to the <c>Secrets</c> table; reads decrypt on the fly.
/// </summary>
public sealed class SecretsStore : ISecretsStore
{
    // DataProtection purpose string — changing this rotates the keyset and
    // invalidates every existing ciphertext, so be careful.
    private const string ProtectorPurpose = "OpenClawNet.Secrets.v1";

    private readonly IDbContextFactory<OpenClawDbContext> _dbFactory;
    private readonly IDataProtector _protector;
    private readonly IReadOnlyList<IVaultCacheInvalidator> _cacheInvalidators;

    public SecretsStore(
        IDbContextFactory<OpenClawDbContext> dbFactory,
        IDataProtectionProvider dataProtection,
        IEnumerable<IVaultCacheInvalidator>? cacheInvalidators = null)
    {
        _dbFactory = dbFactory;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _cacheInvalidators = cacheInvalidators?.ToList() ?? [];
    }

    public async Task<string?> GetAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Secrets.AsNoTracking().FirstOrDefaultAsync(s => s.Name == name, ct);
        if (row is null) return null;
        try { return _protector.Unprotect(row.EncryptedValue); }
        catch { return null; }
    }

    public async Task<IReadOnlyList<SecretSummary>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Secrets
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new SecretSummary(s.Name, s.Description, s.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task SetAsync(string name, string value, string? description = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Secret name is required.", nameof(name));
        if (value is null)
            throw new ArgumentNullException(nameof(value));

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Secrets.FirstOrDefaultAsync(s => s.Name == name, ct);
        var ciphertext = _protector.Protect(value);
        if (existing is null)
        {
            db.Secrets.Add(new SecretEntity
            {
                Name = name,
                EncryptedValue = ciphertext,
                Description = description,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.EncryptedValue = ciphertext;
            // Don't blow away an existing description if the caller didn't supply one.
            if (description is not null) existing.Description = description;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        Invalidate(name);
    }

    public async Task<bool> DeleteAsync(string name, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Secrets.FirstOrDefaultAsync(s => s.Name == name, ct);
        if (row is null)
        {
            Invalidate(name);
            return false;
        }
        db.Secrets.Remove(row);
        await db.SaveChangesAsync(ct);
        Invalidate(name);
        return true;
    }

    private void Invalidate(string name)
    {
        foreach (var invalidator in _cacheInvalidators)
            invalidator.Invalidate(name);
    }
}

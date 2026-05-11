using System.Collections.Concurrent;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Storage;

/// <summary>
/// SQLite + ASP.NET Core DataProtection-backed secret store.
/// Plaintext values are encrypted with a purpose-bound protector before they
/// are written to the <c>Secrets</c> table; reads decrypt on the fly.
/// </summary>
public sealed class SecretsStore : ISecretsStore, IDisposable
{
    // DataProtection purpose string — changing this rotates the keyset and
    // invalidates every existing ciphertext, so be careful.
    private const string ProtectorPurpose = "OpenClawNet.Secrets.v1";

    // Process-wide per-secret locks to handle concurrent rotations across multiple store instances
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PerSecretLocks = new();

    private readonly IDbContextFactory<OpenClawDbContext> _dbFactory;
    private readonly IDataProtector _protector;
    private readonly IReadOnlyList<IVaultCacheInvalidator> _cacheInvalidators;
    private readonly SemaphoreSlim _backfillLock = new(1, 1);

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
        => await GetAsync(name, version: null, ct).ConfigureAwait(false);

    public async Task<string?> GetAsync(string name, int? version, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await BackfillVersionAsync(db, name, ct).ConfigureAwait(false);
        var secret = await db.Secrets.AsNoTracking().FirstOrDefaultAsync(s => s.Name == name, ct);
        if (secret is null || secret.DeletedAt is not null) return null;

        var query = db.SecretVersions.AsNoTracking().Where(v => v.SecretName == name);
        var row = version is null
            ? await query.Where(v => v.IsCurrent).OrderByDescending(v => v.Version).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : await query.FirstOrDefaultAsync(v => v.Version == version.Value, ct).ConfigureAwait(false);

        if (row is null) return null;
        try { return _protector.Unprotect(row.EncryptedValue); }
        catch { return null; }
    }

    public async Task<IReadOnlyList<SecretSummary>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Secrets
            .AsNoTracking()
            .Where(s => s.DeletedAt == null)
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
        var now = DateTime.UtcNow;
        if (existing is null)
        {
            db.Secrets.Add(new SecretEntity
            {
                Name = name,
                EncryptedValue = ciphertext,
                Description = description,
                CreatedAt = now,
                UpdatedAt = now,
                Versions =
                [
                    new SecretVersionEntity
                    {
                        Id = Guid.NewGuid(),
                        SecretName = name,
                        Version = 1,
                        EncryptedValue = ciphertext,
                        CreatedAt = now,
                        IsCurrent = true
                    }
                ]
            });
        }
        else
        {
            existing.EncryptedValue = ciphertext;
            // Don't blow away an existing description if the caller didn't supply one.
            if (description is not null) existing.Description = description;
            existing.UpdatedAt = now;
            existing.DeletedAt = null;
            existing.PurgeAfter = null;
            await BackfillVersionAsync(db, name, ct).ConfigureAwait(false);
            await AddCurrentVersionAsync(db, name, ciphertext, now, ct).ConfigureAwait(false);
        }
        await db.SaveChangesAsync(ct);
        Invalidate(name);
    }

    public async Task<IReadOnlyList<int>> ListVersionsAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return [];
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await BackfillVersionAsync(db, name, ct).ConfigureAwait(false);
        return await db.SecretVersions
            .AsNoTracking()
            .Where(v => v.SecretName == name)
            .OrderBy(v => v.Version)
            .Select(v => v.Version)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task RotateAsync(string name, string newValue, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Secret name is required.", nameof(name));
        if (newValue is null)
            throw new ArgumentNullException(nameof(newValue));

        // Get or create a process-wide lock for this specific secret name
        var secretLock = PerSecretLocks.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));
        await secretLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var existing = await db.Secrets.FirstOrDefaultAsync(s => s.Name == name, ct).ConfigureAwait(false);
            if (existing is null)
            {
                await SetAsync(name, newValue, description: null, ct).ConfigureAwait(false);
                return;
            }

            if (existing.DeletedAt is not null)
                throw new InvalidOperationException("Cannot rotate a soft-deleted secret. Recover it first.");

            var now = DateTime.UtcNow;
            var ciphertext = _protector.Protect(newValue);
            existing.EncryptedValue = ciphertext;
            existing.UpdatedAt = now;
            await BackfillVersionAsync(db, name, ct).ConfigureAwait(false);
            await AddCurrentVersionAsync(db, name, ciphertext, now, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            Invalidate(name);
        }
        finally
        {
            secretLock.Release();
        }
    }

    public async Task<bool> DeleteAsync(string name, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Secrets.FirstOrDefaultAsync(s => s.Name == name, ct);
        if (row is null || row.DeletedAt is not null)
        {
            Invalidate(name);
            return false;
        }
        var now = DateTime.UtcNow;
        row.DeletedAt = now;
        row.PurgeAfter = now.AddDays(30);
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        Invalidate(name);
        return true;
    }

    public async Task<bool> RecoverAsync(string name, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Secrets.FirstOrDefaultAsync(s => s.Name == name, ct).ConfigureAwait(false);
        if (row?.DeletedAt is null)
        {
            Invalidate(name);
            return false;
        }

        row.DeletedAt = null;
        row.PurgeAfter = null;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        Invalidate(name);
        return true;
    }

    public async Task<bool> PurgeAsync(string name, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Secrets.FirstOrDefaultAsync(s => s.Name == name, ct).ConfigureAwait(false);
        if (row is null)
        {
            Invalidate(name);
            return false;
        }

        var versions = db.SecretVersions.Where(v => v.SecretName == name);
        db.SecretVersions.RemoveRange(versions);
        db.Secrets.Remove(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        Invalidate(name);
        return true;
    }

    private async Task BackfillVersionAsync(OpenClawDbContext db, string name, CancellationToken ct)
    {
        // Check outside lock first (fast path)
        if (await db.SecretVersions.AnyAsync(v => v.SecretName == name, ct).ConfigureAwait(false))
            return;

        await _backfillLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check inside lock
            if (await db.SecretVersions.AnyAsync(v => v.SecretName == name, ct).ConfigureAwait(false))
                return;

            var secret = await db.Secrets.FirstOrDefaultAsync(s => s.Name == name, ct).ConfigureAwait(false);
            if (secret is null)
                return;

            var createdAt = secret.CreatedAt == default ? secret.UpdatedAt : secret.CreatedAt;
            db.SecretVersions.Add(new SecretVersionEntity
            {
                Id = Guid.NewGuid(),
                SecretName = name,
                Version = 1,
                EncryptedValue = secret.EncryptedValue,
                CreatedAt = createdAt,
                IsCurrent = true
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _backfillLock.Release();
        }
    }

    private static async Task AddCurrentVersionAsync(OpenClawDbContext db, string name, string ciphertext, DateTime now, CancellationToken ct)
    {
        var currentVersions = await db.SecretVersions
            .Where(v => v.SecretName == name && v.IsCurrent)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var maxVersion = await db.SecretVersions
            .Where(v => v.SecretName == name)
            .Select(v => (int?)v.Version)
            .MaxAsync(ct)
            .ConfigureAwait(false) ?? 0;

        foreach (var current in currentVersions)
        {
            current.IsCurrent = false;
            current.SupersededAt = now;
        }

        db.SecretVersions.Add(new SecretVersionEntity
        {
            Id = Guid.NewGuid(),
            SecretName = name,
            Version = maxVersion + 1,
            EncryptedValue = ciphertext,
            CreatedAt = now,
            IsCurrent = true
        });
    }

    private void Invalidate(string name)
    {
        foreach (var invalidator in _cacheInvalidators)
            invalidator.Invalidate(name);
    }

    public void Dispose()
    {
        _backfillLock.Dispose();
    }
}

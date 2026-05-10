using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenClawNet.Storage.Entities;
using OpenClawNet.Tools.GoogleWorkspace;

namespace OpenClawNet.Storage;

/// <summary>
/// Encrypted SQLite-backed implementation of IGoogleOAuthTokenStore.
/// Uses ASP.NET Core DataProtection to encrypt access and refresh tokens at rest.
/// </summary>
public sealed class EncryptedSqliteOAuthTokenStore : IGoogleOAuthTokenStore
{
    private const string ProtectorPurpose = "OpenClawNet.OAuth.Google";
    private const string ProviderName = "google";

    private readonly IDbContextFactory<OpenClawDbContext> _dbFactory;
    private readonly IDataProtector _protector;
    private readonly ILogger<EncryptedSqliteOAuthTokenStore> _logger;

    public EncryptedSqliteOAuthTokenStore(
        IDbContextFactory<OpenClawDbContext> dbFactory,
        IDataProtectionProvider dataProtection,
        ILogger<EncryptedSqliteOAuthTokenStore> logger)
    {
        _dbFactory = dbFactory;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _logger = logger;
    }

    public async Task SaveTokenAsync(string userId, GoogleTokenSet tokens, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId is required.", nameof(userId));
        if (tokens is null)
            throw new ArgumentNullException(nameof(tokens));

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // Encrypt tokens before storage
        var accessCiphertext = _protector.Protect(tokens.AccessToken);
        var refreshCiphertext = _protector.Protect(tokens.RefreshToken);

        var existing = await db.OAuthTokens
            .FirstOrDefaultAsync(
                t => t.Provider == ProviderName && t.UserId == userId,
                cancellationToken);

        if (existing is null)
        {
            // Insert new record
            db.OAuthTokens.Add(new OAuthTokenEntity
            {
                Provider = ProviderName,
                UserId = userId,
                AccessTokenCiphertext = accessCiphertext,
                RefreshTokenCiphertext = refreshCiphertext,
                ExpiresAtUtc = tokens.ExpiresAtUtc.ToString("O"),
                Scopes = tokens.Scopes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            _logger.LogInformation(
                "Saved new OAuth tokens for provider {Provider}, user {UserId}",
                ProviderName,
                userId);
        }
        else
        {
            // Update existing record
            existing.AccessTokenCiphertext = accessCiphertext;
            existing.RefreshTokenCiphertext = refreshCiphertext;
            existing.ExpiresAtUtc = tokens.ExpiresAtUtc.ToString("O");
            existing.Scopes = tokens.Scopes;
            existing.UpdatedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "Updated OAuth tokens for provider {Provider}, user {UserId}",
                ProviderName,
                userId);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<GoogleTokenSet?> GetTokenAsync(string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.OAuthTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(
                t => t.Provider == ProviderName && t.UserId == userId,
                cancellationToken);

        if (entity is null)
            return null;

        try
        {
            // Decrypt tokens
            var accessToken = _protector.Unprotect(entity.AccessTokenCiphertext);
            var refreshToken = _protector.Unprotect(entity.RefreshTokenCiphertext);

            if (!DateTimeOffset.TryParse(entity.ExpiresAtUtc, out var expiresAt))
            {
                _logger.LogWarning(
                    "Failed to parse ExpiresAtUtc for user {UserId}, treating as expired",
                    userId);
                expiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            }

            return new GoogleTokenSet(
                AccessToken: accessToken,
                RefreshToken: refreshToken,
                ExpiresAtUtc: expiresAt,
                Scopes: entity.Scopes);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to decrypt OAuth tokens for user {UserId}. Tokens may have been encrypted with a different key.",
                userId);
            return null;
        }
    }

    public async Task DeleteTokenAsync(string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.OAuthTokens
            .FirstOrDefaultAsync(
                t => t.Provider == ProviderName && t.UserId == userId,
                cancellationToken);

        if (entity is null)
        {
            _logger.LogDebug(
                "No OAuth tokens found to delete for provider {Provider}, user {UserId}",
                ProviderName,
                userId);
            return;
        }

        db.OAuthTokens.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Deleted OAuth tokens for provider {Provider}, user {UserId}",
            ProviderName,
            userId);
    }
}

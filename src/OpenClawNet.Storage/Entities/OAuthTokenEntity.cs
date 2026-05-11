using System.ComponentModel.DataAnnotations;

namespace OpenClawNet.Storage.Entities;

/// <summary>
/// Encrypted OAuth token storage for external providers (e.g., Google).
/// Both access and refresh tokens are encrypted at rest using ASP.NET Core DataProtection.
/// </summary>
public class OAuthTokenEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>OAuth provider name (e.g., "google").</summary>
    public required string Provider { get; set; }

    /// <summary>User identifier within OpenClawNet (for multi-user scenarios).</summary>
    public required string UserId { get; set; }

    /// <summary>Encrypted access token ciphertext.</summary>
    public required string AccessTokenCiphertext { get; set; }

    /// <summary>Encrypted refresh token ciphertext.</summary>
    public required string RefreshTokenCiphertext { get; set; }

    /// <summary>UTC timestamp when the access token expires (ISO 8601 format).</summary>
    public required string ExpiresAtUtc { get; set; }

    /// <summary>Space-separated list of OAuth scopes granted.</summary>
    public required string Scopes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

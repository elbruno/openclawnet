namespace OpenClawNet.Tools.GoogleWorkspace;

/// <summary>
/// Token store interface for securely persisting Google OAuth tokens.
/// Implementation will handle encryption at rest via DPAPI or DataProtection.
/// </summary>
public interface IGoogleOAuthTokenStore
{
    /// <summary>
    /// Saves or updates OAuth tokens for a user.
    /// </summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="tokens">Token set to persist (will be encrypted).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveTokenAsync(string userId, GoogleTokenSet tokens, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves OAuth tokens for a user.
    /// </summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Decrypted token set, or null if no tokens exist for the user.</returns>
    Task<GoogleTokenSet?> GetTokenAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes OAuth tokens for a user (e.g., on revocation or logout).
    /// </summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteTokenAsync(string userId, CancellationToken cancellationToken);
}

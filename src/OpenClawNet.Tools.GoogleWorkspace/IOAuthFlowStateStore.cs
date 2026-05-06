namespace OpenClawNet.Tools.GoogleWorkspace;

/// <summary>
/// Store for short-lived OAuth flow state (state param → userId + code_verifier binding).
/// State values are single-use and expire after 10 minutes.
/// </summary>
public interface IOAuthFlowStateStore
{
    /// <summary>
    /// Stores a new OAuth flow state with generated state parameter.
    /// </summary>
    /// <param name="userId">User identifier initiating the OAuth flow.</param>
    /// <param name="codeVerifier">PKCE code verifier (must be kept secret until token exchange).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated state parameter (cryptographically random).</returns>
    Task<string> StoreAsync(string userId, string codeVerifier, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves and deletes OAuth flow state (one-shot consumption).
    /// </summary>
    /// <param name="state">State parameter from OAuth callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Flow state if found and not expired, null otherwise.</returns>
    Task<OAuthFlowState?> ConsumeAsync(string state, CancellationToken cancellationToken);
}

/// <summary>
/// OAuth flow state record (userId + code_verifier).
/// </summary>
/// <param name="UserId">User identifier.</param>
/// <param name="CodeVerifier">PKCE code verifier.</param>
public sealed record OAuthFlowState(string UserId, string CodeVerifier);

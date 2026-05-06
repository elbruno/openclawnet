namespace OpenClawNet.Tools.GoogleWorkspace;

/// <summary>
/// Stub implementation of IGoogleOAuthTokenStore that throws NotImplementedException.
/// This is a placeholder until S5-4/S5-5 implement the real token store with encryption.
/// </summary>
internal sealed class StubGoogleOAuthTokenStore : IGoogleOAuthTokenStore
{
    public Task SaveTokenAsync(string userId, GoogleTokenSet tokens, CancellationToken cancellationToken)
    {
        throw new NotImplementedException(
            "OAuth token storage not yet implemented. " +
            "S5-4/S5-5 will provide the real implementation with encryption.");
    }

    public Task<GoogleTokenSet?> GetTokenAsync(string userId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException(
            "OAuth token storage not yet implemented. " +
            "S5-4/S5-5 will provide the real implementation with encryption.");
    }

    public Task DeleteTokenAsync(string userId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException(
            "OAuth token storage not yet implemented. " +
            "S5-4/S5-5 will provide the real implementation with encryption.");
    }
}

using System.Collections.Concurrent;

namespace OpenClawNet.Tools.GoogleWorkspace;

/// <summary>
/// In-memory implementation of IGoogleOAuthTokenStore for development and testing.
/// NOT SUITABLE FOR PRODUCTION — tokens are lost on process restart and not encrypted.
/// S5-5 (Helly) will replace with EncryptedSqliteGoogleOAuthTokenStore.
/// </summary>
internal sealed class InMemoryGoogleOAuthTokenStore : IGoogleOAuthTokenStore
{
    private readonly ConcurrentDictionary<string, GoogleTokenSet> _tokens = new();

    public Task SaveTokenAsync(string userId, GoogleTokenSet tokens, CancellationToken cancellationToken)
    {
        _tokens[userId] = tokens;
        return Task.CompletedTask;
    }

    public Task<GoogleTokenSet?> GetTokenAsync(string userId, CancellationToken cancellationToken)
    {
        _tokens.TryGetValue(userId, out var tokens);
        return Task.FromResult(tokens);
    }

    public Task DeleteTokenAsync(string userId, CancellationToken cancellationToken)
    {
        _tokens.TryRemove(userId, out _);
        return Task.CompletedTask;
    }
}

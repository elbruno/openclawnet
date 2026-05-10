using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace OpenClawNet.Tools.GoogleWorkspace;

/// <summary>
/// In-memory implementation of IOAuthFlowStateStore with 10-minute TTL.
/// Production should use distributed cache (Redis) for multi-instance deployments.
/// </summary>
internal sealed class InMemoryOAuthFlowStateStore : IOAuthFlowStateStore
{
    private readonly ConcurrentDictionary<string, (OAuthFlowState State, DateTimeOffset ExpiresAt)> _store = new();
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

    public Task<string> StoreAsync(string userId, string codeVerifier, CancellationToken cancellationToken)
    {
        // Generate cryptographically random state parameter (32 bytes = 256 bits)
        var stateBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(stateBytes);
        }
        var state = Convert.ToBase64String(stateBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", ""); // URL-safe base64

        var flowState = new OAuthFlowState(userId, codeVerifier);
        var expiresAt = DateTimeOffset.UtcNow.Add(StateTtl);

        _store[state] = (flowState, expiresAt);

        // Sweep expired entries (simple cleanup on every store call)
        SweepExpired();

        return Task.FromResult(state);
    }

    public Task<OAuthFlowState?> ConsumeAsync(string state, CancellationToken cancellationToken)
    {
        if (!_store.TryRemove(state, out var entry))
        {
            return Task.FromResult<OAuthFlowState?>(null);
        }

        // Check expiration
        if (entry.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return Task.FromResult<OAuthFlowState?>(null);
        }

        return Task.FromResult<OAuthFlowState?>(entry.State);
    }

    private void SweepExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var expiredKeys = _store
            .Where(kvp => kvp.Value.ExpiresAt < now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _store.TryRemove(key, out _);
        }
    }
}

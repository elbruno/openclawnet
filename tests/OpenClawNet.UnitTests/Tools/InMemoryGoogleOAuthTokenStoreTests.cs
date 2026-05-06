using OpenClawNet.Tools.GoogleWorkspace;
using Xunit;

namespace OpenClawNet.UnitTests.Tools;

/// <summary>
/// Unit tests for InMemoryGoogleOAuthTokenStore (S5-7).
/// Validates save/get/delete operations and multi-user isolation.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Layer", "Unit")]
public sealed class InMemoryGoogleOAuthTokenStoreTests
{
    [Fact]
    public async Task SaveToken_And_GetToken_Roundtrip()
    {
        // ARRANGE
        var store = new InMemoryGoogleOAuthTokenStore();
        var tokenSet = new GoogleTokenSet(
            AccessToken: "access_token_123",
            RefreshToken: "refresh_token_456",
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1),
            Scopes: "https://www.googleapis.com/auth/gmail.readonly");

        // ACT
        await store.SaveTokenAsync("testuser", tokenSet, CancellationToken.None);
        var retrieved = await store.GetTokenAsync("testuser", CancellationToken.None);

        // ASSERT
        Assert.NotNull(retrieved);
        Assert.Equal("access_token_123", retrieved!.AccessToken);
        Assert.Equal("refresh_token_456", retrieved.RefreshToken);
        Assert.Equal("https://www.googleapis.com/auth/gmail.readonly", retrieved.Scopes);
        Assert.InRange(retrieved.ExpiresAtUtc, 
            DateTimeOffset.UtcNow.AddMinutes(55), 
            DateTimeOffset.UtcNow.AddMinutes(65));
    }

    [Fact]
    public async Task GetToken_Nonexistent_User_Returns_Null()
    {
        // ARRANGE
        var store = new InMemoryGoogleOAuthTokenStore();

        // ACT
        var result = await store.GetTokenAsync("nonexistent_user", CancellationToken.None);

        // ASSERT
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteToken_Removes_Token()
    {
        // ARRANGE
        var store = new InMemoryGoogleOAuthTokenStore();
        var tokenSet = new GoogleTokenSet(
            AccessToken: "access_token_123",
            RefreshToken: "refresh_token_456",
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1),
            Scopes: "https://www.googleapis.com/auth/gmail.readonly");

        await store.SaveTokenAsync("testuser", tokenSet, CancellationToken.None);

        // ACT
        await store.DeleteTokenAsync("testuser", CancellationToken.None);
        var retrieved = await store.GetTokenAsync("testuser", CancellationToken.None);

        // ASSERT
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task DeleteToken_Nonexistent_User_Does_Not_Throw()
    {
        // ARRANGE
        var store = new InMemoryGoogleOAuthTokenStore();

        // ACT & ASSERT (should not throw)
        await store.DeleteTokenAsync("nonexistent_user", CancellationToken.None);
    }

    [Fact]
    public async Task SaveToken_Overwrites_Existing_Token()
    {
        // ARRANGE
        var store = new InMemoryGoogleOAuthTokenStore();
        var tokenSet1 = new GoogleTokenSet(
            AccessToken: "old_access_token",
            RefreshToken: "old_refresh_token",
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1),
            Scopes: "https://www.googleapis.com/auth/gmail.readonly");

        var tokenSet2 = new GoogleTokenSet(
            AccessToken: "new_access_token",
            RefreshToken: "new_refresh_token",
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(2),
            Scopes: "https://www.googleapis.com/auth/calendar.events");

        // ACT
        await store.SaveTokenAsync("testuser", tokenSet1, CancellationToken.None);
        await store.SaveTokenAsync("testuser", tokenSet2, CancellationToken.None);
        var retrieved = await store.GetTokenAsync("testuser", CancellationToken.None);

        // ASSERT
        Assert.NotNull(retrieved);
        Assert.Equal("new_access_token", retrieved!.AccessToken);
        Assert.Equal("new_refresh_token", retrieved.RefreshToken);
        Assert.Equal("https://www.googleapis.com/auth/calendar.events", retrieved.Scopes);
    }

    [Fact]
    public async Task Multiple_UserIds_Are_Isolated()
    {
        // ARRANGE
        var store = new InMemoryGoogleOAuthTokenStore();
        var tokenSet1 = new GoogleTokenSet(
            AccessToken: "access_token_user1",
            RefreshToken: "refresh_token_user1",
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1),
            Scopes: "https://www.googleapis.com/auth/gmail.readonly");

        var tokenSet2 = new GoogleTokenSet(
            AccessToken: "access_token_user2",
            RefreshToken: "refresh_token_user2",
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1),
            Scopes: "https://www.googleapis.com/auth/calendar.events");

        // ACT
        await store.SaveTokenAsync("user1", tokenSet1, CancellationToken.None);
        await store.SaveTokenAsync("user2", tokenSet2, CancellationToken.None);

        var retrieved1 = await store.GetTokenAsync("user1", CancellationToken.None);
        var retrieved2 = await store.GetTokenAsync("user2", CancellationToken.None);

        // ASSERT
        Assert.NotNull(retrieved1);
        Assert.NotNull(retrieved2);
        Assert.Equal("access_token_user1", retrieved1!.AccessToken);
        Assert.Equal("access_token_user2", retrieved2!.AccessToken);
        Assert.Equal("refresh_token_user1", retrieved1.RefreshToken);
        Assert.Equal("refresh_token_user2", retrieved2.RefreshToken);
    }

    [Fact]
    public async Task DeleteToken_One_User_Does_Not_Affect_Other_Users()
    {
        // ARRANGE
        var store = new InMemoryGoogleOAuthTokenStore();
        var tokenSet1 = new GoogleTokenSet(
            AccessToken: "access_token_user1",
            RefreshToken: "refresh_token_user1",
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1),
            Scopes: "https://www.googleapis.com/auth/gmail.readonly");

        var tokenSet2 = new GoogleTokenSet(
            AccessToken: "access_token_user2",
            RefreshToken: "refresh_token_user2",
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1),
            Scopes: "https://www.googleapis.com/auth/calendar.events");

        await store.SaveTokenAsync("user1", tokenSet1, CancellationToken.None);
        await store.SaveTokenAsync("user2", tokenSet2, CancellationToken.None);

        // ACT
        await store.DeleteTokenAsync("user1", CancellationToken.None);

        var retrieved1 = await store.GetTokenAsync("user1", CancellationToken.None);
        var retrieved2 = await store.GetTokenAsync("user2", CancellationToken.None);

        // ASSERT
        Assert.Null(retrieved1);
        Assert.NotNull(retrieved2);
        Assert.Equal("access_token_user2", retrieved2!.AccessToken);
    }

    [Fact]
    public async Task SaveToken_Handles_Concurrent_Access()
    {
        // ARRANGE
        var store = new InMemoryGoogleOAuthTokenStore();
        var tasks = new List<Task>();

        // ACT: Simulate concurrent writes to different users
        for (int i = 0; i < 100; i++)
        {
            var userId = $"user{i}";
            var tokenSet = new GoogleTokenSet(
                AccessToken: $"access_token_{i}",
                RefreshToken: $"refresh_token_{i}",
                ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1),
                Scopes: "https://www.googleapis.com/auth/gmail.readonly");

            tasks.Add(store.SaveTokenAsync(userId, tokenSet, CancellationToken.None));
        }

        await Task.WhenAll(tasks);

        // ASSERT: All tokens should be retrievable
        for (int i = 0; i < 100; i++)
        {
            var retrieved = await store.GetTokenAsync($"user{i}", CancellationToken.None);
            Assert.NotNull(retrieved);
            Assert.Equal($"access_token_{i}", retrieved!.AccessToken);
        }
    }
}

using System.Net.Http.Json;
using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// E2E coverage for the chat auto-name-from-conversation flow.
/// Verifies the UI button, the renamed title, and persistence after reload.
/// </summary>
[Collection("AppHost")]
public sealed class ChatAutoNameTests : PlaywrightTestBase
{
    public ChatAutoNameTests(AppHostFixture fixture) : base(fixture)
    {
    }

    [Fact]
    [Trait("Category", "RequiresModel")]
    public async Task Chat_AutoNameFromConversation_UpdatesAndPersistsTitle()
    {
        Skip.IfNot(Fixture.IsToolCapableModelAvailable, Fixture.ToolCapableModelSkipReason);

        await WithScreenshotOnFailure(async () =>
        {
            using var client = Fixture.CreateGatewayHttpClient();
            client.Timeout = TimeSpan.FromMinutes(3);

            var createResponse = await client.PostAsJsonAsync("/api/sessions", new { title = "New Chat" });
            createResponse.EnsureSuccessStatusCode();

            var session = await createResponse.Content.ReadFromJsonAsync<SessionDto>();
            Assert.NotNull(session);

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?sessionId={session!.Id}", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            var sessionTitle = Page.Locator("[data-testid='current-session-title']");
            await Assertions.Expect(sessionTitle).ToHaveTextAsync("New Chat", new() { Timeout = 15_000 });

            await SendMessageAsync("Can you solve math problems?");

            var autoNameButton = Page.Locator("[data-testid='auto-name-btn']");
            await Assertions.Expect(autoNameButton).ToBeEnabledAsync(new() { Timeout = 15_000 });
            await autoNameButton.ClickAsync();

            await Assertions.Expect(sessionTitle).Not.ToHaveTextAsync("New Chat", new() { Timeout = 30_000 });
            var renamedTitle = (await sessionTitle.TextContentAsync())?.Trim();
            Assert.False(string.IsNullOrWhiteSpace(renamedTitle));
            Assert.NotEqual("New Chat", renamedTitle);

            var sessionRow = Page.Locator($"[data-testid='session-row'][data-session-id='{session.Id}']");
            await Assertions.Expect(sessionRow).ToContainTextAsync(renamedTitle, new() { Timeout = 15_000 });

            await WaitForPersistedTitleAsync(client, session.Id, renamedTitle!);

            await Page.ReloadAsync(new PageReloadOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            await Assertions.Expect(sessionTitle).ToHaveTextAsync(renamedTitle, new() { Timeout = 15_000 });
            await Assertions.Expect(sessionRow).Not.ToContainTextAsync("New Chat", new() { Timeout = 15_000 });
            await Assertions.Expect(sessionRow).ToContainTextAsync(renamedTitle, new() { Timeout = 15_000 });
        });
    }

    private async Task SendMessageAsync(string message)
    {
        var input = Page.Locator("[data-testid='chat-input']");
        var sendButton = Page.Locator("[data-testid='chat-send']");
        var assistantMessages = Page.Locator("[data-testid='assistant-message-complete']");
        var countBefore = await assistantMessages.CountAsync();

        await input.FillAsync(message);
        await sendButton.ClickAsync();

        await Assertions.Expect(assistantMessages)
            .ToHaveCountAsync(countBefore + 1, new() { Timeout = 180_000 });
    }

    private static async Task WaitForPersistedTitleAsync(HttpClient client, Guid sessionId, string expectedTitle)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            var persistedSession = await client.GetFromJsonAsync<SessionDto>($"/api/sessions/{sessionId}");
            if (persistedSession?.Title == expectedTitle)
            {
                return;
            }

            await Task.Delay(500);
        }

        var latestSession = await client.GetFromJsonAsync<SessionDto>($"/api/sessions/{sessionId}");
        Assert.NotNull(latestSession);
        Assert.Equal(expectedTitle, latestSession!.Title);
    }

    private sealed record SessionDto
    {
        public Guid Id { get; init; }
        public string Title { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }
}

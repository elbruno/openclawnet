using System.Net.Http.Json;
using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests.Scenarios;

/// <summary>
/// Scenario 1: user can ask the app to auto-generate and persist a meaningful chat title.
/// </summary>
[Collection("AppHost")]
[Trait("Category", "E2E")]
public sealed class AutoNameChatTests : PlaywrightTestBase
{
    public AutoNameChatTests(AppHostFixture fixture) : base(fixture)
    {
    }

    [SkippableFact]
    public async Task AutoNameChat_WithConversation_UpdatesAndPersistsSessionTitle()
    {
        Skip.IfNot(Fixture.IsAnyToolCapableModelAvailable, Fixture.AnyToolCapableModelSkipReason);

        await WithScreenshotOnFailure(async () =>
        {
            var session = await CreateGatewaySessionAsync();
            await OpenChatAsync(session.Id);

            var title = SessionTitle();
            await Assertions.Expect(title).ToHaveTextAsync("New Chat", new() { Timeout = 30_000 });

            await SendMessageAndWaitForAssistantAsync("I'm planning a C# API for tracking Severance-style office tasks.");
            await SendMessageAndWaitForAssistantAsync("It needs SQLite persistence and a minimal dashboard.");
            await SendMessageAndWaitForAssistantAsync("Please keep the scope focused on naming this chat.");

            var autoNameButton = Page.GetByTestId("auto-name-btn");
            await Assertions.Expect(autoNameButton).ToBeEnabledAsync(new() { Timeout = 10_000 });
            await autoNameButton.ClickAsync();

            await Assertions.Expect(title).Not.ToHaveTextAsync("New Chat", new() { Timeout = 120_000 });
            var generatedTitle = (await title.InnerTextAsync()).Trim();
            Assert.True(generatedTitle.Length >= 4, $"Expected a meaningful generated title, got '{generatedTitle}'.");

            await Page.ReloadAsync(new PageReloadOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            var reloadedTitle = SessionTitle();
            await Assertions.Expect(reloadedTitle).ToHaveTextAsync(generatedTitle, new() { Timeout = 30_000 });
        });
    }

    [Fact]
    public async Task AutoNameChat_WithZeroMessages_DisablesAutoNameButton()
    {
        await WithScreenshotOnFailure(async () =>
        {
            var session = await CreateGatewaySessionAsync();
            await OpenChatAsync(session.Id);

            await Assertions.Expect(SessionTitle()).ToHaveTextAsync("New Chat", new() { Timeout = 30_000 });
            await Assertions.Expect(Page.GetByTestId("auto-name-btn")).ToBeDisabledAsync(new() { Timeout = 10_000 });
        });
    }

    private async Task<SessionDto> CreateGatewaySessionAsync()
    {
        using var client = Fixture.CreateGatewayHttpClient();
        var response = await client.PostAsJsonAsync("/api/sessions", new { title = (string?)null });
        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        return session ?? throw new InvalidOperationException("Gateway returned an empty session response.");
    }

    private async Task OpenChatAsync(Guid sessionId)
    {
        await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?sessionId={sessionId}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 60_000
        });

        await Assertions.Expect(Page.GetByTestId("chat-input")).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }

    private ILocator SessionTitle() =>
        Page.Locator("[data-testid='session-title'], .flex-grow-1 > .d-flex.align-items-center.gap-2.mb-2 span.fw-semibold").First;

    private async Task SendMessageAndWaitForAssistantAsync(string message)
    {
        var completedTurns = await Page.GetByTestId("assistant-message-complete").CountAsync();
        var input = Page.GetByTestId("chat-input");
        await input.FillAsync(message);

        var send = Page.GetByTestId("chat-send");
        await Assertions.Expect(send).ToBeEnabledAsync(new() { Timeout = 10_000 });
        await send.ClickAsync();

        await Page.GetByTestId("assistant-message-complete")
            .Nth(completedTurns)
            .WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Attached,
                Timeout = 180_000
            });
    }

    private sealed record SessionDto(Guid Id, string Title, DateTime CreatedAt, DateTime UpdatedAt);
}

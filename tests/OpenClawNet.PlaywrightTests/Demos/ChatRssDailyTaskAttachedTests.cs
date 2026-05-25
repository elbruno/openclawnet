using System.Net.Http.Json;
using Microsoft.Playwright;
using Xunit;

namespace OpenClawNet.PlaywrightTests.Demos;

[Trait("Category", "DemoLive")]
[Trait("Category", "E2E")]
[Trait("Category", "Chat")]
public sealed class ChatRssDailyTaskAttachedTests : AttachedAspireTestBase
{
    private const string FirstPrompt =
        "check the rss for the latest episodes of https:www.notienenombre.com,and create a summary with the latest 5";

    private const string SecondPrompt =
        "now create a daily task that runs at 9AM every day, runs the same RSS summary action, and save the results in the default storage location using the chat name";

    [SkippableFact]
    public async Task Chat_RssSummary_Rename_AndCreateDailyJobDefinition_Attached()
    {
        await WithScreenshotOnFailure(async () =>
        {
            using var client = CreateGatewayHttpClient();
            client.Timeout = TimeSpan.FromMinutes(5);

            await Page.GotoAsync($"{WebBaseUrl}/chat", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            await StartNewChatAsync();
            await SendPromptAndWaitForAssistantAsync(FirstPrompt);

            var latestAssistantMessage = await GetLatestAssistantMessageTextAsync();
            Assert.False(string.IsNullOrWhiteSpace(latestAssistantMessage));
            Assert.True(
                latestAssistantMessage.Contains("summary", StringComparison.OrdinalIgnoreCase) ||
                latestAssistantMessage.Contains("episode", StringComparison.OrdinalIgnoreCase) ||
                latestAssistantMessage.Contains("rss", StringComparison.OrdinalIgnoreCase),
                $"Expected RSS summary-like output. Got: {latestAssistantMessage}");

            var uniqueChatTitle = $"rss-notienenombre-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
            await RenameCurrentChatAsync(uniqueChatTitle);
            await WaitForPersistedTitleAsync(client, uniqueChatTitle);

            var jobsBefore = await client.GetFromJsonAsync<List<JobDto>>("/api/jobs") ?? [];
            var existingIds = jobsBefore.Select(j => j.Id).ToHashSet();

            await SendPromptAndWaitForAssistantAsync(SecondPrompt);
            var taskAssistantMessage = await GetLatestAssistantMessageTextAsync();
            Assert.False(string.IsNullOrWhiteSpace(taskAssistantMessage));

            var createdJob = await WaitForMatchingJobAsync(
                client,
                existingIds,
                uniqueChatTitle,
                timeout: TimeSpan.FromSeconds(120));

            Assert.NotNull(createdJob);
            Assert.True(createdJob!.IsRecurring, "Expected a recurring daily job definition.");
            Assert.Equal("0 9 * * *", createdJob.CronExpression);
            Assert.Contains("notienenombre", createdJob.Prompt, StringComparison.OrdinalIgnoreCase);
            Assert.True(
                createdJob.Name.Contains(uniqueChatTitle, StringComparison.OrdinalIgnoreCase) ||
                createdJob.Prompt.Contains(uniqueChatTitle, StringComparison.OrdinalIgnoreCase),
                "Expected created job to reference the unique chat title.");
        });
    }

    private async Task StartNewChatAsync()
    {
        var newChatButton = Page.GetByRole(AriaRole.Button, new() { Name = "+ New Chat" });
        await Assertions.Expect(newChatButton).ToBeEnabledAsync(new() { Timeout = 10_000 });
        await newChatButton.ClickAsync();

        var input = Page.GetByTestId("chat-input");
        await Assertions.Expect(input).ToBeEmptyAsync(new() { Timeout = 10_000 });
    }

    private async Task SendPromptAndWaitForAssistantAsync(string prompt)
    {
        var completeEvents = Page.Locator("[data-testid='assistant-message-complete']");
        var completedBefore = await completeEvents.CountAsync();

        await Page.GetByTestId("chat-input").FillAsync(prompt);
        await Page.GetByTestId("chat-send").ClickAsync();

        await Assertions.Expect(completeEvents)
            .ToHaveCountAsync(completedBefore + 1, new() { Timeout = 180_000 });
    }

    private async Task RenameCurrentChatAsync(string title)
    {
        await Page.Locator("button[title='Rename session']").ClickAsync();

        var renameInput = Page.Locator("input.form-control.form-control-sm").First;
        await Assertions.Expect(renameInput).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await renameInput.FillAsync(title);
        await renameInput.PressAsync("Enter");

        await Assertions.Expect(Page.Locator("[data-testid='current-session-title']"))
            .ToHaveTextAsync(title, new() { Timeout = 15_000 });
    }

    private async Task<string> GetLatestAssistantMessageTextAsync()
    {
        var locator = Page.Locator("[data-testid='assistant-message'][data-role='assistant']").Last;
        await locator.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        return (await locator.InnerTextAsync()).Trim();
    }

    private static async Task WaitForPersistedTitleAsync(HttpClient client, string expectedTitle)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var sessions = await client.GetFromJsonAsync<List<SessionDto>>("/api/sessions") ?? [];
            if (sessions.Any(session => string.Equals(session.Title, expectedTitle, StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Delay(500);
        }

        var persistedSessions = await client.GetFromJsonAsync<List<SessionDto>>("/api/sessions") ?? [];
        Assert.Contains(persistedSessions, session => string.Equals(session.Title, expectedTitle, StringComparison.Ordinal));
    }

    private static async Task<JobDto?> WaitForMatchingJobAsync(
        HttpClient client,
        HashSet<Guid> existingIds,
        string expectedChatTitle,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            var jobs = await client.GetFromJsonAsync<List<JobDto>>("/api/jobs") ?? [];
            var candidate = jobs
                .Where(j => !existingIds.Contains(j.Id))
                .FirstOrDefault(j =>
                    j.IsRecurring &&
                    string.Equals(j.CronExpression, "0 9 * * *", StringComparison.Ordinal) &&
                    (j.Name.Contains(expectedChatTitle, StringComparison.OrdinalIgnoreCase) ||
                     j.Prompt.Contains(expectedChatTitle, StringComparison.OrdinalIgnoreCase)));

            if (candidate is not null)
            {
                return candidate;
            }

            await Task.Delay(1_000);
        }

        return null;
    }

    private sealed record JobDto
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Prompt { get; init; } = string.Empty;
        public bool IsRecurring { get; init; }
        public string? CronExpression { get; init; }
    }

    private sealed record SessionDto
    {
        public Guid Id { get; init; }
        public string Title { get; init; } = string.Empty;
    }
}

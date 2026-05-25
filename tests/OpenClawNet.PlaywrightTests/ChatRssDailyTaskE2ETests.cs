using System.Net.Http.Json;
using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests;

[Collection("AspireHost")]
[Trait("Category", "E2E")]
public sealed class ChatRssDailyTaskE2ETests : AspireHostPlaywrightTestBase
{
    private const string FirstPrompt =
        "check the rss for the latest episodes of https:www.notienenombre.com,and create a summary with the latest 5";

    private const string SecondPrompt =
        "now create a daily task that runs at 9AM every day, runs the same RSS summary action, and save the results in the default storage location using the chat name";

    public ChatRssDailyTaskE2ETests(AspireHostFixture fixture) : base(fixture)
    {
    }

    private sealed record AgentProfileDraft(
        string Name,
        string Provider,
        string Model,
        string Instructions,
        bool RequireToolApproval);

    [SkippableFact]
    [Trait("Category", "RequiresModel")]
    public async Task Chat_RssSummary_Rename_AndCreateDailyJobDefinition()
    {
        Skip.IfNot(Fixture.IsAnyToolCapableModelAvailable, Fixture.AnyToolCapableModelSkipReason);

        await WithScreenshotOnFailure(async () =>
        {
            var instructions =
                "Use the schedule tool for recurring-job requests and web.fetch for RSS/web retrieval tasks. " +
                "When asked to create a daily task at 9AM, use cron expression '0 9 * * *'. " +
                "When saving results, use the chat name and the default storage location.";

            var profileName = Fixture.IsAzureOpenAIAvailable
                ? await CreateProfileAsync(new AgentProfileDraft(
                    Name: $"rss-daily-task-{Guid.NewGuid():N}".ToLowerInvariant(),
                    Provider: await CreateAzureProviderAsync($"azure-rss-daily-{Guid.NewGuid():N}"),
                    Model: Fixture.AzureOpenAIDeployment!,
                    Instructions: instructions,
                    RequireToolApproval: false))
                : await CreateProfileAsync(new AgentProfileDraft(
                    Name: $"rss-daily-task-{Guid.NewGuid():N}".ToLowerInvariant(),
                    Provider: "ollama",
                    Model: AspireHostFixture.ToolCapableTestModel,
                    Instructions: instructions,
                    RequireToolApproval: false));

            await LogStepAsync("Opening a new chat session");
            using var client = Fixture.CreateGatewayHttpClient();
            client.Timeout = TimeSpan.FromMinutes(5);

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            await StartNewChatAsync();
            await LogStepAsync("Sending the RSS summary prompt");
            await SendPromptAndWaitForAssistantAsync(FirstPrompt);

            var latestAssistantMessage = await GetLatestAssistantMessageTextAsync();
            Assert.False(string.IsNullOrWhiteSpace(latestAssistantMessage));
            Assert.True(
                latestAssistantMessage.Contains("summary", StringComparison.OrdinalIgnoreCase) ||
                latestAssistantMessage.Contains("episode", StringComparison.OrdinalIgnoreCase) ||
                latestAssistantMessage.Contains("rss", StringComparison.OrdinalIgnoreCase),
                $"Expected the assistant to present a summary result. Got: {latestAssistantMessage}");

            var uniqueChatTitle = $"rss-notienenombre-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
            await LogStepAsync($"Renaming chat to {uniqueChatTitle}");
            await RenameCurrentChatAsync(uniqueChatTitle);
            await WaitForPersistedTitleAsync(client, uniqueChatTitle);

            var jobsBefore = await client.GetFromJsonAsync<List<JobDto>>("/api/jobs") ?? [];
            var existingIds = jobsBefore.Select(j => j.Id).ToHashSet();

            await LogStepAsync("Sending the recurring-task prompt");
            await SendPromptAndWaitForAssistantAsync(SecondPrompt);
            var taskAssistantMessage = await GetLatestAssistantMessageTextAsync();
            Assert.False(string.IsNullOrWhiteSpace(taskAssistantMessage));
            Assert.True(
                taskAssistantMessage.Contains("schedule", StringComparison.OrdinalIgnoreCase) ||
                taskAssistantMessage.Contains("daily", StringComparison.OrdinalIgnoreCase) ||
                taskAssistantMessage.Contains("created", StringComparison.OrdinalIgnoreCase) ||
                taskAssistantMessage.Contains("0 9 * * *", StringComparison.OrdinalIgnoreCase),
                $"Expected the assistant to present the created task result. Got: {taskAssistantMessage}");

            var createdJob = await WaitForMatchingJobAsync(
                client,
                existingIds,
                uniqueChatTitle,
                timeout: TimeSpan.FromSeconds(90));

            Assert.NotNull(createdJob);
            Assert.True(createdJob!.IsRecurring, "Expected a recurring daily job definition.");
            Assert.Equal("0 9 * * *", createdJob.CronExpression);
            Assert.Contains("notienenombre", createdJob.Prompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("default storage location", createdJob.Prompt, StringComparison.OrdinalIgnoreCase);
            Assert.True(
                createdJob.Name.Contains(uniqueChatTitle, StringComparison.OrdinalIgnoreCase) ||
                createdJob.Prompt.Contains(uniqueChatTitle, StringComparison.OrdinalIgnoreCase),
                "Expected the created job to reference the unique chat title for output naming.");
        });
    }

    private async Task<string> CreateProfileAsync(AgentProfileDraft draft)
    {
        using var http = Fixture.CreateGatewayHttpClient();
        var body = new
        {
            DisplayName = draft.Name,
            draft.Provider,
            draft.Model,
            draft.Instructions,
            EnabledTools = (string[]?)null,
            Temperature = (double?)null,
            MaxTokens = (int?)null,
            IsDefault = false,
            draft.RequireToolApproval
        };

        var response = await http.PutAsJsonAsync($"/api/agent-profiles/{Uri.EscapeDataString(draft.Name)}", body);
        response.EnsureSuccessStatusCode();
        return draft.Name;
    }

    private async Task<string> CreateAzureProviderAsync(string providerName)
    {
        using var http = Fixture.CreateGatewayHttpClient();
        var response = await http.PutAsJsonAsync($"/api/model-providers/{providerName}", new
        {
            providerType = "azure-openai",
            displayName = "Azure OpenAI (Chat RSS E2E)",
            endpoint = Fixture.AzureOpenAIEndpoint,
            model = Fixture.AzureOpenAIDeployment,
            apiKey = Fixture.AzureOpenAIApiKey,
            deploymentName = Fixture.AzureOpenAIDeployment,
            authMode = "api-key",
            isSupported = true
        });

        response.EnsureSuccessStatusCode();
        return providerName;
    }

    private async Task StartNewChatAsync()
    {
        var newChatBtn = Page.GetByRole(AriaRole.Button, new() { Name = "+ New Chat" });
        await Assertions.Expect(newChatBtn).ToBeEnabledAsync(new() { Timeout = 10_000 });
        await newChatBtn.ClickAsync();

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

            await Task.Delay(1000);
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

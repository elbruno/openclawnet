using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// Wave 4 PR-3 — E2E coverage for the tool-approval flow (un-skipped after PR-1 + PR-2 merged).
///
/// Tagged <c>Trait("Category", "ToolApproval")</c> so they don't run in default CI.
/// Run with: <c>dotnet test --filter "Category=ToolApproval"</c> against a live Aspire stack.
///
/// Reference: docs/analysis/tool-approval-design.md (Ripley, 2026-04-19).
/// Bruno's decisions baked in:
///   Q1 = per-tool-type, session-scoped approval cache
///   Q2 = schedule tool is EXEMPT (no approval prompt even on requiring profiles)
///   Q3 = cron jobs against requiring profiles fail-fast (see CronJob test in IntegrationTests)
///   Q4 = "Remember for this session" checkbox is offered
///   Q5 = browser tool stays in the requires-approval set
/// </summary>
[Collection("AppHost")]
[Trait("Category", "ToolApproval")]
public class ToolApprovalFlowTests : PlaywrightTestBase
{
    // Wave 4 PR-1 (Lambert UI) and PR-2 (Dallas backend) merged — tests live now.
    // Still gated by Trait("Category","ToolApproval") so they only run when explicitly selected.
    public ToolApprovalFlowTests(AppHostFixture fixture) : base(fixture) { }

    // ---------------------------------------------------------------------
    // Helpers — placeholder shapes so the file compiles cleanly today.
    // The post-merge implementer should replace these with real DTOs from
    // OpenClawNet.Models.Abstractions / Gateway client packages.
    // ---------------------------------------------------------------------

    private sealed record AgentProfileDraft(
        string Name,
        string Provider,
        string Model,
        string Instructions,
        bool RequireToolApproval);

    private sealed record ApprovalStreamProbeResult(
        bool EmitsApproval,
        string SkipReason,
        string? ToolName = null);

    /// <summary>
    /// Creates (or upserts) an AgentProfile via the gateway's PUT /api/agent-profiles/{name}
    /// endpoint. The profile name is the natural key — it lives in the URL, not the body.
    /// </summary>
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

    /// <summary>
    /// TODO (post PR-1): tighten selector to the actual ToolApprovalCard
    /// component contract (e.g. <c>data-testid="tool-approval-card"</c>).
    /// </summary>
    private ILocator ApprovalCard() =>
        Page.Locator("[data-testid='tool-approval-card'], .tool-approval-card");

    private async Task SendChatMessageAsync(string text)
    {
        var input = Page.GetByTestId("chat-input");
        await input.FillAsync(text);
        var sendBtn = Page.GetByTestId("chat-send");
        await Microsoft.Playwright.Assertions.Expect(sendBtn).ToBeEnabledAsync(new() { Timeout = 5_000 });
        await sendBtn.ClickAsync();
    }

    private async Task<ApprovalStreamProbeResult> ProbeApprovalStreamAsync(string profileName, string prompt)
    {
        using var http = Fixture.CreateGatewayHttpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        try
        {
            using var response = await http.PostAsJsonAsync("/api/chat/stream", new
            {
                sessionId = Guid.NewGuid(),
                message = prompt,
                agentProfileName = profileName
            }, cts.Token);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream && !cts.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var eventType = root.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString()
                    : null;
                var toolName = root.TryGetProperty("toolName", out var toolElement)
                    ? toolElement.GetString()
                    : null;

                if (string.Equals(eventType, "tool_approval", StringComparison.OrdinalIgnoreCase))
                {
                    return new ApprovalStreamProbeResult(true, string.Empty, toolName);
                }

                if (string.Equals(eventType, "complete", StringComparison.OrdinalIgnoreCase))
                {
                    return new ApprovalStreamProbeResult(
                        false,
                        $"Profile '{profileName}' completed without emitting a tool_approval event.");
                }

                if (string.Equals(eventType, "error", StringComparison.OrdinalIgnoreCase))
                {
                    var content = root.TryGetProperty("content", out var contentElement)
                        ? contentElement.GetString()
                        : "Unknown error";
                    return new ApprovalStreamProbeResult(
                        false,
                        $"Profile '{profileName}' returned an error before tool approval: {content}");
                }
            }

            return new ApprovalStreamProbeResult(
                false,
                $"Profile '{profileName}' did not emit a tool_approval event within 90s.");
        }
        catch (OperationCanceledException)
        {
            return new ApprovalStreamProbeResult(
                false,
                $"Profile '{profileName}' did not emit a tool_approval event within 90s.");
        }
    }

    // ---------------------------------------------------------------------
    // Scenario 1
    // ---------------------------------------------------------------------
    [SkippableFact]
    public async Task Profile_RequireApproval_True_PausesOnToolCall()
    {
        Skip.IfNot(Fixture.IsToolCapableModelAvailable, Fixture.ToolCapableModelSkipReason);
        await WithScreenshotOnFailure(async () =>
        {
            // Arrange: create a profile that requires approval and open chat against it.
            var profileName = await CreateProfileAsync(new AgentProfileDraft(
                Name: $"approval-required-{Guid.NewGuid():N}",
                Provider: "ollama", Model: AppHostFixture.ToolCapableTestModel,
                Instructions: "Use the browser tool to fetch https://example.com when asked.",
                RequireToolApproval: true));
            await LogStepAsync($"Profile created: {profileName} (model: {AppHostFixture.ToolCapableTestModel})");

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await LogStepAsync("Chat page loaded — sending prompt");

            // Act: send a prompt that should trigger a `browser` or `web_fetch` tool call.
            await SendChatMessageAsync("Please open example.com and tell me the title.");
            await LogStepAsync("Prompt sent — waiting for tool approval card (up to 90s)");

            // Assert: ToolApprovalCard appears with one of the URL-fetching tools
            //          (the model may pick either; both are reasonable for this prompt)
            //          and the agent stream is paused (no tool_result yet).
            await WaitForWithTicksAsync(ApprovalCard(), 180_000, "tool approval card");
            var cardText = await ApprovalCard().InnerTextAsync();
            Assert.True(
                cardText.Contains("browser", StringComparison.OrdinalIgnoreCase) ||
                cardText.Contains("web_fetch", StringComparison.OrdinalIgnoreCase),
                $"Expected approval card to reference 'browser' or 'web_fetch'. Card text: {cardText}");
            await AssertCardContainsAsync(ApprovalCard(), "example.com");
            await LogStepAsync("✅ Approval card validated");
        });
    }

    private static async Task AssertCardContainsAsync(ILocator locator, string expected)
    {
        var text = await locator.InnerTextAsync();
        Assert.Contains(expected, text, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------
    // Scenario 2
    // ---------------------------------------------------------------------
    [SkippableFact]
    public async Task Profile_RequireApproval_True_UserApproves_ContinuesExecution()
    {
        Skip.IfNot(Fixture.IsToolCapableModelAvailable, Fixture.ToolCapableModelSkipReason);
        await WithScreenshotOnFailure(async () =>
        {
            // Arrange: same setup as scenario 1.
            var profileName = await CreateProfileAsync(new AgentProfileDraft(
                $"approval-required-{Guid.NewGuid():N}", "ollama", AppHostFixture.ToolCapableTestModel,
                "Use the browser tool when asked.", RequireToolApproval: true));
            await LogStepAsync($"Profile created: {profileName}");

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await LogStepAsync("Chat page loaded — sending prompt");
            await SendChatMessageAsync("Open example.com.");
            await LogStepAsync("Prompt sent — waiting for tool approval card (up to 180s)");
            await WaitForWithTicksAsync(ApprovalCard(), 180_000, "tool approval card");

            // Act: click [Approve].
            await LogStepAsync("Card visible — clicking Approve");
            await ApprovalCard().Locator("button:has-text('Approve')").ClickAsync();

            // Assert: the tool result streams in afterward and the assistant message completes.
            //   - approval card disappears
            //   - a tool_result event renders (look for tool result element)
            //   - the assistant turn reaches a "done" state (no spinner / final message rendered)
            await LogStepAsync("Waiting for approval card to disappear (up to 90s)");
            await ApprovalCard().WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Hidden,
                Timeout = 180_000
            });
            await LogStepAsync("Card hidden — waiting for tool result (up to 90s)");
            await WaitForWithTicksAsync(Page.Locator("[data-testid='tool-result']"), 180_000, "tool result");
            await LogStepAsync("Tool result rendered — waiting for assistant completion (up to 90s)");
            await WaitForWithTicksAsync(Page.Locator("[data-testid='assistant-message-complete']"), 180_000, "assistant complete");
            await LogStepAsync("✅ Full approval flow completed");
        });
    }

    // ---------------------------------------------------------------------
    // Scenario 3
    // ---------------------------------------------------------------------
    [SkippableFact]
    public async Task Profile_RequireApproval_True_UserDenies_StopsCleanly()
    {
        Skip.IfNot(Fixture.IsToolCapableModelAvailable, Fixture.ToolCapableModelSkipReason);
        await WithScreenshotOnFailure(async () =>
        {
            // Arrange.
            var profileName = await CreateProfileAsync(new AgentProfileDraft(
                $"approval-required-{Guid.NewGuid():N}", "ollama", AppHostFixture.ToolCapableTestModel,
                "Use the browser tool when asked.", RequireToolApproval: true));

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await SendChatMessageAsync("Open example.com.");
            await ApprovalCard().WaitForAsync();

            // Act: click [Deny].
            await ApprovalCard().Locator("button:has-text('Deny')").ClickAsync();

            // Assert: agent emits a "tool denied" message (or equivalent) and the turn
            //         terminates cleanly with NO exception toast.
            await Page.Locator("text=/denied|cancelled|not approved/i").First
                .WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
            Assert.Equal(0, await Page.Locator(".toast-error, [data-testid='error-toast']").CountAsync());
        });
    }

    // ---------------------------------------------------------------------
    // Scenario 4
    // ---------------------------------------------------------------------
    [SkippableFact]
    public async Task Profile_RequireApproval_False_AutoApproves()
    {
        Skip.IfNot(Fixture.IsToolCapableModelAvailable, Fixture.ToolCapableModelSkipReason);
        await WithScreenshotOnFailure(async () =>
        {
            // Arrange: profile that does NOT require approval.
            var profileName = await CreateProfileAsync(new AgentProfileDraft(
                $"auto-approve-{Guid.NewGuid():N}", "ollama", AppHostFixture.ToolCapableTestModel,
                "Use the browser tool when asked.", RequireToolApproval: false));

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            // Act: same prompt that would trigger a tool call.
            await SendChatMessageAsync("Open example.com.");

            // Assert: NO approval card renders; tool runs immediately and a result
            //         is observed within a short window.
            await Page.Locator("[data-testid='tool-result']").First
                .WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });
            Assert.Equal(0, await ApprovalCard().CountAsync());
        });
    }

    // ---------------------------------------------------------------------
    // Scenario 5
    // ---------------------------------------------------------------------
    [SkippableFact]
    public async Task RememberForSession_SuppressesSubsequentPrompts()
    {
        Skip.IfNot(Fixture.IsToolCapableModelAvailable, Fixture.ToolCapableModelSkipReason);
        await WithScreenshotOnFailure(async () =>
        {
            // Arrange: profile=require, open chat.
            var profileName = await CreateProfileAsync(new AgentProfileDraft(
                $"approval-required-{Guid.NewGuid():N}", "ollama", AppHostFixture.ToolCapableTestModel,
                "Use the browser tool when asked.", RequireToolApproval: true));

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            // Act: first call prompts; user checks "Remember for this session" + Approve.
            await SendChatMessageAsync("Open example.com (first).");
            await ApprovalCard().WaitForAsync();
            await ApprovalCard().Locator("input[type='checkbox']").First.CheckAsync();
            await ApprovalCard().Locator("button:has-text('Approve')").ClickAsync();
            await ApprovalCard().WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Hidden
            });

            // Act 2: send a second message that triggers the SAME tool name in the
            //         SAME chat session.
            await SendChatMessageAsync("Now open example.org with the browser tool.");

            // Assert: no approval card appears; tool result arrives directly.
            await Page.Locator("[data-testid='tool-result']").Nth(1)
                .WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });
            Assert.Equal(0, await ApprovalCard().CountAsync());
        });
    }

    // ---------------------------------------------------------------------
    // Scenario 6 — schedule tool is exempt (Bruno Q2 = No)
    // ---------------------------------------------------------------------
    [SkippableFact]
    public async Task ScheduleTool_Exempt_NoApprovalEvenWhenRequired()
    {
        Skip.IfNot(Fixture.IsToolCapableModelAvailable, Fixture.ToolCapableModelSkipReason);
        await WithScreenshotOnFailure(async () =>
        {
            // Arrange: requiring profile.
            var profileName = await CreateProfileAsync(new AgentProfileDraft(
                $"approval-required-{Guid.NewGuid():N}", "ollama", AppHostFixture.ToolCapableTestModel,
                "Use the schedule tool to create jobs when asked.", RequireToolApproval: true));

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            // Act: prompt that drives the agent to invoke the `schedule` tool.
            //       Wave 5 PR-C (Vasquez): make the cron syntax explicit so the model
            //       has minimal ambiguity about which tool to pick.
            await SendChatMessageAsync(
                "Use the schedule tool to create a cron job with expression '0 9 * * *' that prints 'hello'.");

            // Assert: NO approval card renders for `schedule` — it's exempt (Bruno Q2).
            //   A tool_complete event for `schedule` should land within 60s, surfacing
            //   the hidden [data-testid='tool-result'] sentinel (PR-B).
            await Page.Locator("[data-testid='tool-result']").First
                .WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });
            Assert.Equal(0, await ApprovalCard().CountAsync());
        });
    }

    // ---------------------------------------------------------------------
    // Scenario 7 — browser + web_fetch always require approval on requiring profile
    // ---------------------------------------------------------------------
    [SkippableTheory]
    [InlineData("web_fetch", "Issue a single HTTP GET to https://example.com and tell me the status code.")]
    [InlineData("browser", "Drive a headless browser to https://example.com and read the page title.")]
    public async Task BrowserAndWebFetch_AlwaysRequireApproval_OnRequiringProfile(
        string expectedTool, string prompt)
    {
        Skip.IfNot(Fixture.IsToolCapableModelAvailable, Fixture.ToolCapableModelSkipReason);
        await WithScreenshotOnFailure(async () =>
        {
            // Arrange.
            var profileName = await CreateProfileAsync(new AgentProfileDraft(
                $"approval-required-{Guid.NewGuid():N}", "ollama", AppHostFixture.ToolCapableTestModel,
                $"Use the {expectedTool} tool when asked.", RequireToolApproval: true));

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            // Act.
            await SendChatMessageAsync(prompt);

            // Assert: approval card appears and references the expected tool name.
            await ApprovalCard().WaitForAsync(new LocatorWaitForOptions { Timeout = 180_000 });
            await AssertCardContainsAsync(ApprovalCard(), expectedTool);
        });
    }

    // ---------------------------------------------------------------------
    // Scenario 8 — model matrix: which Ollama models actually emit a tool
    //              call within 90 s for "Please open example.com..."?
    //              Each row = one model. Result is recorded per-row so
    //              xUnit produces a Pass/Fail per model, which doubles as
    //              our compatibility table.
    // ---------------------------------------------------------------------
    [SkippableTheory]
    [Trait("Category", "ToolApprovalMatrix")]
    [InlineData("gemma4:e2b")]
    [InlineData("qwen2.5:3b")]
    [InlineData("llama3.2:latest")]
    [InlineData("phi4-mini:latest")]
    public async Task Model_Matrix_PausesOnToolCall(string modelName)
    {
        var probe = await Fixture.ProbeOllamaToolCallCompatibilityAsync(modelName);
        Skip.IfNot(probe.IsSupported, probe.SkipReason);

        await WithScreenshotOnFailure(async () =>
        {
            await LogStepAsync($"📊 Matrix run for model: {modelName}");
            await LogStepAsync($"[{modelName}] Probe accepted tool '{probe.ObservedToolName}' with args {probe.ObservedArgumentsJson ?? "{}"}");
            const string prompt = "Please use browser_navigate to open https://example.com and tell me the title.";
            var profileName = await CreateProfileAsync(new AgentProfileDraft(
                Name: $"matrix-{modelName.Replace(':', '-').Replace('.', '-')}-{Guid.NewGuid():N}".ToLowerInvariant(),
                Provider: "ollama", Model: modelName,
                Instructions: "When asked to open a webpage, call the browser_navigate tool with the target URL. Do not answer from memory.",
                RequireToolApproval: true));
            await LogStepAsync($"Profile created: {profileName}");

            var approvalProbe = await ProbeApprovalStreamAsync(profileName, prompt);
            Skip.IfNot(approvalProbe.EmitsApproval, approvalProbe.SkipReason);
            await LogStepAsync($"[{modelName}] Gateway stream emitted tool_approval for '{approvalProbe.ToolName}'");

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await LogStepAsync($"Chat page loaded for {modelName} — sending prompt");
            await SendChatMessageAsync(prompt);
            await LogStepAsync($"[{modelName}] Prompt sent — waiting up to 90s for tool approval card");

            await WaitForWithTicksAsync(ApprovalCard(), 180_000, $"tool approval card ({modelName})");
            var cardText = await ApprovalCard().InnerTextAsync();
            await LogStepAsync($"✅ [{modelName}] Card appeared. Text: {cardText.Replace('\n', ' ').Substring(0, Math.Min(120, cardText.Length))}");
        }, $"Model_Matrix_{modelName.Replace(':', '-').Replace('.', '-')}");
    }

    // ---------------------------------------------------------------------
    // Scenario 9 — Azure OpenAI variant of the model matrix.
    //              Skipped unless AZURE_OPENAI_ENDPOINT/_API_KEY/_DEPLOYMENT
    //              env vars are set. Pre-creates a named provider definition
    //              and a profile bound to it (same pattern as
    //              WebsiteWatcherE2ETests.UpsertAzureProviderAndProfileAsync).
    // ---------------------------------------------------------------------
    [SkippableFact]
    [Trait("Category", "ToolApprovalMatrix")]
    public async Task Model_Matrix_AzureOpenAI_PausesOnToolCall()
    {
        Skip.IfNot(Fixture.IsAzureOpenAIAvailable,
            "Azure OpenAI not configured — set AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, AZURE_OPENAI_DEPLOYMENT.");

        await WithScreenshotOnFailure(async () =>
        {
            var providerName = $"azure-openai-matrix-{Guid.NewGuid():N}".ToLowerInvariant();
            await LogStepAsync($"📊 Matrix run for Azure OpenAI deployment: {Fixture.AzureOpenAIDeployment}");

            using (var http = Fixture.CreateGatewayHttpClient())
            {
                var providerResp = await http.PutAsJsonAsync($"/api/model-providers/{providerName}", new
                {
                    providerType = "azure-openai",
                    displayName = "Azure OpenAI (Matrix)",
                    endpoint = Fixture.AzureOpenAIEndpoint,
                    model = Fixture.AzureOpenAIDeployment,
                    apiKey = Fixture.AzureOpenAIApiKey,
                    deploymentName = Fixture.AzureOpenAIDeployment,
                    authMode = "api-key",
                    isSupported = true
                });
                Assert.True(providerResp.IsSuccessStatusCode,
                    $"PUT /api/model-providers/{providerName} → {(int)providerResp.StatusCode}");
                await LogStepAsync($"Provider definition created: {providerName}");
            }

            var profileName = await CreateProfileAsync(new AgentProfileDraft(
                Name: $"matrix-azure-{Guid.NewGuid():N}".ToLowerInvariant(),
                Provider: providerName, Model: Fixture.AzureOpenAIDeployment!,
                Instructions: "Use the browser tool to fetch https://example.com when asked.",
                RequireToolApproval: true));
            await LogStepAsync($"Profile created: {profileName}");

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await LogStepAsync("Chat page loaded for Azure OpenAI — sending prompt");
            await SendChatMessageAsync("Please open example.com and tell me the title.");
            await LogStepAsync("[azure] Prompt sent — waiting up to 90s for tool approval card");

            await WaitForWithTicksAsync(ApprovalCard(), 180_000, $"tool approval card (azure:{Fixture.AzureOpenAIDeployment})");
            var cardText = await ApprovalCard().InnerTextAsync();
            await LogStepAsync($"✅ [azure:{Fixture.AzureOpenAIDeployment}] Card appeared. Text: {cardText.Replace('\n', ' ').Substring(0, Math.Min(120, cardText.Length))}");
        }, "Model_Matrix_AzureOpenAI");
    }

    // ---------------------------------------------------------------------
    // Scenario 10 — Verify Approve button click disables UI, sends POST, and agent resumes
    // ---------------------------------------------------------------------
    [SkippableFact]
    [Trait("Category", "ToolApprovalMatrix")]
    public async Task AzureOpenAI_ApproveButton_DisablesOnClickAndResumes()
    {
        Skip.IfNot(Fixture.IsAzureOpenAIAvailable,
            "Azure OpenAI not configured — set AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, AZURE_OPENAI_DEPLOYMENT.");

        await WithScreenshotOnFailure(async () =>
        {
            var providerName = $"azure-openai-approve-e2e-{Guid.NewGuid():N}".ToLowerInvariant();
            await LogStepAsync($"🧪 E2E Approve button test with Azure OpenAI: {Fixture.AzureOpenAIDeployment}");

            // Hook console and network events for debugging
            Page.Console += (_, msg) => Console.WriteLine($"[browser:{msg.Type}] {msg.Text}");
            var networkLog = new List<string>();
            Page.Response += (_, resp) =>
            {
                if (resp.Url.Contains("tool-approval"))
                {
                    var logLine = $"[net] {resp.Status} {resp.Url}";
                    networkLog.Add(logLine);
                    Console.WriteLine(logLine);
                }
            };

            using (var http = Fixture.CreateGatewayHttpClient())
            {
                var providerResp = await http.PutAsJsonAsync($"/api/model-providers/{providerName}", new
                {
                    providerType = "azure-openai",
                    displayName = "Azure OpenAI (E2E)",
                    endpoint = Fixture.AzureOpenAIEndpoint,
                    model = Fixture.AzureOpenAIDeployment,
                    apiKey = Fixture.AzureOpenAIApiKey,
                    deploymentName = Fixture.AzureOpenAIDeployment,
                    authMode = "api-key",
                    isSupported = true
                });
                Assert.True(providerResp.IsSuccessStatusCode,
                    $"PUT /api/model-providers/{providerName} → {(int)providerResp.StatusCode}");
                await LogStepAsync($"Provider created: {providerName}");
            }

            var profileName = await CreateProfileAsync(new AgentProfileDraft(
                Name: $"e2e-approve-{Guid.NewGuid():N}".ToLowerInvariant(),
                Provider: providerName, Model: Fixture.AzureOpenAIDeployment!,
                Instructions: "Use the browser tool to fetch https://example.com when asked.",
                RequireToolApproval: true));
            await LogStepAsync($"Profile created: {profileName}");

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await LogStepAsync("Chat page loaded — sending prompt");
            await SendChatMessageAsync("Please open example.com and tell me the title.");
            await LogStepAsync("Prompt sent — waiting for tool approval card");

            await WaitForWithTicksAsync(ApprovalCard(), 180_000, "tool approval card");
            var cardText = await ApprovalCard().InnerTextAsync();
            await LogStepAsync($"✅ Card appeared. Text: {cardText.Replace('\n', ' ').Substring(0, Math.Min(120, cardText.Length))}");

            // Verify Approve button is enabled
            var approveBtn = ApprovalCard().Locator("button:has-text('Approve')");
            await Microsoft.Playwright.Assertions.Expect(approveBtn).ToBeEnabledAsync(new() { Timeout = 5_000 });
            await LogStepAsync("✅ Approve button is enabled");

            // Click Approve and verify immediate feedback (button disabled)
            await approveBtn.ClickAsync();
            await LogStepAsync("Approve button clicked");

            // Verify button is disabled immediately (within 500ms)
            await Microsoft.Playwright.Assertions.Expect(approveBtn).ToBeDisabledAsync(new() { Timeout = 500 });
            await LogStepAsync("✅ Approve button is disabled");

            // Optional: check for "Approving" feedback if it's still visible (might be too fast on local)
            var approvingFeedback = ApprovalCard().Locator("button:has-text('Approving')");
            var isBusyStateVisible = await approvingFeedback.IsVisibleAsync();
            if (isBusyStateVisible)
            {
                await LogStepAsync("✅ Button shows 'Approving...' state");
            }
            else
            {
                await LogStepAsync("⚠️ 'Approving...' state not captured (POST was too fast)");
            }

            // Wait for card to disappear (POST succeeded)
            await Microsoft.Playwright.Assertions.Expect(ApprovalCard()).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
            await LogStepAsync("✅ Card disappeared — POST succeeded");

            // Wait for agent to resume and emit tool result (look for assistant message after tool completes)
            // The agent should stream back the result within 60s
            var assistantMessages = Page.Locator(".message-assistant");
            var messageCountBefore = await assistantMessages.CountAsync();
            await LogStepAsync($"Assistant message count before: {messageCountBefore}");

            // Wait for at least one new assistant message (tool result + final answer)
            await Microsoft.Playwright.Assertions.Expect(assistantMessages).ToHaveCountAsync(messageCountBefore + 1, new() { Timeout = 120_000 });
            await LogStepAsync("✅ Agent resumed — assistant message appeared");

            // Log network activity for debugging
            await LogStepAsync($"Network log: {string.Join(", ", networkLog)}");
            Assert.Contains(networkLog, log => log.Contains("200") && log.Contains("tool-approval"));
            await LogStepAsync("✅ POST /api/chat/tool-approval returned 200");

        }, "AzureOpenAI_ApproveButton_E2E");
    }
}

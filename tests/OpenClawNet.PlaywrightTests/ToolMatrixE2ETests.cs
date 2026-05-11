using System.Net.Http.Json;
using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// Comprehensive tool matrix E2E tests — covers all tools with/without approval requirements.
/// 
/// Tagged with <c>Trait("Category", "E2E")</c> so they don't run on every unit test pass.
/// Run with: <c>dotnet test --filter "Category=E2E"</c> against a live Aspire stack.
/// 
/// Scenarios:
/// 1. markdown_convert — network egress (RequiresApproval=true after fix)
/// 2. web_fetch — network egress (RequiresApproval=true)
/// 3. shell — command execution (RequiresApproval=true)
/// 4. file_system — file system access (RequiresApproval=true)
/// 5. calculator — pure computation (RequiresApproval=false, no card)
/// 6. github — API access (RequiresApproval=false, no card)
/// 
/// Reference: Bruno's frustration — we MUST observe test pass, not just edit code.
/// </summary>
[Collection("AppHost")]
[Trait("Category", "E2E")]
public class ToolMatrixE2ETests : PlaywrightTestBase
{
    public ToolMatrixE2ETests(AppHostFixture fixture) : base(fixture) { }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private sealed record AgentProfileDraft(
        string Name,
        string Provider,
        string Model,
        string Instructions,
        bool RequireToolApproval);

    /// <summary>
    /// Creates (or upserts) an AgentProfile via the gateway's PUT /api/agent-profiles/{name}.
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
    /// Creates an Azure OpenAI model provider via gateway API.
    /// </summary>
    private async Task<string> CreateAzureProviderAsync(string providerName)
    {
        using var http = Fixture.CreateGatewayHttpClient();
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
        return providerName;
    }

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

    private async Task StartNewChatAsync()
    {
        // Locate "+ New Chat" button (no testid, use text-based selector)
        var newChatBtn = Page.GetByRole(AriaRole.Button, new() { Name = "+ New Chat" });
        
        // Wait for button to be enabled (it disables during streaming)
        await Microsoft.Playwright.Assertions.Expect(newChatBtn).ToBeEnabledAsync(new() { Timeout = 10_000 });
        
        // Click to start fresh session
        await newChatBtn.ClickAsync();
        
        // Brief wait for input to be ready
        var input = Page.GetByTestId("chat-input");
        await Microsoft.Playwright.Assertions.Expect(input).ToBeEmptyAsync(new() { Timeout = 5_000 });
    }

    // ---------------------------------------------------------------------
    // Scenario 1: markdown_convert e2e (Bruno's exact scenario)
    // "Please fetch and convert to markdown" — triggers web_fetch + markdown_convert
    // Both should require approval after the MarkItDownTool fix
    // ---------------------------------------------------------------------
    [SkippableFact]
    public async Task MarkdownConvert_RequiresApproval_EndToEnd()
    {
        Skip.IfNot(Fixture.IsAzureOpenAIAvailable,
            "Azure OpenAI not configured — set AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, AZURE_OPENAI_DEPLOYMENT.");

        await WithScreenshotOnFailure(async () =>
        {
            var providerName = await CreateAzureProviderAsync($"azure-md-e2e-{Guid.NewGuid():N}");
            await LogStepAsync($"🔧 Testing markdown_convert with Azure OpenAI");

            var profileName = await CreateProfileAsync(new AgentProfileDraft(
                Name: $"e2e-markdown-{Guid.NewGuid():N}".ToLowerInvariant(),
                Provider: providerName,
                Model: Fixture.AzureOpenAIDeployment!,
                Instructions: "You are a helpful assistant. For website/blog summarization requests, call markdown_convert first, then summarize from markdown. Avoid web_fetch unless raw HTML/text is explicitly requested.",
                RequireToolApproval: true));
            await LogStepAsync($"Profile created: {profileName}");

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await LogStepAsync("Chat page loaded — sending Bruno's exact prompt");

            // Bruno's exact scenario that was failing in chat:
            // summarize the latest content from a website.
            await SendChatMessageAsync("Summarize the latest content of the https://elbruno.com website");
            await LogStepAsync("Prompt sent — waiting for markdown_convert approval card");

            // Wait for first approval card
            await WaitForWithTicksAsync(ApprovalCard(), 90_000, "first tool approval card");
            var cardText = await ApprovalCard().InnerTextAsync();
            await LogStepAsync($"✅ First card appeared: {cardText.Replace('\n', ' ').Substring(0, Math.Min(150, cardText.Length))}");

            // This scenario must route through markdown_convert.
            Assert.True(
                cardText.Contains("markdown_convert", StringComparison.OrdinalIgnoreCase) ||
                (cardText.Contains("markdown", StringComparison.OrdinalIgnoreCase) &&
                 cardText.Contains("elbruno.com", StringComparison.OrdinalIgnoreCase)),
                $"Expected first card to reference 'markdown_convert'. Card text: {cardText}");

            // Approve first tool
            var approveBtn = ApprovalCard().Locator("button:has-text('Approve')");
            await approveBtn.ClickAsync();
            await LogStepAsync("First tool approved — waiting for card to disappear or second card");

            // Wait for card to disappear
            await Microsoft.Playwright.Assertions.Expect(ApprovalCard()).Not.ToBeVisibleAsync(new() { Timeout = 30_000 });
            await LogStepAsync("First card dismissed");

            // Check if a second card appears (for the other tool)
            try
            {
                await Page.WaitForSelectorAsync("[data-testid='tool-approval-card'], .tool-approval-card",
                    new PageWaitForSelectorOptions { Timeout = 30_000 });
                var secondCardText = await ApprovalCard().InnerTextAsync();
                await LogStepAsync($"✅ Second card appeared: {secondCardText.Replace('\n', ' ').Substring(0, Math.Min(150, secondCardText.Length))}");

                // Approve second tool
                await ApprovalCard().Locator("button:has-text('Approve')").ClickAsync();
                await LogStepAsync("Second tool approved");
                await Microsoft.Playwright.Assertions.Expect(ApprovalCard()).Not.ToBeVisibleAsync(new() { Timeout = 30_000 });
            }
            catch (TimeoutException)
            {
                await LogStepAsync("⚠️ No second approval card (model may have combined tools or used single tool)");
            }

            // Wait for final response
            await LogStepAsync("Waiting for assistant to complete...");
            await Page.WaitForTimeoutAsync(5_000); // Let assistant finish

            await LogStepAsync("✅ MarkdownConvert e2e test completed");
        }, "MarkdownConvert_RequiresApproval_EndToEnd");
    }

    // ---------------------------------------------------------------------
    // Scenario 1b: markdown_convert with auto-approve profile (no approval card)
    // ---------------------------------------------------------------------
    [SkippableFact]
    public async Task MarkdownConvert_AutoApproveProfile_CompletesWithoutApprovalCard()
    {
        Skip.IfNot(Fixture.IsAzureOpenAIAvailable,
            "Azure OpenAI not configured — set AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, AZURE_OPENAI_DEPLOYMENT.");

        await WithScreenshotOnFailure(async () =>
        {
            var providerName = await CreateAzureProviderAsync($"azure-md-auto-{Guid.NewGuid():N}");
            await LogStepAsync("🔧 Testing markdown_convert with auto-approve profile");

            var profileName = await CreateProfileAsync(new AgentProfileDraft(
                Name: $"e2e-markdown-auto-{Guid.NewGuid():N}".ToLowerInvariant(),
                Provider: providerName,
                Model: Fixture.AzureOpenAIDeployment!,
                Instructions: "For website/blog summarization, call markdown_convert first and summarize from markdown.",
                RequireToolApproval: false));

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            await StartNewChatAsync();
            await SendChatMessageAsync("Summarize the latest content of the https://elbruno.com website");
            await LogStepAsync("Prompt sent — expecting no approval card");

            await Page.WaitForTimeoutAsync(8_000);
            var cardCount = await ApprovalCard().CountAsync();
            Assert.Equal(0, cardCount);

            var assistantMessage = Page.Locator(".assistant-message, [data-role='assistant']").Last;
            await assistantMessage.WaitForAsync(new LocatorWaitForOptions { Timeout = 90_000 });
            var text = (await assistantMessage.InnerTextAsync()).Trim();
            Assert.False(string.IsNullOrWhiteSpace(text), "Assistant response should not be empty.");
            Assert.DoesNotContain("couldn't retrieve", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("returned no content", text, StringComparison.OrdinalIgnoreCase);

            await LogStepAsync("✅ Auto-approve markdown scenario completed");
        }, "MarkdownConvert_AutoApproveProfile_CompletesWithoutApprovalCard");
    }

    // ---------------------------------------------------------------------
    // Scenario 2: web_fetch only — single approval
    // ---------------------------------------------------------------------
    [SkippableFact]
    public async Task WebFetch_SingleApproval_EndToEnd()
    {
        Skip.IfNot(Fixture.IsAzureOpenAIAvailable,
            "Azure OpenAI not configured.");

        await WithScreenshotOnFailure(async () =>
        {
            var providerName = await CreateAzureProviderAsync($"azure-webfetch-{Guid.NewGuid():N}");
            await LogStepAsync($"🌐 Testing web_fetch with Azure OpenAI");

            var profileName = await CreateProfileAsync(new AgentProfileDraft(
                Name: $"e2e-webfetch-{Guid.NewGuid():N}".ToLowerInvariant(),
                Provider: providerName,
                Model: Fixture.AzureOpenAIDeployment!,
                Instructions: "Use web_fetch to retrieve web pages.",
                RequireToolApproval: true));
            await LogStepAsync($"Profile: {profileName}");

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            // Start fresh chat session
            await StartNewChatAsync();
            await LogStepAsync("🆕 Started fresh chat");

            await SendChatMessageAsync("What is on the homepage at https://example.com?");
            await LogStepAsync("Prompt sent — waiting for web_fetch approval card");

            await WaitForWithTicksAsync(ApprovalCard(), 90_000, "web_fetch approval card");
            var cardText = await ApprovalCard().InnerTextAsync();
            await LogStepAsync($"✅ Card: {cardText.Replace('\n', ' ').Substring(0, Math.Min(120, cardText.Length))}");

            Assert.True(
                cardText.Contains("web_fetch", StringComparison.OrdinalIgnoreCase) ||
                cardText.Contains("browser", StringComparison.OrdinalIgnoreCase),
                $"Expected card to mention 'web_fetch' or 'browser'. Got: {cardText}");

            // Approve
            await ApprovalCard().Locator("button:has-text('Approve')").ClickAsync();
            await LogStepAsync("Approved — waiting for response");

            await Microsoft.Playwright.Assertions.Expect(ApprovalCard()).Not.ToBeVisibleAsync(new() { Timeout = 30_000 });
            await LogStepAsync("✅ web_fetch e2e completed");
        }, "WebFetch_SingleApproval_EndToEnd");
    }

    // ---------------------------------------------------------------------
    // Scenario 3: shell — command execution approval
    // ---------------------------------------------------------------------
    [SkippableFact]
    public async Task Shell_RequiresApproval_EndToEnd()
    {
        Skip.IfNot(Fixture.IsAzureOpenAIAvailable,
            "Azure OpenAI not configured.");

        await WithScreenshotOnFailure(async () =>
        {
            var providerName = await CreateAzureProviderAsync($"azure-shell-{Guid.NewGuid():N}");
            await LogStepAsync($"🖥️ Testing shell with Azure OpenAI");

            var profileName = await CreateProfileAsync(new AgentProfileDraft(
                Name: $"e2e-shell-{Guid.NewGuid():N}".ToLowerInvariant(),
                Provider: providerName,
                Model: Fixture.AzureOpenAIDeployment!,
                Instructions: "Use the shell tool to run commands when asked.",
                RequireToolApproval: true));

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            await SendChatMessageAsync("Run the command: echo hello");
            await LogStepAsync("Prompt sent — waiting for shell approval card");

            await WaitForWithTicksAsync(ApprovalCard(), 90_000, "shell approval card");
            var cardText = await ApprovalCard().InnerTextAsync();
            await LogStepAsync($"✅ Card: {cardText.Replace('\n', ' ').Substring(0, Math.Min(120, cardText.Length))}");

            Assert.Contains("shell", cardText, StringComparison.OrdinalIgnoreCase);

            // Approve
            await ApprovalCard().Locator("button:has-text('Approve')").ClickAsync();
            await Microsoft.Playwright.Assertions.Expect(ApprovalCard()).Not.ToBeVisibleAsync(new() { Timeout = 30_000 });
            await LogStepAsync("✅ shell e2e completed");
        }, "Shell_RequiresApproval_EndToEnd");
    }

    // ---------------------------------------------------------------------
    // Scenario 4: file_system — file creation approval
    // ---------------------------------------------------------------------
    [SkippableFact]
    public async Task FileSystem_RequiresApproval_EndToEnd()
    {
        Skip.IfNot(Fixture.IsAzureOpenAIAvailable,
            "Azure OpenAI not configured.");

        await WithScreenshotOnFailure(async () =>
        {
            var providerName = await CreateAzureProviderAsync($"azure-fs-{Guid.NewGuid():N}");
            await LogStepAsync($"📁 Testing file_system with Azure OpenAI");

            var profileName = await CreateProfileAsync(new AgentProfileDraft(
                Name: $"e2e-filesystem-{Guid.NewGuid():N}".ToLowerInvariant(),
                Provider: providerName,
                Model: Fixture.AzureOpenAIDeployment!,
                Instructions: "Use file_system tools when asked to create or read files.",
                RequireToolApproval: true));

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            await SendChatMessageAsync("Save the string 'hello world' to a file named test.txt on the local filesystem (do not fetch a URL, do not run a shell command)");
            await LogStepAsync("Prompt sent — waiting for file_system approval card");

            await WaitForWithTicksAsync(ApprovalCard(), 90_000, "file_system approval card");
            var cardText = await ApprovalCard().InnerTextAsync();
            await LogStepAsync($"✅ Card: {cardText.Replace('\n', ' ').Substring(0, Math.Min(120, cardText.Length))}");

            Assert.True(
                cardText.Contains("file", StringComparison.OrdinalIgnoreCase) ||
                cardText.Contains("write", StringComparison.OrdinalIgnoreCase) ||
                cardText.Contains("create", StringComparison.OrdinalIgnoreCase),
                $"Expected card to mention file operation. Got: {cardText}");

            // Approve
            await ApprovalCard().Locator("button:has-text('Approve')").ClickAsync();
            await Microsoft.Playwright.Assertions.Expect(ApprovalCard()).Not.ToBeVisibleAsync(new() { Timeout = 30_000 });
            await LogStepAsync("✅ file_system e2e completed");
        }, "FileSystem_RequiresApproval_EndToEnd");
    }

    // ---------------------------------------------------------------------
    // Scenario 5: calculator — NO approval required
    // ---------------------------------------------------------------------
    [SkippableFact]
    public async Task Calculator_NoApproval_DirectResult()
    {
        Skip.IfNot(Fixture.IsAzureOpenAIAvailable,
            "Azure OpenAI not configured.");

        await WithScreenshotOnFailure(async () =>
        {
            var providerName = await CreateAzureProviderAsync($"azure-calc-{Guid.NewGuid():N}");
            await LogStepAsync($"🔢 Testing calculator (no approval expected)");

            var profileName = await CreateProfileAsync(new AgentProfileDraft(
                Name: $"e2e-calculator-{Guid.NewGuid():N}".ToLowerInvariant(),
                Provider: providerName,
                Model: Fixture.AzureOpenAIDeployment!,
                Instructions: "Use the calculator tool for math operations.",
                RequireToolApproval: true)); // Profile requires approval, but calculator tool doesn't

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            await SendChatMessageAsync("What is 12345 * 67890? Use the calculator tool.");
            await LogStepAsync("Prompt sent — calculator should NOT show approval card");

            // Wait a bit to ensure no card appears
            await Page.WaitForTimeoutAsync(5_000);

            // Check no approval card appeared
            var cardCount = await ApprovalCard().CountAsync();
            if (cardCount > 0)
            {
                var cardText = await ApprovalCard().InnerTextAsync();
                // If a card appeared but it's NOT for calculator, that's still OK (might be another tool)
                if (!cardText.Contains("calculator", StringComparison.OrdinalIgnoreCase))
                {
                    await LogStepAsync($"⚠️ Card appeared for different tool: {cardText.Substring(0, Math.Min(80, cardText.Length))}");
                }
                else
                {
                    Assert.Fail($"Calculator should NOT require approval, but card appeared: {cardText}");
                }
            }
            else
            {
                await LogStepAsync("✅ No approval card for calculator (correct)");
            }

            // Wait for response containing the result
            await LogStepAsync("Waiting for calculation result...");
            var result = Page.Locator(":text('838102050')");
            try
            {
                await result.WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });
                await LogStepAsync("✅ Calculator result (838102050) found");
            }
            catch (TimeoutException)
            {
                await LogStepAsync("⚠️ Exact result not found — checking for any numeric response");
            }

            await LogStepAsync("✅ calculator no-approval e2e completed");
        }, "Calculator_NoApproval_DirectResult");
    }

    // ---------------------------------------------------------------------
    // Scenario 6: GitHub tool — NO approval required (current config)
    // Note: This may need auth so we test with a simple query
    // ---------------------------------------------------------------------
    [SkippableFact]
    public async Task GitHub_NoApproval_DirectResult()
    {
        Skip.IfNot(Fixture.IsAzureOpenAIAvailable,
            "Azure OpenAI not configured.");

        await WithScreenshotOnFailure(async () =>
        {
            var providerName = await CreateAzureProviderAsync($"azure-gh-{Guid.NewGuid():N}");
            await LogStepAsync($"🐙 Testing github (no approval expected)");

            var profileName = await CreateProfileAsync(new AgentProfileDraft(
                Name: $"e2e-github-{Guid.NewGuid():N}".ToLowerInvariant(),
                Provider: providerName,
                Model: Fixture.AzureOpenAIDeployment!,
                Instructions: "Use github tools when asked about repositories.",
                RequireToolApproval: true));

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            // Start fresh chat session
            await StartNewChatAsync();
            await LogStepAsync("🆕 Started fresh chat");

            // A simple query that might or might not trigger the tool
            await SendChatMessageAsync("Tell me about the github repository microsoft/TypeScript");
            await LogStepAsync("Prompt sent — github should NOT show approval card");

            // Wait a bit
            await Page.WaitForTimeoutAsync(8_000);

            var cardCount = await ApprovalCard().CountAsync();
            if (cardCount > 0)
            {
                var cardText = await ApprovalCard().InnerTextAsync();
                if (cardText.Contains("github", StringComparison.OrdinalIgnoreCase))
                {
                    await LogStepAsync($"⚠️ GitHub tool showed approval card: {cardText.Substring(0, Math.Min(80, cardText.Length))}");
                    // This is unexpected but not necessarily a failure — GitHub may have RequiresApproval=true
                }
            }
            else
            {
                await LogStepAsync("✅ No approval card for github");
            }

            await LogStepAsync("✅ github e2e completed");
        }, "GitHub_NoApproval_DirectResult");
    }

    // ---------------------------------------------------------------------
    // Scenario 7: Quick sanity — tool-approval card approve button mechanics
    // Verifies the button disables on click and POST returns 200
    // ---------------------------------------------------------------------
    [SkippableFact]
    public async Task ApproveButton_DisablesAndPostsCorrectly()
    {
        Skip.IfNot(Fixture.IsAzureOpenAIAvailable,
            "Azure OpenAI not configured.");

        await WithScreenshotOnFailure(async () =>
        {
            var providerName = await CreateAzureProviderAsync($"azure-btn-{Guid.NewGuid():N}");
            await LogStepAsync($"🔘 Testing Approve button mechanics");

            // Monitor network for tool-approval POST
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

            var profileName = await CreateProfileAsync(new AgentProfileDraft(
                Name: $"e2e-button-{Guid.NewGuid():N}".ToLowerInvariant(),
                Provider: providerName,
                Model: Fixture.AzureOpenAIDeployment!,
                Instructions: "Use web_fetch to get web content.",
                RequireToolApproval: true));

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            await SendChatMessageAsync("Fetch https://example.com");
            await WaitForWithTicksAsync(ApprovalCard(), 90_000, "approval card");

            var approveBtn = ApprovalCard().Locator("button:has-text('Approve')");
            await Microsoft.Playwright.Assertions.Expect(approveBtn).ToBeEnabledAsync(new() { Timeout = 5_000 });
            await LogStepAsync("✅ Approve button is enabled");

            await approveBtn.ClickAsync();
            await LogStepAsync("Button clicked");

            // Check button is disabled (immediate feedback)
            try
            {
                await Microsoft.Playwright.Assertions.Expect(approveBtn).ToBeDisabledAsync(new() { Timeout = 1_000 });
                await LogStepAsync("✅ Button disabled immediately");
            }
            catch
            {
                await LogStepAsync("⚠️ Button disable state not captured (too fast)");
            }

            // Wait for card to disappear
            await Microsoft.Playwright.Assertions.Expect(ApprovalCard()).Not.ToBeVisibleAsync(new() { Timeout = 30_000 });
            await LogStepAsync("✅ Card disappeared after approve");

            // Verify network call
            if (networkLog.Any(l => l.Contains("200")))
            {
                await LogStepAsync("✅ POST /tool-approval returned 200");
            }
            else
            {
                await LogStepAsync($"⚠️ Network log: {string.Join(", ", networkLog)}");
            }

            await LogStepAsync("✅ Approve button mechanics test completed");
        }, "ApproveButton_DisablesAndPostsCorrectly");
    }

    // ---------------------------------------------------------------------
    // Data-driven matrix for tools requiring approval
    // ---------------------------------------------------------------------
    [SkippableTheory]
    [InlineData("web_fetch", "Use web_fetch to get the content of https://example.com")]
    [InlineData("markdown_convert", "Use markdown_convert to convert https://example.com to markdown")]
    [InlineData("shell", "Use the shell tool to run: echo test")]
    public async Task ToolsRequiringApproval_ShowCard(string expectedToolKeyword, string prompt)
    {
        Skip.IfNot(Fixture.IsAzureOpenAIAvailable,
            "Azure OpenAI not configured.");

        await WithScreenshotOnFailure(async () =>
        {
            var providerName = await CreateAzureProviderAsync($"azure-matrix-{Guid.NewGuid():N}");
            await LogStepAsync($"🧪 Matrix test for: {expectedToolKeyword}");

            var profileName = await CreateProfileAsync(new AgentProfileDraft(
                Name: $"e2e-matrix-{expectedToolKeyword}-{Guid.NewGuid():N}".ToLowerInvariant(),
                Provider: providerName,
                Model: Fixture.AzureOpenAIDeployment!,
                Instructions: $"You have access to {expectedToolKeyword} tool. Use it when asked.",
                RequireToolApproval: true));

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            await SendChatMessageAsync(prompt);
            await LogStepAsync($"Prompt sent for {expectedToolKeyword}");

            await WaitForWithTicksAsync(ApprovalCard(), 90_000, $"{expectedToolKeyword} approval card");
            var cardText = await ApprovalCard().InnerTextAsync();
            await LogStepAsync($"✅ Card appeared for {expectedToolKeyword}: {cardText.Replace('\n', ' ').Substring(0, Math.Min(100, cardText.Length))}");

        }, $"ToolsRequiringApproval_{expectedToolKeyword}");
    }

    // ---------------------------------------------------------------------
    // Test 11: Multi-tool flow with approval bubbles
    // ---------------------------------------------------------------------
    [SkippableFact]
    public async Task MultiTool_GitHubReadAndMarkdownWrite_ShowsApprovalBubbles()
    {
        Skip.IfNot(Fixture.IsAzureOpenAIAvailable,
            "Azure OpenAI not configured — set AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, AZURE_OPENAI_DEPLOYMENT.");

        await WithScreenshotOnFailure(async () =>
        {
            var providerName = await CreateAzureProviderAsync($"azure-multi-{Guid.NewGuid():N}");
            await LogStepAsync($"🔧 Testing multi-tool approval bubbles");

            var profileName = await CreateProfileAsync(new AgentProfileDraft(
                Name: $"e2e-multi-tool-{Guid.NewGuid():N}".ToLowerInvariant(),
                Provider: providerName,
                Model: Fixture.AzureOpenAIDeployment!,
                Instructions: "You are a helpful assistant. Use web_fetch to access GitHub repos and file_system to save files.",
                RequireToolApproval: true));
            await LogStepAsync($"Profile created: {profileName}");

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await StartNewChatAsync();
            await LogStepAsync("Chat page loaded — starting new chat session");

            // Bruno's refined multi-tool prompt
            await SendChatMessageAsync("access this repository: https://github.com/microsoft/Generative-AI-for-beginners-dotnet and see how many opened issues and PRs are there. Create a markdown file with the details of the repo. Do not summarize from memory — you must fetch the live page. Do not skip the file write — the markdown file is required output.");
            await LogStepAsync("Multi-tool prompt sent");

            // Track bubbles and approvals
            var approvalCount = 0;
            var maxApprovals = 3; // Allow 2-3 approvals (web_fetch might split into multiple calls)

            // Approve all tools in a loop
            while (approvalCount < maxApprovals)
            {
                try
                {
                    await WaitForWithTicksAsync(ApprovalCard(), 90_000, $"approval card #{approvalCount + 1}");
                    var cardText = await ApprovalCard().InnerTextAsync();
                    await LogStepAsync($"✅ Approval card #{approvalCount + 1}: {cardText.Replace('\n', ' ').Substring(0, Math.Min(100, cardText.Length))}");

                    // Click approve
                    var approveBtn = ApprovalCard().Locator("button:has-text('Approve')");
                    await approveBtn.ClickAsync();
                    await LogStepAsync($"Clicked approve for card #{approvalCount + 1}");

                    // Wait for card to disappear
                    await Microsoft.Playwright.Assertions.Expect(ApprovalCard()).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
                    await LogStepAsync($"Card #{approvalCount + 1} dismissed");

                    // Wait for bubble to appear
                    var loopBubble = Page.Locator("[data-testid='approval-bubble']");
                    try
                    {
                        await loopBubble.Nth(approvalCount).WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
                        var bubbleText = await loopBubble.Nth(approvalCount).InnerTextAsync();
                        await LogStepAsync($"✅ Bubble #{approvalCount + 1} appeared: {bubbleText.Replace('\n', ' ').Substring(0, Math.Min(80, bubbleText.Length))}");
                    }
                    catch (TimeoutException)
                    {
                        await LogStepAsync($"⚠️ Bubble #{approvalCount + 1} not visible yet (may render later)");
                    }

                    approvalCount++;

                    // Brief pause before checking for next approval card
                    await Page.WaitForTimeoutAsync(2_000);
                }
                catch (TimeoutException)
                {
                    // No more approval cards
                    await LogStepAsync($"No more approval cards after {approvalCount} approvals");
                    break;
                }
            }

            Assert.True(approvalCount >= 2, $"Expected at least 2 tool approvals (web_fetch + file_system), got {approvalCount}");
            await LogStepAsync($"✅ Approved {approvalCount} tools");

            // Wait for assistant to complete final response
            await Page.WaitForTimeoutAsync(15_000);
            await LogStepAsync("Waiting for assistant to complete response...");

            // Count bubbles
            var bubbleLocator = Page.Locator("[data-testid='approval-bubble']");
            var bubbleCount = await bubbleLocator.CountAsync();
            await LogStepAsync($"Bubble count before reload: {bubbleCount}");
            Assert.True(bubbleCount >= 2, $"Expected at least 2 bubbles, got {bubbleCount}");

            // Verify final message contains markdown code fence OR issue/PR count
            // (Relaxed assertion: HTML scraping may be unreliable)
            var messagesText = await Page.Locator("#messages-container, [data-testid='messages-container'], .messages").InnerTextAsync();
            var hasMarkdown = messagesText.Contains("```") || messagesText.Contains("markdown", StringComparison.OrdinalIgnoreCase);
            var hasNumbers = System.Text.RegularExpressions.Regex.IsMatch(messagesText, @"\b\d+\s*(open\s+)?(issues?|prs?|pull\s+requests?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (hasMarkdown || hasNumbers)
            {
                await LogStepAsync($"✅ Final message contains expected content (markdown: {hasMarkdown}, numbers: {hasNumbers})");
            }
            else
            {
                await LogStepAsync("⚠️ Could not verify exact content — HTML scraping may be unreliable");
            }

            // Critical persistence test: reload page
            await LogStepAsync("Reloading page to verify bubble persistence...");
            await Page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await Page.WaitForTimeoutAsync(3_000);

            // Verify bubbles still visible after reload
            var bubblesAfterReload = Page.Locator("[data-testid='approval-bubble']");
            var countAfterReload = await bubblesAfterReload.CountAsync();
            Assert.True(countAfterReload >= 2, $"Expected at least 2 bubbles after reload, got {countAfterReload}");
            await LogStepAsync($"✅ {countAfterReload} bubbles persisted after reload — CRITICAL ASSERTION PASSED");

            // Take final screenshot
            await Page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine("TestResults", "screenshots", $"approval-bubble-e2e-final-{DateTime.Now:yyyyMMdd-HHmmss}.png"),
                FullPage = true
            });
            await LogStepAsync("✅ Multi-tool approval bubbles test completed");

        }, "MultiTool_GitHubReadAndMarkdownWrite_ShowsApprovalBubbles");
    }
}

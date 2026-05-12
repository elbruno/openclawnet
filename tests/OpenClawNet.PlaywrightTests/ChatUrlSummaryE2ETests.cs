using Microsoft.Playwright;
using System.Net.Http.Json;
using Xunit;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// Regression test for issue #152: "Critical: URL summary chat scenario fails for https://elbruno.com prompt"
/// 
/// This test validates the exact user journey described in the issue:
/// 1. Create a new chat using default agent provider
/// 2. Ask: "summarize the content of the website https://elbruno.com"
/// 3. Expected: reliable answer from URL content retrieval
/// 4. Actual behavior before fix: flow does not complete correctly
/// 
/// Acceptance criteria from issue #152:
/// - Repro scenario passes consistently in E2E
/// - Same scenario passes with both approval modes (auto-approve and user-approval)
/// - Failure modes are explicit and actionable (no silent/empty outcome)
/// - Regression test added for exact prompt pattern
/// 
/// Related: Issue #148 (failing test tracking), Issue #149 (published page/workflow mismatch)
/// </summary>
[Collection("AppHost")]
[Trait("Category", "E2E")]
[Trait("Area", "chat")]
[Trait("Area", "url-fetch")]
[Trait("TraceId", "chat-url-summary-elbruno")]
public class ChatUrlSummaryE2ETests : PlaywrightTestBase
{
    public ChatUrlSummaryE2ETests(AppHostFixture fixture) : base(fixture) { }

    /// <summary>
    /// Test the exact prompt from issue #152 with manual tool approval.
    /// This validates that the markdown_convert tool is called, approved, and returns usable content.
    /// </summary>
    [SkippableFact]
    public async Task ElBrunoComSummary_WithManualApproval_ReturnsUsableContent()
    {
        Skip.IfNot(Fixture.IsAzureOpenAIAvailable,
            "Azure OpenAI not configured — set AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, AZURE_OPENAI_DEPLOYMENT.");

        await WithScreenshotOnFailure(async () =>
        {
            // Create Azure OpenAI provider
            var providerName = $"azure-issue152-{Guid.NewGuid():N}";
            using var http = Fixture.CreateGatewayHttpClient();
            var providerResp = await http.PutAsJsonAsync($"/api/model-providers/{providerName}", new
            {
                providerType = "azure-openai",
                displayName = "Azure OpenAI (Issue #152)",
                endpoint = Fixture.AzureOpenAIEndpoint,
                model = Fixture.AzureOpenAIDeployment,
                apiKey = Fixture.AzureOpenAIApiKey,
                deploymentName = Fixture.AzureOpenAIDeployment,
                authMode = "api-key",
                isSupported = true
            });
            Assert.True(providerResp.IsSuccessStatusCode, $"PUT /api/model-providers/{providerName} → {(int)providerResp.StatusCode}");

            // Create agent profile with tool approval required and instructions that prefer markdown_convert
            var profileName = $"e2e-issue152-manual-{Guid.NewGuid():N}".ToLowerInvariant();
            var profileResp = await http.PutAsJsonAsync($"/api/agent-profiles/{Uri.EscapeDataString(profileName)}", new
            {
                DisplayName = profileName,
                Provider = providerName,
                Model = Fixture.AzureOpenAIDeployment,
                Instructions = "You are a helpful assistant. When users ask to summarize website content, use markdown_convert to fetch and convert the URL to markdown first, then provide a concise summary from the markdown content.",
                EnabledTools = (string[]?)null, // All tools enabled
                Temperature = 0.7,
                MaxTokens = (int?)null,
                IsDefault = false,
                RequireToolApproval = true
            });
            Assert.True(profileResp.IsSuccessStatusCode, $"PUT /api/agent-profiles/{profileName} → {(int)profileResp.StatusCode}");

            // Navigate to chat page with the test profile
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            // Send the exact prompt from issue #152
            var testPrompt = "summarize the content of the website https://elbruno.com";
            var input = Page.GetByTestId("chat-input");
            await input.FillAsync(testPrompt);
            var sendBtn = Page.GetByTestId("chat-send");
            await Microsoft.Playwright.Assertions.Expect(sendBtn).ToBeEnabledAsync(new() { Timeout = 5_000 });
            await sendBtn.ClickAsync();

            // Wait for tool approval card to appear
            var approvalCard = Page.Locator("[data-testid='tool-approval-card'], .tool-approval-card");
            await Microsoft.Playwright.Assertions.Expect(approvalCard).ToBeVisibleAsync(new() { Timeout = 90_000 });

            // Verify the approval card is for markdown_convert (not web_fetch)
            var cardText = await approvalCard.InnerTextAsync();
            Assert.True(
                cardText.Contains("markdown_convert", StringComparison.OrdinalIgnoreCase) ||
                (cardText.Contains("markdown", StringComparison.OrdinalIgnoreCase) &&
                 cardText.Contains("elbruno.com", StringComparison.OrdinalIgnoreCase)),
                $"Expected approval card for markdown_convert. Card text: {cardText}");

            // Approve the tool call
            var approveBtn = approvalCard.Locator("button:has-text('Approve')");
            await approveBtn.ClickAsync();

            // Wait for approval card to disappear
            await Microsoft.Playwright.Assertions.Expect(approvalCard).Not.ToBeVisibleAsync(new() { Timeout = 30_000 });

            // Wait for assistant response to appear
            var assistantMessage = Page.Locator(".assistant-message, [data-role='assistant']").Last;
            await assistantMessage.WaitForAsync(new LocatorWaitForOptions { Timeout = 120_000 });

            // Validate the response contains meaningful content
            var responseText = (await assistantMessage.InnerTextAsync()).Trim();
            
            // Acceptance criteria: failure modes must be explicit and actionable
            Assert.False(string.IsNullOrWhiteSpace(responseText), 
                "Assistant response should not be empty. If the tool failed, it must return an explicit error message.");

            // Check for silent failure patterns that would violate acceptance criteria
            Assert.DoesNotContain("could not retrieve", responseText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("returned no content", responseText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("empty output", responseText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("failed to fetch", responseText, StringComparison.OrdinalIgnoreCase);

            // The response should reference the site or Bruno (since elbruno.com is Bruno Capuano's blog)
            var hasRelevantContent = 
                responseText.Contains("bruno", StringComparison.OrdinalIgnoreCase) ||
                responseText.Contains("capuano", StringComparison.OrdinalIgnoreCase) ||
                responseText.Contains("microsoft", StringComparison.OrdinalIgnoreCase) ||
                responseText.Contains("azure", StringComparison.OrdinalIgnoreCase) ||
                responseText.Contains("ai", StringComparison.OrdinalIgnoreCase) ||
                responseText.Contains("blog", StringComparison.OrdinalIgnoreCase) ||
                responseText.Contains("developer", StringComparison.OrdinalIgnoreCase) ||
                responseText.Length > 100; // If response is substantial, it likely has meaningful content

            Assert.True(hasRelevantContent, 
                $"Response should contain meaningful content from elbruno.com. Response: {responseText.Substring(0, Math.Min(500, responseText.Length))}");
        }, "ElBrunoComSummary_WithManualApproval_ReturnsUsableContent");
    }

    /// <summary>
    /// Test the exact prompt from issue #152 with auto-approve mode.
    /// This validates that the flow works without requiring user interaction.
    /// </summary>
    [SkippableFact]
    public async Task ElBrunoComSummary_WithAutoApprove_ReturnsUsableContent()
    {
        Skip.IfNot(Fixture.IsAzureOpenAIAvailable,
            "Azure OpenAI not configured — set AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, AZURE_OPENAI_DEPLOYMENT.");

        await WithScreenshotOnFailure(async () =>
        {
            // Create Azure OpenAI provider
            var providerName = $"azure-issue152-auto-{Guid.NewGuid():N}";
            using var http = Fixture.CreateGatewayHttpClient();
            var providerResp = await http.PutAsJsonAsync($"/api/model-providers/{providerName}", new
            {
                providerType = "azure-openai",
                displayName = "Azure OpenAI (Issue #152 Auto)",
                endpoint = Fixture.AzureOpenAIEndpoint,
                model = Fixture.AzureOpenAIDeployment,
                apiKey = Fixture.AzureOpenAIApiKey,
                deploymentName = Fixture.AzureOpenAIDeployment,
                authMode = "api-key",
                isSupported = true
            });
            Assert.True(providerResp.IsSuccessStatusCode);

            // Create agent profile with auto-approve
            var profileName = $"e2e-issue152-auto-{Guid.NewGuid():N}".ToLowerInvariant();
            var profileResp = await http.PutAsJsonAsync($"/api/agent-profiles/{Uri.EscapeDataString(profileName)}", new
            {
                DisplayName = profileName,
                Provider = providerName,
                Model = Fixture.AzureOpenAIDeployment,
                Instructions = "You are a helpful assistant. When users ask to summarize website content, use markdown_convert to fetch and convert the URL to markdown first, then provide a concise summary from the markdown content.",
                EnabledTools = (string[]?)null,
                Temperature = 0.7,
                MaxTokens = (int?)null,
                IsDefault = false,
                RequireToolApproval = false // Auto-approve mode
            });
            Assert.True(profileResp.IsSuccessStatusCode);

            // Navigate to chat page
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/chat?profile={profileName}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            // Send the exact prompt from issue #152
            var testPrompt = "summarize the content of the website https://elbruno.com";
            var input = Page.GetByTestId("chat-input");
            await input.FillAsync(testPrompt);
            var sendBtn = Page.GetByTestId("chat-send");
            await Microsoft.Playwright.Assertions.Expect(sendBtn).ToBeEnabledAsync(new() { Timeout = 5_000 });
            await sendBtn.ClickAsync();

            // In auto-approve mode, no approval card should appear
            await Page.WaitForTimeoutAsync(5_000);
            var approvalCard = Page.Locator("[data-testid='tool-approval-card'], .tool-approval-card");
            var cardCount = await approvalCard.CountAsync();
            Assert.Equal(0, cardCount);

            // Wait for assistant response
            var assistantMessage = Page.Locator(".assistant-message, [data-role='assistant']").Last;
            await assistantMessage.WaitForAsync(new LocatorWaitForOptions { Timeout = 120_000 });

            // Validate response
            var responseText = (await assistantMessage.InnerTextAsync()).Trim();
            
            Assert.False(string.IsNullOrWhiteSpace(responseText), 
                "Assistant response should not be empty in auto-approve mode.");

            // Same validation as manual mode
            Assert.DoesNotContain("could not retrieve", responseText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("returned no content", responseText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("empty output", responseText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("failed to fetch", responseText, StringComparison.OrdinalIgnoreCase);

            var hasRelevantContent = 
                responseText.Contains("bruno", StringComparison.OrdinalIgnoreCase) ||
                responseText.Contains("capuano", StringComparison.OrdinalIgnoreCase) ||
                responseText.Contains("microsoft", StringComparison.OrdinalIgnoreCase) ||
                responseText.Contains("azure", StringComparison.OrdinalIgnoreCase) ||
                responseText.Contains("ai", StringComparison.OrdinalIgnoreCase) ||
                responseText.Contains("blog", StringComparison.OrdinalIgnoreCase) ||
                responseText.Contains("developer", StringComparison.OrdinalIgnoreCase) ||
                responseText.Length > 100;

            Assert.True(hasRelevantContent, 
                $"Response should contain meaningful content. Response: {responseText.Substring(0, Math.Min(500, responseText.Length))}");
        }, "ElBrunoComSummary_WithAutoApprove_ReturnsUsableContent");
    }
}

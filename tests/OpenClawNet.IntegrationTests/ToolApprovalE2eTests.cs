using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace OpenClawNet.IntegrationTests;

/// <summary>
/// E2E test for Bruno's tool approval workflow scenario:
/// 1. Start Aspire (if not running)
/// 2. Open web app at http://localhost:5010
/// 3. Navigate to Chat page
/// 4. Create new chat with default agent
/// 5. Send: "ok, convert the content of elbruno.com to markdown and save it on a file"
/// 6. Wait for tool_approval event
/// 7. Click Approve
/// 8. Verify: file saved, result shown, no errors
/// 
/// IMPORTANT: This test requires a running Aspire stack with:
/// - Gateway service (API backend)
/// - Web service (Blazor frontend) on port 5010
/// - Tool-capable model (e.g., Ollama with qwen2.5:3b or Azure OpenAI)
/// 
/// Run with: dotnet test --filter "FullyQualifiedName~ToolApprovalE2eTests"
/// </summary>
[Collection("E2E")]
[Trait("Category", "E2E")]
[Trait("Category", "ToolApproval")]
public class ToolApprovalE2eTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;
    private HttpClient? _gatewayClient;

    // Configuration
    private const string WebAppUrl = "http://localhost:5010";
    private const string GatewayApiUrl = "http://localhost:5000";
    
    public ToolApprovalE2eTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        // 1. Check if Aspire is running, start if needed
        await StartAspireIfNeededAsync();

        // 2. Initialize Playwright and browser
        _playwright = await Playwright.CreateAsync();
        
        var headed = Environment.GetEnvironmentVariable("PLAYWRIGHT_HEADED")
            ?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = !headed,
            SlowMo = headed ? 500 : 0
        });

        _context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 }
        });

        _page = await _context.NewPageAsync();
        _page.SetDefaultTimeout(30_000);

        // 3. Initialize HTTP client for Gateway API
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = 
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        _gatewayClient = new HttpClient(handler) 
        { 
            BaseAddress = new Uri(GatewayApiUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    public async Task DisposeAsync()
    {
        _gatewayClient?.Dispose();
        if (_page is not null) await _page.CloseAsync();
        if (_context is not null) await _context.DisposeAsync();
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
    }

    /// <summary>
    /// Full E2E test for Bruno's tool approval workflow with web_fetch + markdown tool.
    /// Tests the complete flow: send message, wait for approval prompt, approve, verify result.
    /// </summary>
    [SkippableFact]
    public async Task ToolApprovalWorkflow_ApprovesAndExecutes_WebFetchAndMarkdown()
    {
        // Skip if model not available (Ollama or Azure OpenAI required)
        Skip.IfNot(await IsModelAvailableAsync(), 
            "No tool-capable model available. Requires Ollama (qwen2.5:3b) or Azure OpenAI.");

        try
        {
            // Step 1: Create agent profile with RequireToolApproval = true
            var profileName = await CreateApprovalRequiredProfileAsync();
            _output.WriteLine($"Created profile: {profileName}");

            // Step 2: Navigate to web app with the profile
            await _page!.GotoAsync($"{WebAppUrl}/?profile={profileName}", 
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            _output.WriteLine($"Navigated to {WebAppUrl}");

            // Step 3: Send the chat message
            var testMessage = "ok, convert the content of elbruno.com to markdown and save it on a file";
            await SendChatMessageAsync(testMessage);
            _output.WriteLine($"Sent message: {testMessage}");

            // Step 4: Wait for tool approval card to appear (timeout: 30s)
            var approvalCard = await WaitForToolApprovalCardAsync(TimeSpan.FromSeconds(30));
            Assert.NotNull(approvalCard);
            
            var cardText = await approvalCard.InnerTextAsync();
            _output.WriteLine($"Approval card appeared: {cardText}");
            
            // Verify the approval card mentions web_fetch or browser tool
            Assert.True(
                cardText.Contains("web_fetch", StringComparison.OrdinalIgnoreCase) ||
                cardText.Contains("browser", StringComparison.OrdinalIgnoreCase) ||
                cardText.Contains("elbruno.com", StringComparison.OrdinalIgnoreCase),
                "Approval card should reference web_fetch/browser tool or the target URL");

            // Step 5: Click the Approve button
            await ClickApproveButtonAsync(approvalCard);
            _output.WriteLine("Clicked Approve button");

            // Step 6: Wait for tool execution to complete and result to appear (timeout: 60s)
            await WaitForToolExecutionResultAsync(TimeSpan.FromSeconds(60));
            _output.WriteLine("Tool execution completed");

            // Step 7: Verify result - check for success indicators
            await VerifyResultAsync();
            _output.WriteLine("Result verified successfully");

            // Step 8: Take screenshot for documentation
            await CaptureScreenshotAsync("success");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Test failed: {ex.Message}");
            await CaptureScreenshotAsync("failure");
            throw;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helper Methods
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks if Aspire is running by probing the gateway health endpoint.
    /// If not running, attempts to start it via `aspire start`.
    /// </summary>
    private async Task StartAspireIfNeededAsync()
    {
        try
        {
            using var testClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await testClient.GetAsync($"{GatewayApiUrl}/health");
            
            if (response.IsSuccessStatusCode)
            {
                _output.WriteLine("Aspire is already running");
                return;
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Gateway not reachable: {ex.Message}");
        }

        // Try to start Aspire
        _output.WriteLine("Attempting to start Aspire...");
        
        var startInfo = new ProcessStartInfo
        {
            FileName = "aspire",
            Arguments = "start",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."))
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException(
                "Failed to start Aspire. Ensure 'aspire' CLI is installed and AppHost is configured.");
        }

        // Wait up to 2 minutes for services to start
        var timeout = TimeSpan.FromMinutes(2);
        var stopwatch = Stopwatch.StartNew();
        
        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                using var testClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var response = await testClient.GetAsync($"{GatewayApiUrl}/health");
                
                if (response.IsSuccessStatusCode)
                {
                    _output.WriteLine($"Aspire started successfully after {stopwatch.Elapsed.TotalSeconds:F1}s");
                    return;
                }
            }
            catch
            {
                // Not ready yet, continue waiting
            }

            await Task.Delay(5000);
        }

        throw new TimeoutException(
            $"Aspire did not become healthy within {timeout.TotalSeconds}s. " +
            "Check logs with 'aspire describe' or start manually.");
    }

    /// <summary>
    /// Creates an agent profile with RequireToolApproval = true.
    /// Returns the profile name.
    /// </summary>
    private async Task<string> CreateApprovalRequiredProfileAsync()
    {
        var profileName = $"bruno-e2e-test-{Guid.NewGuid():N}";
        
        var profilePayload = new
        {
            DisplayName = profileName,
            Provider = "ollama",
            Model = "qwen2.5:3b",
            Instructions = "You are a helpful assistant. Use the web_fetch tool to retrieve web pages and the file_write tool to save content. When asked to convert a website to markdown and save it, fetch the URL first, then write the result to a file.",
            EnabledTools = (string[]?)null, // null = all tools enabled
            Temperature = 0.7,
            MaxTokens = (int?)null,
            IsDefault = false,
            RequireToolApproval = true
        };

        var response = await _gatewayClient!.PutAsJsonAsync(
            $"/api/agent-profiles/{Uri.EscapeDataString(profileName)}", 
            profilePayload);
        
        response.EnsureSuccessStatusCode();
        return profileName;
    }

    /// <summary>
    /// Checks if a tool-capable model is available (Ollama or Azure OpenAI).
    /// </summary>
    private async Task<bool> IsModelAvailableAsync()
    {
        // Check Ollama
        try
        {
            var ollamaBase = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") 
                ?? "http://localhost:11434";
            
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var resp = await http.GetAsync($"{ollamaBase}/api/tags");
            
            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                if (body.Contains("qwen2.5:3b", StringComparison.OrdinalIgnoreCase))
                {
                    _output.WriteLine("Found Ollama model: qwen2.5:3b");
                    return true;
                }
            }
        }
        catch
        {
            // Ollama not available
        }

        // Check Azure OpenAI
        var azEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        var azKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
        var azDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT");

        if (!string.IsNullOrWhiteSpace(azEndpoint) && 
            !string.IsNullOrWhiteSpace(azKey) && 
            !string.IsNullOrWhiteSpace(azDeployment))
        {
            _output.WriteLine("Found Azure OpenAI configuration");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Sends a chat message by filling the input and clicking Send.
    /// </summary>
    private async Task SendChatMessageAsync(string message)
    {
        // Find chat input (could be textarea or input)
        var input = _page!.GetByTestId("chat-input")
            .Or(_page.Locator("textarea").Last)
            .Or(_page.Locator("input[type='text']").Last);

        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await input.FillAsync(message);

        // Find and click Send button
        var sendBtn = _page.GetByTestId("chat-send")
            .Or(_page.Locator("button:has-text('Send')").First)
            .Or(_page.Locator("button[type='submit']").First);

        await sendBtn.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        await sendBtn.ClickAsync();
    }

    /// <summary>
    /// Waits for the tool approval card to appear.
    /// Returns the approval card locator or throws TimeoutException.
    /// </summary>
    private async Task<ILocator> WaitForToolApprovalCardAsync(TimeSpan timeout)
    {
        var approvalCard = _page!.Locator("[data-testid='tool-approval-card']")
            .Or(_page.Locator(".tool-approval-card"))
            .Or(_page.Locator("text=/approval.*required/i").Locator("xpath=ancestor::div[1]"))
            .First;

        await approvalCard.WaitForAsync(new LocatorWaitForOptions 
        { 
            Timeout = (float)timeout.TotalMilliseconds 
        });

        return approvalCard;
    }

    /// <summary>
    /// Clicks the Approve button in the tool approval card.
    /// </summary>
    private async Task ClickApproveButtonAsync(ILocator approvalCard)
    {
        var approveBtn = approvalCard.Locator("button:has-text('Approve')")
            .Or(approvalCard.Locator("button[data-testid='approve-button']"))
            .First;

        await approveBtn.ClickAsync();

        // Wait for approval card to disappear
        await approvalCard.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 10_000
        });
    }

    /// <summary>
    /// Waits for tool execution to complete by looking for tool result indicators.
    /// </summary>
    private async Task WaitForToolExecutionResultAsync(TimeSpan timeout)
    {
        // Wait for tool result element to appear
        // The UI should show tool execution results with [data-testid='tool-result']
        // or in the Agent Activity / message stream
        var toolResult = _page!.Locator("[data-testid='tool-result']")
            .Or(_page.Locator(".tool-result"))
            .Or(_page.Locator("text=/tool.*complete/i"))
            .Or(_page.Locator("text=/saved.*file/i"))
            .First;

        try
        {
            await toolResult.WaitForAsync(new LocatorWaitForOptions 
            { 
                Timeout = (float)timeout.TotalMilliseconds 
            });
        }
        catch (TimeoutException)
        {
            // Alternative: check if assistant message completed
            var assistantComplete = _page.Locator("[data-testid='assistant-message-complete']")
                .Or(_page.Locator(".assistant-message.complete"))
                .First;

            await assistantComplete.WaitForAsync(new LocatorWaitForOptions 
            { 
                Timeout = (float)timeout.TotalMilliseconds 
            });
        }
    }

    /// <summary>
    /// Verifies the result of the tool execution.
    /// Checks for:
    /// - No error toasts or error messages
    /// - Success indicators (file saved, markdown content, etc.)
    /// - Agent Activity log shows web_fetch executed
    /// </summary>
    private async Task VerifyResultAsync()
    {
        // 1. Check for error indicators (should be absent)
        var errorToasts = await _page!.Locator(".toast-error, [data-testid='error-toast'], .error-message")
            .CountAsync();
        Assert.Equal(0, errorToasts);

        // 2. Check page content for success indicators
        var pageContent = await _page.ContentAsync();
        
        // Should NOT contain error keywords
        Assert.DoesNotContain("failed", pageContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", pageContent, StringComparison.OrdinalIgnoreCase);
        
        // 3. Look for success indicators in the UI
        // Check if the assistant response mentions the file or completion
        var messages = await _page.Locator(".assistant-message, [data-role='assistant']")
            .AllInnerTextsAsync();
        
        var hasSuccessIndicator = messages.Any(m => 
            m.Contains("saved", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("created", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("file", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("markdown", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasSuccessIndicator, 
            "Expected assistant message to indicate successful file save or markdown conversion");

        // 4. Verify Agent Activity / tool execution log (if visible)
        // This may require expanding an Activity panel or checking a separate view
        // For now, we check if tool result elements are present
        var toolResults = await _page.Locator("[data-testid='tool-result'], .tool-result")
            .CountAsync();
        
        Assert.True(toolResults > 0, 
            "Expected at least one tool result indicator in the UI");

        _output.WriteLine($"Verification passed: {errorToasts} errors, {toolResults} tool results, success indicators found");
    }

    /// <summary>
    /// Captures a screenshot for debugging or documentation.
    /// </summary>
    private async Task CaptureScreenshotAsync(string suffix)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var screenshotDir = Path.Combine("TestResults", "screenshots");
            var filename = $"ToolApprovalE2E_{suffix}_{timestamp}.png";
            var fullPath = Path.Combine(screenshotDir, filename);

            Directory.CreateDirectory(screenshotDir);

            await _page!.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = fullPath,
                FullPage = true
            });

            _output.WriteLine($"Screenshot saved: {fullPath}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Failed to capture screenshot: {ex.Message}");
        }
    }
}

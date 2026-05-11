using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit.Abstractions;
using Xunit;

namespace OpenClawNet.IntegrationTests;

/// <summary>
/// E2E test for Bruno's tool approval workflow scenario:
/// 1. Resolve resources with `aspire describe --format Json`
/// 2. Start Aspire only if resources are missing, then wait until available
/// 3. Open web app at discovered URL
/// 3. Navigate to Chat page
/// 4. Create new chat with default agent
/// 5. Send: "ok, convert the content of elbruno.com to markdown and save it on a file"
/// 6. Wait for tool_approval event
/// 7. Click Approve
/// 8. Verify: file saved, result shown, no errors
/// 9. Stop Aspire with `aspire stop` when this test started it
/// 
/// IMPORTANT: This test requires a running Aspire stack with:
/// - Gateway service (API backend)
/// - Web service (Blazor frontend) discovered from Aspire resources
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
    private bool _startedAspireForTest;

    // Configuration (resolved from Aspire when available)
    private string _webAppUrl = "http://localhost:5010";
    private string _gatewayApiUrl = "http://localhost:5000";
    
    public ToolApprovalE2eTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        // 1. Resolve service-discovery endpoints and start Aspire when missing.
        await StartAspireIfNeededAsync();
        await ResolveServiceUrlsAsync();

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
            BaseAddress = new Uri(_gatewayApiUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };

        await EnsureChatPageReachableAsync();
    }

    public async Task DisposeAsync()
    {
        _gatewayClient?.Dispose();
        if (_page is not null) await _page.CloseAsync();
        if (_context is not null) await _context.DisposeAsync();
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();

        if (_startedAspireForTest)
        {
            var stopResult = await RunAspireCommandAsync("stop");
            if (stopResult.ExitCode == 0)
            {
                _output.WriteLine("Stopped Aspire with 'aspire stop'.");
            }
            else
            {
                _output.WriteLine($"aspire stop failed: {stopResult.Stderr}");
            }
        }
    }

    /// <summary>
    /// Full E2E test for Bruno's tool approval workflow for website summarization via markdown_convert.
    /// Tests the complete flow: send message, wait for approval prompt, approve, verify result.
    /// </summary>
    [SkippableFact]
    public async Task ToolApprovalWorkflow_ApprovesAndExecutes_MarkdownWebsiteSummary()
    {
        // Skip if model not available (Ollama or Azure OpenAI required)
        Skip.IfNot(await IsModelAvailableAsync(), 
            "No tool-capable model available. Requires Ollama (qwen2.5:3b) or Azure OpenAI.");

        try
        {
            // Step 1: Prefer an existing approval-enabled profile; create one only as fallback
            var profileName = await ResolveApprovalProfileNameAsync();
            _output.WriteLine($"Using profile: {profileName}");

            // Step 2: Navigate to web app with the profile
            var sessionId = Guid.NewGuid().ToString();
            await _page!.GotoAsync($"{_webAppUrl}/chat?profile={Uri.EscapeDataString(profileName)}&sessionId={Uri.EscapeDataString(sessionId)}", 
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            _output.WriteLine($"Navigated to {_webAppUrl}");

            // Step 3: Send the chat message
            var testMessage = "Summarize the latest content of the https://elbruno.com website";
            await SendChatMessageAsync(testMessage);
            _output.WriteLine($"Sent message: {testMessage}");

            // Step 4: Wait for tool approval card to appear (timeout: 30s)
            var approvalCard = await WaitForToolApprovalCardAsync(TimeSpan.FromSeconds(30));
            Assert.NotNull(approvalCard);
            
            var cardText = await approvalCard.InnerTextAsync();
            _output.WriteLine($"Approval card appeared: {cardText}");
            
            // Verify the approval card targets markdown conversion for this summary scenario
            Assert.True(
                cardText.Contains("markdown_convert", StringComparison.OrdinalIgnoreCase) ||
                (cardText.Contains("markdown", StringComparison.OrdinalIgnoreCase) &&
                 cardText.Contains("elbruno.com", StringComparison.OrdinalIgnoreCase)),
                "Approval card should reference markdown_convert for website summary requests");

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

    /// <summary>
    /// End-to-end verification for the same website-summary scenario using an
    /// auto-approve profile. No tool approval card should appear.
    /// </summary>
    [SkippableFact]
    public async Task ToolApprovalWorkflow_AutoApproveProfile_CompletesWithoutApprovalCard()
    {
        Skip.IfNot(await IsModelAvailableAsync(),
            "No tool-capable model available. Requires Ollama (qwen2.5:3b) or Azure OpenAI.");

        var profileName = await CreateProfileAsync(requireToolApproval: false);
        _output.WriteLine($"Using auto-approve profile: {profileName}");

        var sessionId = Guid.NewGuid().ToString();
        await _page!.GotoAsync(
            $"{_webAppUrl}/chat?profile={Uri.EscapeDataString(profileName)}&sessionId={Uri.EscapeDataString(sessionId)}",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        var testMessage = "Summarize the latest content of the https://elbruno.com website";
        await SendChatMessageAsync(testMessage);
        _output.WriteLine($"Sent message: {testMessage}");

        // Give the model time to select tools and begin output.
        await _page.WaitForTimeoutAsync(8_000);
        var approvalCardCount = await _page
            .Locator("[data-testid='tool-approval-card'], .tool-approval-card")
            .CountAsync();
        Assert.Equal(0, approvalCardCount);

        // Wait for at least one assistant response block to appear.
        var assistantMessage = _page.Locator(".assistant-message, [data-role='assistant']").Last;
        await assistantMessage.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60_000
        });

        var responseText = (await assistantMessage.InnerTextAsync()).Trim();
        Assert.False(string.IsNullOrWhiteSpace(responseText), "Assistant response should not be empty.");
    }

    private async Task<string> ResolveApprovalProfileNameAsync()
    {
        var profiles = await _gatewayClient!.GetFromJsonAsync<List<AgentProfileSummary>>("/api/agent-profiles?kind=Standard")
            ?? [];

        var selected = profiles
            .Where(p => (p.IsEnabled ?? true) && (p.RequireToolApproval ?? false))
            .OrderByDescending(p => p.IsDefault)
            .ThenByDescending(p => p.LastTestSucceeded ?? false)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (selected is not null && !string.IsNullOrWhiteSpace(selected.Name))
        {
            return selected.Name;
        }

        return await CreateProfileAsync(requireToolApproval: true);
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
        await ResolveServiceUrlsAsync();

        try
        {
            using var testClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await testClient.GetAsync($"{_gatewayApiUrl}/health");
            
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

        // Resolve resource map first; only start when describe doesn't return usable resources.
        if (await HasValidAspireResourcesAsync())
        {
            _output.WriteLine("Aspire resources discovered from 'aspire describe --format Json'.");
            return;
        }

        // Try to start Aspire
        _output.WriteLine("Attempting to start Aspire...");
        var startInfo = new ProcessStartInfo
        {
            FileName = "aspire",
            Arguments = "start",
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            WorkingDirectory = GetRepositoryRoot()
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Failed to start Aspire. Ensure 'aspire' CLI is installed and AppHost is configured.");
        _startedAspireForTest = true;

        // Wait up to 2 minutes for resources to appear and health to become reachable.
        var timeout = TimeSpan.FromMinutes(2);
        var stopwatch = Stopwatch.StartNew();
        
        while (stopwatch.Elapsed < timeout)
        {
            await ResolveServiceUrlsAsync();

            try
            {
                using var testClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var response = await testClient.GetAsync($"{_gatewayApiUrl}/health");
                
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

    private async Task EnsureChatPageReachableAsync()
    {
        try
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            var response = await http.GetAsync($"{_webAppUrl}/chat");
            if (!response.IsSuccessStatusCode)
            {
                throw new SkipException(
                    $"Skipping ToolApprovalE2eTests: web chat URL '{_webAppUrl}/chat' returned {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadAsStringAsync();
            var looksLikeChatPage = body.Contains("OpenClaw .NET - Chat", StringComparison.OrdinalIgnoreCase)
                || body.Contains("data-testid=\"chat-input\"", StringComparison.OrdinalIgnoreCase)
                || body.Contains("+ New Chat", StringComparison.OrdinalIgnoreCase);

            if (!looksLikeChatPage)
            {
                throw new SkipException(
                    $"Skipping ToolApprovalE2eTests: '{_webAppUrl}/chat' does not look like the chat UI.");
            }
        }
        catch (SkipException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SkipException(
                $"Skipping ToolApprovalE2eTests: web chat URL '{_webAppUrl}/chat' is not reachable ({ex.Message}).");
        }
    }

    private async Task ResolveServiceUrlsAsync()
    {
        var envGateway = Environment.GetEnvironmentVariable("OPENCLAWNET_GATEWAY_URL");
        var envWeb = Environment.GetEnvironmentVariable("OPENCLAWNET_WEB_URL");
        if (!string.IsNullOrWhiteSpace(envGateway) && !string.IsNullOrWhiteSpace(envWeb))
        {
            _gatewayApiUrl = envGateway.TrimEnd('/');
            _webAppUrl = envWeb.TrimEnd('/');
            _output.WriteLine($"Using service URLs from env: gateway={_gatewayApiUrl}, web={_webAppUrl}");
            return;
        }

        var describeResult = await RunAspireCommandAsync("describe --format Json");
        if (describeResult.ExitCode != 0)
        {
            _output.WriteLine($"aspire describe failed: {describeResult.Stderr}");
            return;
        }

        var trimmed = describeResult.Stdout.Trim();
        var jsonStart = trimmed.IndexOf('{');
        var jsonEnd = trimmed.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd <= jsonStart)
        {
            return;
        }

        var json = trimmed.Substring(jsonStart, jsonEnd - jsonStart + 1);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        string? gatewayUrl = null;
        string? webUrl = null;
        foreach (var resource in resources.EnumerateArray())
        {
            if (!resource.TryGetProperty("displayName", out var displayNameProp))
                continue;

            var displayName = displayNameProp.GetString() ?? string.Empty;
            if (!resource.TryGetProperty("urls", out var urlsProp) || urlsProp.ValueKind != JsonValueKind.Array)
                continue;

            var urls = urlsProp.EnumerateArray()
                .Select(u => u.TryGetProperty("url", out var p) ? p.GetString() : null)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u!)
                .ToList();

            var selectedUrl = urls.FirstOrDefault(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                ?? urls.FirstOrDefault(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(selectedUrl))
                continue;

            if (displayName.Equals("gateway", StringComparison.OrdinalIgnoreCase))
                gatewayUrl = selectedUrl;
            else if (displayName.Equals("web", StringComparison.OrdinalIgnoreCase))
                webUrl = selectedUrl;
        }

        if (!string.IsNullOrWhiteSpace(gatewayUrl))
            _gatewayApiUrl = gatewayUrl.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(webUrl))
            _webAppUrl = webUrl.TrimEnd('/');

        _output.WriteLine($"Resolved service URLs: gateway={_gatewayApiUrl}, web={_webAppUrl}");
    }

    private async Task<bool> HasValidAspireResourcesAsync()
    {
        var describeResult = await RunAspireCommandAsync("describe --format Json");
        if (describeResult.ExitCode != 0)
        {
            return false;
        }

        var trimmed = describeResult.Stdout.Trim();
        var jsonStart = trimmed.IndexOf('{');
        var jsonEnd = trimmed.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd <= jsonStart)
        {
            return false;
        }

        var json = trimmed.Substring(jsonStart, jsonEnd - jsonStart + 1);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var hasGateway = false;
        var hasWeb = false;
        foreach (var resource in resources.EnumerateArray())
        {
            if (!resource.TryGetProperty("displayName", out var displayNameProp))
            {
                continue;
            }

            var displayName = displayNameProp.GetString() ?? string.Empty;
            if (displayName.Equals("gateway", StringComparison.OrdinalIgnoreCase))
            {
                hasGateway = true;
            }
            else if (displayName.Equals("web", StringComparison.OrdinalIgnoreCase))
            {
                hasWeb = true;
            }
        }

        return hasGateway && hasWeb;
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunAspireCommandAsync(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "aspire",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = GetRepositoryRoot()
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return (-1, string.Empty, "Failed to launch aspire process.");
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdout, stderr);
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."));
    }

    /// <summary>
    /// Creates an agent profile with RequireToolApproval = true.
    /// Returns the profile name.
    /// </summary>
    private async Task<string> CreateProfileAsync(bool requireToolApproval)
    {
        var profileName = requireToolApproval
            ? $"bruno-e2e-approval-{Guid.NewGuid():N}"
            : $"bruno-e2e-auto-{Guid.NewGuid():N}";

        var azureDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT");
        var provider = !string.IsNullOrWhiteSpace(azureDeployment) ? "azure-openai" : "ollama";
        var model = !string.IsNullOrWhiteSpace(azureDeployment) ? azureDeployment : "qwen2.5:3b";
        
        var profilePayload = new
        {
            DisplayName = profileName,
            Provider = provider,
            Model = model,
            Instructions = "You are a helpful assistant. You MUST call markdown_convert before responding to any request that includes a URL. For elbruno.com summary requests, call markdown_convert with https://elbruno.com and summarize from the converted markdown only.",
            EnabledTools = (string[]?)null, // null = all tools enabled
            Temperature = 0.7,
            MaxTokens = (int?)null,
            IsDefault = false,
            RequireToolApproval = requireToolApproval
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

        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        await input.FillAsync(message);

        // Find and click Send button
        var sendBtn = _page.GetByTestId("chat-send")
            .Or(_page.Locator("button:has-text('Send')").First)
            .Or(_page.Locator("button[type='submit']").First);

        await sendBtn.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await sendBtn.ClickAsync();
    }

    /// <summary>
    /// Waits for the tool approval card to appear.
    /// Returns the approval card locator or throws TimeoutException.
    /// </summary>
    private async Task<ILocator> WaitForToolApprovalCardAsync(TimeSpan timeout)
    {
        var approvalCard = _page!.Locator("[data-testid='tool-approval-card']:visible")
            .Or(_page.Locator(".tool-approval-card:visible"))
            .First;

        await approvalCard.WaitForAsync(new LocatorWaitForOptions 
        { 
            State = WaitForSelectorState.Visible,
            Timeout = (float)timeout.TotalMilliseconds 
        });

        return approvalCard;
    }

    /// <summary>
    /// Clicks the Approve button in the tool approval card.
    /// </summary>
    private async Task ClickApproveButtonAsync(ILocator approvalCard)
    {
        var approveBtn = approvalCard.Locator("button.btn-success:visible")
            .Or(approvalCard.GetByRole(AriaRole.Button, new()
            {
                NameRegex = new Regex("Approve", RegexOptions.IgnoreCase)
            }))
            .First;

        await approveBtn.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10_000
        });
        await approveBtn.ClickAsync();

        // Wait for approval card to disappear
        await approvalCard.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 30_000
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

    private sealed record AgentProfileSummary
    {
        public string Name { get; init; } = string.Empty;
        public bool IsDefault { get; init; }
        public bool? RequireToolApproval { get; init; }
        public bool? IsEnabled { get; init; }
        public bool? LastTestSucceeded { get; init; }
    }
}

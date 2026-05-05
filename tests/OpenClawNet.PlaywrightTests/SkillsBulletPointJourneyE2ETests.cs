using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// Full user-journey E2E for bullet-point response skill:
///
///   1. Create a "bullet-point" skill via the Skills page
///   2. Enable it for the default chat agent
///   3. Open a NEW chat with that agent
///   4. Send a neutral question
///   5. Verify the assistant replies with bullet points (• or - or *) and numbered items
///   6. Clean up
///
/// This proves the skill ACTUALLY influences agent behavior end-to-end.
///
/// Live LLM required — skipped when neither Ollama tool-capable model nor
/// Azure OpenAI is configured.
///
/// Headed run:
///   $env:PLAYWRIGHT_HEADED="true"
///   dotnet test tests\OpenClawNet.PlaywrightTests --filter "FullyQualifiedName~SkillsBulletPointJourneyE2ETests"
/// </summary>
[Collection("AppHost")]
[Trait("Category", "Live")]
public class SkillsBulletPointJourneyE2ETests : PlaywrightTestBase
{
    private const string SkillName = "bullet-point-journey";
    private const string SkillDescription =
        "User-journey bullet-point skill. Forces the agent to always respond with structured bullet points.";
    private const string SkillBody = """
        # Bullet Point Response Mode (Journey E2E)

        You are a structured assistant that ALWAYS formats responses as bullet points.

        Every reply MUST follow these rules:

        - Start your response with the 📋 emoji
        - Use bullet points (• or - or *) for all key information
        - Use at least 3 bullet points per response
        - Keep each bullet point concise (1-2 sentences max)
        - Use nested bullets when explaining details
        - End your response with "✅ Formatted as requested"

        No matter what the user asks, format your answer using bullet points.
        Stay in structured format at all times.
        """;

    // Detection: the skill tells the model to start with 📋 and end with ✅
    // Also requires at least 3 bullet markers (•, -, or *)
    private static readonly Regex ClipboardEmoji = new("📋", RegexOptions.Compiled);
    private static readonly Regex CheckmarkEnd = new("✅.*Formatted as requested", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BulletMarkers = new(@"(?:^|\n)\s*[•\-\*]\s+", RegexOptions.Compiled | RegexOptions.Multiline);

    public SkillsBulletPointJourneyE2ETests(AppHostFixture fixture) : base(fixture)
    {
    }

    [SkippableFact]
    public async Task BulletPointSkill_AppliedToAgent_AgentRepliesWithBullets()
    {
        Skip.IfNot(Fixture.IsAnyToolCapableModelAvailable, Fixture.AnyToolCapableModelSkipReason);

        // ── Setup: discover the default chat agent profile ───────────────────
        string agentName;
        using (var http = Fixture.CreateGatewayHttpClient())
        {
            var profiles = await http.GetFromJsonAsync<List<AgentProfileDto>>(
                "/api/agent-profiles?kind=Standard");
            if (profiles is null || profiles.Count == 0)
            {
                throw new Xunit.Sdk.XunitException("No agent profiles available — cannot run journey.");
            }
            var profile = profiles.FirstOrDefault(p => p.IsDefault) ?? profiles[0];
            agentName = profile.Name;
        }

        await DeleteSkillIfExistsAsync(SkillName);

        await WithScreenshotOnFailure(async () =>
        {
            // ── 1. Create the skill via the Skills page ──────────────────────
            await LogStepAsync($"Step 1/5: Create '{SkillName}' skill via /skills");
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/skills", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            await Page.Locator("button:has-text('New skill')").First.ClickAsync();
            await Assertions.Expect(Page.Locator("[data-testid='skill-name']"))
                .ToBeVisibleAsync(new() { Timeout = 10_000 });

            await Page.Locator("[data-testid='skill-name']").FillAsync(SkillName);
            await Page.Locator("[data-testid='skill-description']").FillAsync(SkillDescription);
            await Page.Locator("[data-testid='skill-body']").FillAsync(SkillBody);
            await Page.Locator("[data-testid='skill-submit']").ClickAsync();

            await Assertions.Expect(Page.Locator("[data-testid='skill-name']"))
                .ToBeHiddenAsync(new() { Timeout = 15_000 });

            await WaitForWithTicksAsync(
                Page.Locator($"td code:has-text('{SkillName}')"),
                15_000,
                $"row for '{SkillName}'");

            // ── 2. Enable the skill for the default agent ────────────────────
            await LogStepAsync($"Step 2/5: Enable '{SkillName}' for agent '{agentName}'");
            var agentPicker = Page.Locator("[data-testid='skills-agent-picker']");
            await Assertions.Expect(agentPicker).ToBeVisibleAsync(new() { Timeout = 10_000 });

            var optionExists = await agentPicker
                .Locator($"option[value='{agentName}']").CountAsync();
            if (optionExists == 0)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Default agent '{agentName}' not in skills agent picker.");
            }
            await agentPicker.SelectOptionAsync(agentName);
            await Page.WaitForTimeoutAsync(500);

            var row = Page.Locator($"tr:has(td code:has-text('{SkillName}'))").First;
            var toggle = row.Locator("[data-testid='enabled-switch']");
            await Assertions.Expect(toggle).ToBeVisibleAsync(new() { Timeout = 10_000 });
            if (!await toggle.IsCheckedAsync())
            {
                await toggle.ClickAsync();
            }
            await Page.WaitForTimeoutAsync(2_000);

            // Verify via API that the skill is enabled for this agent.
            using (var http = Fixture.CreateGatewayHttpClient())
            {
                var apiSkill = await http.GetFromJsonAsync<SkillApiDto>($"/api/skills/{SkillName}");
                if (apiSkill is null
                    || !apiSkill.EnabledByAgent.TryGetValue(agentName, out var on)
                    || !on)
                {
                    throw new Xunit.Sdk.XunitException(
                        $"Skill '{SkillName}' is not enabled for agent '{agentName}' after toggle.");
                }
            }
            await LogStepAsync($"✅ Skill enabled for {agentName}");

            // ── 3. Open chat and explicitly start a NEW chat session ─────────
            await LogStepAsync($"Step 3/5: Open /chat?profile={agentName} and click '+ New Chat'");
            await Page.GotoAsync(
                $"{Fixture.WebBaseUrl}/chat?profile={Uri.EscapeDataString(agentName)}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });

            var newChatBtn = Page.Locator("button:has-text('New Chat')").First;
            await Assertions.Expect(newChatBtn).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await newChatBtn.ClickAsync();
            await Page.WaitForTimeoutAsync(500);

            var chatInput = Page.GetByTestId("chat-input");
            await chatInput.WaitForAsync(new() { Timeout = 15_000 });

            // ── 4. Send a neutral question ───────────────────────────────────
            const string userMessage =
                "Explain the benefits of test-driven development in software engineering.";
            await LogStepAsync($"Step 4/5: Sending message: \"{userMessage}\"");
            await chatInput.FillAsync(userMessage);

            var sendBtn = Page.GetByTestId("chat-send");
            await Assertions.Expect(sendBtn).ToBeEnabledAsync(new() { Timeout = 5_000 });
            await sendBtn.ClickAsync();

            // ── 5. Wait for assistant reply and assert bullet format ─────────
            await LogStepAsync("Step 5/5: Waiting for assistant reply (up to 120s)…");
            var assistantComplete = Page.Locator("[data-testid='assistant-message-complete']").First;
            await assistantComplete.WaitForAsync(new()
            {
                State = WaitForSelectorState.Attached,
                Timeout = 120_000
            });

            var assistantMessages = Page.Locator("[data-testid='assistant-message']");
            await Assertions.Expect(assistantMessages).Not.ToHaveCountAsync(0, new() { Timeout = 5_000 });
            var fullReply = await assistantMessages.Last.InnerTextAsync();

            await LogStepAsync($"Assistant reply: {Truncate(fullReply, 200)}");

            var hasClipboard = ClipboardEmoji.IsMatch(fullReply);
            var hasCheckmark = CheckmarkEnd.IsMatch(fullReply);
            var bulletMatches = BulletMarkers.Matches(fullReply);
            var bulletCount = bulletMatches.Count;

            if (!hasClipboard || !hasCheckmark || bulletCount < 3)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Bullet point format NOT detected in reply.\n" +
                    $"  📋 emoji present: {hasClipboard}\n" +
                    $"  ✅ checkmark present: {hasCheckmark}\n" +
                    $"  Bullet markers found: {bulletCount} (need ≥3)\n" +
                    $"  Reply: {fullReply}");
            }

            await LogStepAsync(
                $"📋 Bullet format detected (clipboard={hasClipboard}, checkmark={hasCheckmark}, bullets={bulletCount}). Skill is working end-to-end!");
        });

        // ── Cleanup ──────────────────────────────────────────────────────────
        await DeleteSkillIfExistsAsync(SkillName);
    }

    private async Task DeleteSkillIfExistsAsync(string name)
    {
        try
        {
            using var http = Fixture.CreateGatewayHttpClient();
            using var resp = await http.DeleteAsync($"/api/skills/{name}");
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "(empty)" : (s.Length <= max ? s : s[..max] + "…");

    private sealed record SkillApiDto(string Name, Dictionary<string, bool> EnabledByAgent);

    private sealed record AgentProfileDto(string Name, string? DisplayName, string? Provider, bool IsDefault);
}

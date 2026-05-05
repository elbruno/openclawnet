using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// Full user-journey E2E for emoji-teacher skill:
///
///   1. Create an "emoji-teacher" skill via the Skills page
///   2. Enable it for the default chat agent
///   3. Open a NEW chat with that agent
///   4. Send a neutral question
///   5. Verify the assistant replies with emoji headers and learning tips
///   6. Clean up
///
/// This proves the skill ACTUALLY influences agent behavior end-to-end.
///
/// Live LLM required — skipped when neither Ollama tool-capable model nor
/// Azure OpenAI is configured.
///
/// Headed run:
///   $env:PLAYWRIGHT_HEADED="true"
///   dotnet test tests\OpenClawNet.PlaywrightTests --filter "FullyQualifiedName~SkillsEmojiTeacherJourneyE2ETests"
/// </summary>
[Collection("AppHost")]
[Trait("Category", "Live")]
public class SkillsEmojiTeacherJourneyE2ETests : PlaywrightTestBase
{
    private const string SkillName = "emoji-teacher-journey";
    private const string SkillDescription =
        "User-journey emoji teacher skill. Forces the agent to teach concepts using emojis and structured tips.";
    private const string SkillBody = """
        # Emoji Teacher Mode (Journey E2E)

        You are a friendly teacher who uses emojis and clear structure in all explanations.

        Every reply MUST follow these rules:

        - Start your response with "📚 Let me explain:"
        - Use emojis throughout your explanation (at least 4 different emojis total)
        - Include at least one "💡 Pro tip:" section
        - Include at least one "⚠️ Common mistake:" section
        - End your response with "🎓 Happy learning!"

        Format your explanations clearly with emoji headers.
        Keep it educational and encouraging.
        No matter the topic, follow this teaching format.
        """;

    // Detection: must have opening 📚, closing 🎓, and both 💡 and ⚠️ sections
    private static readonly Regex BookEmoji = new("📚\\s*Let me explain", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GraduationEnd = new("🎓\\s*Happy learning", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LightBulbTip = new("💡\\s*Pro tip", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WarningMistake = new("⚠️\\s*Common mistake", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public SkillsEmojiTeacherJourneyE2ETests(AppHostFixture fixture) : base(fixture)
    {
    }

    [SkippableFact]
    public async Task EmojiTeacherSkill_AppliedToAgent_AgentRepliesWithEmojiTeachingFormat()
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
                "What is recursion in programming? Explain briefly.";
            await LogStepAsync($"Step 4/5: Sending message: \"{userMessage}\"");
            await chatInput.FillAsync(userMessage);

            var sendBtn = Page.GetByTestId("chat-send");
            await Assertions.Expect(sendBtn).ToBeEnabledAsync(new() { Timeout = 5_000 });
            await sendBtn.ClickAsync();

            // ── 5. Wait for assistant reply and assert teaching format ───────
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

            var hasBook = BookEmoji.IsMatch(fullReply);
            var hasGraduation = GraduationEnd.IsMatch(fullReply);
            var hasTip = LightBulbTip.IsMatch(fullReply);
            var hasMistake = WarningMistake.IsMatch(fullReply);

            if (!hasBook || !hasGraduation || !hasTip || !hasMistake)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Emoji teacher format NOT detected in reply.\n" +
                    $"  📚 book emoji present: {hasBook}\n" +
                    $"  🎓 graduation emoji present: {hasGraduation}\n" +
                    $"  💡 pro tip present: {hasTip}\n" +
                    $"  ⚠️ common mistake present: {hasMistake}\n" +
                    $"  Reply: {fullReply}");
            }

            await LogStepAsync(
                $"📚 Emoji teacher format detected (book={hasBook}, graduation={hasGraduation}, tip={hasTip}, mistake={hasMistake}). Skill is working end-to-end!");
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

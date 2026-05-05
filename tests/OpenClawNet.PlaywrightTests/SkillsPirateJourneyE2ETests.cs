using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// Full user-journey E2E for skills:
///
///   1. Create a "pirate-mode" skill via the Skills page
///   2. Enable it for the default chat agent
///   3. Open a NEW chat with that agent
///   4. Send a neutral question
///   5. Verify the assistant replies in pirate dialect (🏴‍☠️ or pirate words)
///   6. Clean up
///
/// This complements <see cref="SkillsPirateModeE2ETests"/> which focuses on the
/// CRUD lifecycle + four UX bug fixes; this one proves the skill ACTUALLY
/// influences agent behavior end-to-end.
///
/// Live LLM required — skipped when neither Ollama tool-capable model nor
/// Azure OpenAI is configured.
///
/// Headed run:
///   $env:PLAYWRIGHT_HEADED="true"
///   dotnet test tests\OpenClawNet.PlaywrightTests --filter "FullyQualifiedName~SkillsPirateJourneyE2ETests"
/// </summary>
[Collection("AppHost")]
[Trait("Category", "Live")]
public class SkillsPirateJourneyE2ETests : PlaywrightTestBase
{
    private const string SkillName = "pirate-mode-journey";
    private const string SkillDescription =
        "User-journey pirate dialect skill. Forces the agent to talk like a pirate and end every reply with 🏴‍☠️.";
    private const string SkillBody = """
        # Pirate Mode (Journey E2E)

        You are a salty pirate captain. Every reply MUST:

        - Use heavy pirate dialect: arr, matey, ye, ahoy, plunder, booty
        - End with the emoji 🏴‍☠️
        - Be brief (1-3 sentences) and seafaring in tone

        Even for math, weather, or trivia — answer in pirate.
        Stay in character at all times. No matter what the user asks,
        you reply as a pirate.
        """;

    // Detection: the skill explicitly tells the model to end every reply with 🏴‍☠️,
    // so the emoji is the strongest signal. Also accept ≥2 pirate keywords as
    // backup in case the model swaps the emoji for plain text.
    private static readonly Regex PirateEmoji = new("🏴‍☠️", RegexOptions.Compiled);
    private static readonly Regex PirateKeyword = new(
        @"\b(arr+|matey|ahoy|plunder|booty|landlubber|scallywag|seafarin'?|seafaring|treasure|cap'?n|aye)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public SkillsPirateJourneyE2ETests(AppHostFixture fixture) : base(fixture)
    {
    }

    [SkippableFact]
    public async Task PirateSkill_AppliedToAgent_AgentRepliesInPirate()
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
            // Prefer the default profile; if none flagged, take the first.
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

            // Make sure the default agent is one of the picker's options.
            var optionExists = await agentPicker
                .Locator($"option[value='{agentName}']").CountAsync();
            if (optionExists == 0)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Default agent '{agentName}' not in skills agent picker. " +
                    $"Picker may filter out profiles without explicit AgentName.");
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

            // Click "+ New Chat" so the conversation starts fresh — guarantees
            // the skill is bound at the start of this turn (Q2 next-turn rule).
            var newChatBtn = Page.Locator("button:has-text('New Chat')").First;
            await Assertions.Expect(newChatBtn).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await newChatBtn.ClickAsync();
            await Page.WaitForTimeoutAsync(500);

            var chatInput = Page.GetByTestId("chat-input");
            await chatInput.WaitForAsync(new() { Timeout = 15_000 });

            // ── 4. Send a neutral question ───────────────────────────────────
            const string userMessage =
                "What is the result of two plus two? Reply with one short sentence.";
            await LogStepAsync($"Step 4/5: Sending message: \"{userMessage}\"");
            await chatInput.FillAsync(userMessage);

            var sendBtn = Page.GetByTestId("chat-send");
            await Assertions.Expect(sendBtn).ToBeEnabledAsync(new() { Timeout = 5_000 });
            await sendBtn.ClickAsync();

            // ── 5. Wait for assistant reply and assert pirate dialect ────────
            await LogStepAsync("Step 5/5: Waiting for assistant reply (up to 120s)…");
            // The sentinel <span> is rendered with the `hidden` HTML attribute,
            // so default state="visible" never matches. Wait for it to be ATTACHED
            // to the DOM instead — that's the actual completion signal.
            var assistantComplete = Page.Locator("[data-testid='assistant-message-complete']").First;
            await assistantComplete.WaitForAsync(new()
            {
                State = WaitForSelectorState.Attached,
                Timeout = 120_000
            });

            // Read the LAST assistant message bubble via the new data-testid.
            var assistantMessages = Page.Locator("[data-testid='assistant-message']");
            await Assertions.Expect(assistantMessages).Not.ToHaveCountAsync(0, new() { Timeout = 5_000 });
            var fullReply = await assistantMessages.Last.InnerTextAsync();

            await LogStepAsync($"Assistant reply: {Truncate(fullReply, 200)}");

            var hasEmoji = PirateEmoji.IsMatch(fullReply);
            var keywordHits = PirateKeyword.Matches(fullReply).Count;

            if (!hasEmoji && keywordHits < 2)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Pirate dialect NOT detected in reply.\n" +
                    $"  Emoji 🏴‍☠️ present: {hasEmoji}\n" +
                    $"  Pirate keyword hits: {keywordHits} (need ≥2 if no emoji)\n" +
                    $"  Reply: {fullReply}");
            }

            await LogStepAsync(
                $"🏴‍☠️ Pirate dialect detected (emoji={hasEmoji}, keywords={keywordHits}). Skill is working end-to-end!");
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

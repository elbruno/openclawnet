using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace OpenClawNet.PlaywrightTests.Demos;

/// <summary>
/// ⚠️ DEMO-ONLY E2E TEST — NOT FOR CI OR REGRESSION TESTING ⚠️
///
/// This is the "Aspire already running" twin of <see cref="SkillsPirateJourneyE2ETests"/>.
/// Same user journey, different infrastructure assumption.
///
/// ┌──────────────────────────────────────────────────────────────────────────────┐
/// │                         THIS TEST vs. THE IN-PROCESS TEST                     │
/// ├──────────────────────────────────────────────────────────────────────────────┤
/// │ SkillsPirateJourneyE2ETests (parent folder)                                  │
/// │   • Boots Aspire in-process via DistributedApplicationTestingBuilder         │
/// │   • ~30–60s cold start per test run                                          │
/// │   • Hides the Aspire dashboard (no UI to show audience)                      │
/// │   • Perfect for CI, regression coverage, automated validation                │
/// │                                                                                │
/// │ PirateJourneyAttachedTests (this file, Demos/ folder)                        │
/// │   • ATTACHES to already-running `aspire start` instance                      │
/// │   • ~2–3s attach time (no Aspire boot)                                       │
/// │   • Dashboard stays visible throughout (perfect for live demos)              │
/// │   • ALWAYS headed + SlowMo (voice-over friendly)                             │
/// │   • Excluded from CI via [Trait("Category", "DemoLive")]                     │
/// └──────────────────────────────────────────────────────────────────────────────┘
///
/// ┌──────────────────────────────────────────────────────────────────────────────┐
/// │                           WHEN TO USE THIS TEST                               │
/// ├──────────────────────────────────────────────────────────────────────────────┤
/// │ ✅ Live conference demos                                                     │
/// │ ✅ Voice-over recording for slide decks                                      │
/// │ ✅ Fast iteration during presenter rehearsal                                 │
/// │ ✅ Any scenario where the audience needs to see the Aspire dashboard         │
/// └──────────────────────────────────────────────────────────────────────────────┘
///
/// ┌──────────────────────────────────────────────────────────────────────────────┐
/// │                         WHEN NOT TO USE THIS TEST                             │
/// ├──────────────────────────────────────────────────────────────────────────────┤
/// │ ❌ CI/CD pipelines       — use SkillsPirateJourneyE2ETests instead          │
/// │ ❌ Regression testing    — use SkillsPirateJourneyE2ETests instead          │
/// │ ❌ Automated validation  — use SkillsPirateJourneyE2ETests instead          │
/// └──────────────────────────────────────────────────────────────────────────────┘
///
/// ┌──────────────────────────────────────────────────────────────────────────────┐
/// │                            HOW TO RUN THIS TEST                               │
/// ├──────────────────────────────────────────────────────────────────────────────┤
/// │ Terminal 1 (start Aspire):                                                   │
/// │   aspire start src\OpenClawNet.AppHost                                       │
/// │   # Wait for green health checks + dashboard (http://localhost:15178)        │
/// │                                                                                │
/// │ Terminal 2 (run the test):                                                   │
/// │   $env:NUGET_PACKAGES = "$env:USERPROFILE\.nuget\packages2"                │
/// │   $env:PLAYWRIGHT_HEADED = "true"                                           │
/// │   $env:PLAYWRIGHT_SLOWMO = "1500"  # 800=fast, 1500=default, 2500=slow     │
/// │                                                                                │
/// │   # Optional: override URLs if your ports differ                             │
/// │   # $env:OPENCLAW_WEB_URL = "https://localhost:7294"                        │
/// │   # $env:OPENCLAW_GATEWAY_URL = "https://localhost:7067"                    │
/// │                                                                                │
/// │   dotnet test tests\OpenClawNet.PlaywrightTests `                           │
/// │     --filter "Category=DemoLive&FullyQualifiedName~PirateJourneyAttachedTests" │
/// └──────────────────────────────────────────────────────────────────────────────┘
///
/// ┌──────────────────────────────────────────────────────────────────────────────┐
/// │                              TRAIT CONVENTION                                 │
/// ├──────────────────────────────────────────────────────────────────────────────┤
/// │ [Trait("Category", "DemoLive")] — excludes from default CI runs:            │
/// │   dotnet test --filter "Category!=Live"                                      │
/// │                                                                                │
/// │ Demo runs explicitly opt-in:                                                 │
/// │   dotnet test --filter "Category=DemoLive"                                   │
/// └──────────────────────────────────────────────────────────────────────────────┘
///
/// <seealso cref="SkillsPirateJourneyE2ETests"/> — In-process twin for CI/regression
/// <seealso cref="AttachedAspireTestBase"/> — Base class with full documentation
/// </summary>
[Trait("Category", "DemoLive")]
public class PirateJourneyAttachedTests : AttachedAspireTestBase
{
    // Use a timestamped skill name to avoid cross-run state pollution since the
    // SQLite DB is shared across runs when attaching to a persistent Aspire instance.
    private readonly string _skillName = $"pirate-mode-demo-{DateTime.UtcNow:yyyyMMddHHmmss}";

    private const string SkillDescription =
        "Demo-only pirate dialect skill. Forces the agent to talk like a pirate and end every reply with 🏴‍☠️.";

    private const string SkillBody = """
        # Pirate Mode (Demo — Attached Aspire)

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

    [Fact]
    public async Task PirateSkill_AppliedToAgent_AgentRepliesInPirate()
    {
        // Note: No Skip.IfNot check here — this is demo-only. If Aspire/LLM isn't
        // ready, the test should fail loud so the presenter knows before going on stage.

        // ── Setup: discover the default chat agent profile ───────────────────
        string agentName;
        using (var http = CreateGatewayHttpClient())
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

        // Idempotent cleanup: if the skill already exists from a prior demo, delete it first.
        await DeleteSkillIfExistsAsync(_skillName);

        await WithScreenshotOnFailure(async () =>
        {
            // ── 1. Create the skill via the Skills page ──────────────────────
            await LogStepAsync($"Step 1/5: Create '{_skillName}' skill via /skills");
            await Page.GotoAsync($"{WebBaseUrl}/skills", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            await Page.Locator("button:has-text('New skill')").First.ClickAsync();
            await Assertions.Expect(Page.Locator("[data-testid='skill-name']"))
                .ToBeVisibleAsync(new() { Timeout = 10_000 });

            await Page.Locator("[data-testid='skill-name']").FillAsync(_skillName);
            await Page.Locator("[data-testid='skill-description']").FillAsync(SkillDescription);
            await Page.Locator("[data-testid='skill-body']").FillAsync(SkillBody);
            await Page.Locator("[data-testid='skill-submit']").ClickAsync();

            await Assertions.Expect(Page.Locator("[data-testid='skill-name']"))
                .ToBeHiddenAsync(new() { Timeout = 15_000 });

            await WaitForWithTicksAsync(
                Page.Locator($"td code:has-text('{_skillName}')"),
                15_000,
                $"row for '{_skillName}'");

            // ── 2. Enable the skill for the default agent ────────────────────
            await LogStepAsync($"Step 2/5: Enable '{_skillName}' for agent '{agentName}'");
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

            var row = Page.Locator($"tr:has(td code:has-text('{_skillName}'))").First;
            var toggle = row.Locator("[data-testid='enabled-switch']");
            await Assertions.Expect(toggle).ToBeVisibleAsync(new() { Timeout = 10_000 });
            if (!await toggle.IsCheckedAsync())
            {
                await toggle.ClickAsync();
            }
            await Page.WaitForTimeoutAsync(2_000);

            // Verify via API that the skill is enabled for this agent.
            using (var http = CreateGatewayHttpClient())
            {
                var apiSkill = await http.GetFromJsonAsync<SkillApiDto>($"/api/skills/{_skillName}");
                if (apiSkill is null
                    || !apiSkill.EnabledByAgent.TryGetValue(agentName, out var on)
                    || !on)
                {
                    throw new Xunit.Sdk.XunitException(
                        $"Skill '{_skillName}' is not enabled for agent '{agentName}' after toggle.");
                }
            }
            await LogStepAsync($"✅ Skill enabled for {agentName}");

            // ── 3. Open chat and explicitly start a NEW chat session ─────────
            await LogStepAsync($"Step 3/5: Open /chat?profile={agentName} and click '+ New Chat'");
            await Page.GotoAsync(
                $"{WebBaseUrl}/chat?profile={Uri.EscapeDataString(agentName)}",
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
        await DeleteSkillIfExistsAsync(_skillName);
    }

    private async Task DeleteSkillIfExistsAsync(string name)
    {
        try
        {
            using var http = CreateGatewayHttpClient();
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

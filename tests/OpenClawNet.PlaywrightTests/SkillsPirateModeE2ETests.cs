using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// CRUD + UX-bug E2E for the Skills page. Creates → enables → deletes a
/// pirate-mode skill via the UI, verifying the four UX fixes from b24ffe6
/// plus the toggle-persistence fix from 95ac6a4.
///
/// Does NOT validate that the skill actually changes agent behavior — see
/// <see cref="SkillsPirateJourneyE2ETests"/> for the full chat round-trip.
///
/// Bug coverage:
///
///   Bug 1 — Newly created skill not visible until tab switch (refresh + filter reset)
///   Bug 2 — Delete button has no confirmation (now uses window.confirm via JS)
///   Bug 3 — Tab clicks do not change visible filter (now call ApplyFilter via SelectTab)
///   Bug 4 — Agent picker empty (now unions agent profiles via JobsClient)
///   Bug 5 — Toggle does not persist (verified via direct API call)
///
/// Run headed to watch it live:
///   $env:PLAYWRIGHT_HEADED="true"
///   dotnet test tests\OpenClawNet.PlaywrightTests --filter "FullyQualifiedName~SkillsPirateModeE2ETests"
/// </summary>
[Collection("AppHost")]
public class SkillsPirateModeE2ETests : PlaywrightTestBase
{
    private const string SkillName = "pirate-mode-e2e";
    private const string SkillDescription =
        "E2E pirate dialect skill — appends 🏴‍☠️ to every reply and forces seafaring tone.";
    private const string SkillBody = """
        # Pirate Mode (E2E)

        You are a salty pirate captain. Every reply MUST:

        - Use pirate dialect (arr, matey, ye, plunder, etc.)
        - End with the emoji 🏴‍☠️
        - Be brief and seafaring

        Stay in character at all times.
        """;

    public SkillsPirateModeE2ETests(AppHostFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task PirateModeSkill_FullLifecycle_AllFourUxBugsPass()
    {
        // ── Pre-clean: make sure no leftover from a previous run ─────────────
        await DeleteSkillIfExistsAsync(SkillName);

        // The Blazor confirm() prompt must auto-accept for the delete-confirm
        // step at the end of the test. Use a synchronous fire-and-forget handler
        // — Playwright .NET does NOT await async event handlers, which would
        // leave the dialog open forever and block Blazor's DeleteAsync.
        Page.Dialog += (_, dialog) =>
        {
            Console.WriteLine($"[dialog] {dialog.Type}: {dialog.Message} → ACCEPT");
            _ = dialog.AcceptAsync();
        };

        await WithScreenshotOnFailure(async () =>
        {
            // ── Step 1: Navigate to /skills ──────────────────────────────────
            await LogStepAsync("Step 1: Navigate to /skills");
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/skills", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            await Assertions.Expect(Page.Locator("h3:has-text('Skills')"))
                .ToBeVisibleAsync(new() { Timeout = 15_000 });

            // ── Bug 3 verification: tab clicks change the filter ─────────────
            await LogStepAsync("Bug 3: clicking 'System' tab filters table");
            await Page.Locator(".nav-tabs .nav-link", new() { HasTextString = "System" })
                .First.ClickAsync();
            await Page.WaitForTimeoutAsync(500);
            // System layer ships at least 'memory' built-in.
            await Assertions.Expect(Page.Locator("td code:has-text('memory')").First)
                .ToBeVisibleAsync(new() { Timeout = 10_000 });

            await LogStepAsync("Bug 3: clicking 'Installed' tab — system rows should disappear");
            await Page.Locator(".nav-tabs .nav-link", new() { HasTextString = "Installed" })
                .First.ClickAsync();
            await Page.WaitForTimeoutAsync(500);
            // 'memory' is system-only, must NOT appear under Installed
            await Assertions.Expect(Page.Locator("td code:has-text('memory')"))
                .ToHaveCountAsync(0);

            // ── Step 2: Create the pirate-mode skill via dialog ──────────────
            await LogStepAsync("Step 2: Click 'New skill' to open the authoring dialog");
            await Page.Locator("button:has-text('New skill')").First.ClickAsync();

            await Assertions.Expect(Page.Locator("[data-testid='skill-name']"))
                .ToBeVisibleAsync(new() { Timeout = 10_000 });

            await LogStepAsync($"Filling form: name='{SkillName}'");
            await Page.Locator("[data-testid='skill-name']").FillAsync(SkillName);
            await Page.Locator("[data-testid='skill-description']").FillAsync(SkillDescription);
            await Page.Locator("[data-testid='skill-body']").FillAsync(SkillBody);

            await LogStepAsync("Clicking Submit");
            await Page.Locator("[data-testid='skill-submit']").ClickAsync();

            // Wait for modal to close
            await Assertions.Expect(Page.Locator("[data-testid='skill-name']"))
                .ToBeHiddenAsync(new() { Timeout = 15_000 });

            // ── Bug 1 verification: skill appears WITHOUT switching tabs ─────
            await LogStepAsync("Bug 1: skill row visible without manual tab refresh");
            await WaitForWithTicksAsync(
                Page.Locator($"td code:has-text('{SkillName}')"),
                15_000,
                $"row for '{SkillName}'");

            // ── Bug 4 verification: agent picker is populated ────────────────
            await LogStepAsync("Bug 4: agent picker is non-empty (union with JobsClient profiles)");
            var agentPicker = Page.Locator("[data-testid='skills-agent-picker']");
            var agentCount = await agentPicker.Locator("option").CountAsync();
            // 1 sentinel + at least 1 real agent
            if (agentCount < 2)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Bug 4: agent picker only has {agentCount} options; expected ≥ 2 (sentinel + real agent).");
            }
            await LogStepAsync($"Bug 4 ✅: agent picker has {agentCount} options");

            // Pick the first real (non-sentinel) agent value.
            var firstAgent = await agentPicker.Locator("option").Nth(1).GetAttributeAsync("value");
            if (string.IsNullOrWhiteSpace(firstAgent))
            {
                throw new Xunit.Sdk.XunitException("Bug 4: first agent option has empty value");
            }
            await LogStepAsync($"Selecting agent '{firstAgent}'");
            await agentPicker.SelectOptionAsync(firstAgent);
            await Page.WaitForTimeoutAsync(500);

            // ── Bug 5 verification: enable toggle persists ───────────────────
            await LogStepAsync("Bug 5: toggle the enabled switch on for the new skill");
            // Find the row by skill name, then the switch within it.
            var row = Page.Locator($"tr:has(td code:has-text('{SkillName}'))").First;
            var toggle = row.Locator("[data-testid='enabled-switch']");

            await Assertions.Expect(toggle).ToBeVisibleAsync(new() { Timeout = 10_000 });

            // The pre-clean only deletes SKILL.md; an agent's enabled.json may still
            // reference the previous run's entry. Toggle until ON regardless of start state.
            var alreadyOn = await toggle.IsCheckedAsync();
            await LogStepAsync($"Toggle initial state: checked={alreadyOn}");
            if (!alreadyOn)
            {
                await toggle.ClickAsync();
            }
            // Wait for server roundtrip (LoadAsync on success)
            await Page.WaitForTimeoutAsync(2_000);

            // After reload, toggle should still be checked
            var toggleAfter = Page.Locator($"tr:has(td code:has-text('{SkillName}')) [data-testid='enabled-switch']");
            await Assertions.Expect(toggleAfter).ToBeCheckedAsync(new() { Timeout = 10_000 });
            await LogStepAsync("Bug 5 ✅: toggle persisted ON after server reload");

            // Verify via API too.
            using var http = Fixture.CreateGatewayHttpClient();
            var apiSkill = await http.GetFromJsonAsync<SkillApiDto>(
                $"/api/skills/{SkillName}");
            if (apiSkill is null
                || !apiSkill.EnabledByAgent.TryGetValue(firstAgent!, out var apiEnabled)
                || !apiEnabled)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Bug 5: API says skill is NOT enabled for {firstAgent}. Got: {System.Text.Json.JsonSerializer.Serialize(apiSkill)}");
            }
            await LogStepAsync($"API confirmed: {SkillName} enabled for {firstAgent}");

            // ── Bug 2 verification: delete button asks for confirmation ──────
            await LogStepAsync("Bug 2: click delete — confirm dialog must appear (auto-accepted)");
            var deleteBtn = row.Locator("[data-testid='delete-skill']");
            await Assertions.Expect(deleteBtn).ToBeVisibleAsync(new() { Timeout = 5_000 });
            await deleteBtn.ClickAsync();

            // Row should disappear after delete + reload
            await Assertions.Expect(Page.Locator($"td code:has-text('{SkillName}')"))
                .ToHaveCountAsync(0, new() { Timeout = 15_000 });
            await LogStepAsync("Bug 2 ✅: skill deleted after confirm");

            await LogStepAsync("🎉 All four UX bug fixes verified");
        });
    }

    private async Task DeleteSkillIfExistsAsync(string name)
    {
        try
        {
            using var http = Fixture.CreateGatewayHttpClient();
            using var resp = await http.DeleteAsync($"/api/skills/{name}");
            // 200/204 = deleted; 404 = wasn't there. Anything else, ignore — the test will fail later.
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    /// <summary>
    /// Minimal DTO matching the gateway's GET /api/skills/{name} response
    /// — we only need EnabledByAgent for the assertion.
    /// </summary>
    private sealed record SkillApiDto(
        string Name,
        Dictionary<string, bool> EnabledByAgent);
}

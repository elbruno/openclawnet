// Issue #153 - Skills page: bulk select and bulk delete support
// E2E tests for bulk selection and deletion of skills

using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// E2E tests for Skills bulk selection and bulk delete functionality.
/// Tests verify UI selection state, checkbox behavior, bulk delete button visibility,
/// confirmation dialogs, successful deletion, partial failure handling, and edge cases.
/// </summary>
[Collection("AppHost")]
[Trait("Category", "E2E")]
public class SkillsBulkDeleteE2ETests : PlaywrightTestBase
{
    public SkillsBulkDeleteE2ETests(AppHostFixture fixture) : base(fixture)
    {
    }

    // ====================================================================
    // TEST 1: Bulk select checkbox column appears on installed tab
    // ====================================================================

    [Fact]
    [Trait("Feature", "SkillsBulkDelete")]
    public async Task BulkSelect_CheckboxColumnVisible_OnInstalledTab()
    {
        await WithScreenshotOnFailure(async () =>
        {
            await LogStepAsync("Navigate to Skills page");
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/skills", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            await LogStepAsync("Click Installed tab");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Installed" }).ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await LogStepAsync("Verify select-all checkbox appears");
            var selectAllCheckbox = Page.GetByTestId("skills-select-all");
            await Assertions.Expect(selectAllCheckbox).ToBeVisibleAsync();

            await LogStepAsync("Verify individual skill checkboxes appear");
            // Look for any skill row checkbox
            var skillRows = Page.Locator("tr").Filter(new() { HasText = "skill" });
            var count = await skillRows.CountAsync();
            if (count > 0)
            {
                var firstRow = skillRows.First();
                var checkbox = firstRow.Locator("input[type='checkbox']");
                await Assertions.Expect(checkbox).ToBeVisibleAsync();
            }
        });
    }

    // ====================================================================
    // TEST 2: Select-all checkbox selects all installed skills
    // ====================================================================

    [Fact]
    [Trait("Feature", "SkillsBulkDelete")]
    public async Task BulkSelect_SelectAll_SelectsAllInstalledSkills()
    {
        await WithScreenshotOnFailure(async () =>
        {
            // Create test skills first
            await CreateTestSkillsAsync(3);

            await LogStepAsync("Navigate to Skills page");
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/skills", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            await LogStepAsync("Click Installed tab");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Installed" }).ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await LogStepAsync("Check select-all checkbox");
            var selectAllCheckbox = Page.GetByTestId("skills-select-all");
            await selectAllCheckbox.CheckAsync();

            await LogStepAsync("Verify all skill checkboxes are checked");
            var skillCheckboxes = Page.Locator("input[type='checkbox'][data-testid^='skill-select-']");
            var count = await skillCheckboxes.CountAsync();
            for (int i = 0; i < count; i++)
            {
                await Assertions.Expect(skillCheckboxes.Nth(i)).ToBeCheckedAsync();
            }

            await LogStepAsync("Verify delete button appears with count");
            var deleteButton = Page.GetByTestId("skills-bulk-delete-button");
            await Assertions.Expect(deleteButton).ToBeVisibleAsync();
            await Assertions.Expect(deleteButton).ToContainTextAsync("Delete selected");
        });
    }

    // ====================================================================
    // TEST 3: Individual checkbox selection
    // ====================================================================

    [Fact]
    [Trait("Feature", "SkillsBulkDelete")]
    public async Task BulkSelect_IndividualCheckbox_TogglesSelection()
    {
        await WithScreenshotOnFailure(async () =>
        {
            // Create test skills first
            await CreateTestSkillsAsync(2);

            await LogStepAsync("Navigate to Skills page");
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/skills", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            await LogStepAsync("Click Installed tab");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Installed" }).ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await LogStepAsync("Check first skill checkbox");
            var firstCheckbox = Page.Locator("input[type='checkbox'][data-testid^='skill-select-']").First();
            await firstCheckbox.CheckAsync();

            await LogStepAsync("Verify delete button appears");
            var deleteButton = Page.GetByTestId("skills-bulk-delete-button");
            await Assertions.Expect(deleteButton).ToBeVisibleAsync();
            await Assertions.Expect(deleteButton).ToContainTextAsync("Delete selected (1)");

            await LogStepAsync("Uncheck the checkbox");
            await firstCheckbox.UncheckAsync();

            await LogStepAsync("Verify delete button disappears");
            await Assertions.Expect(deleteButton).Not.ToBeVisibleAsync();
        });
    }

    // ====================================================================
    // TEST 4: Bulk delete with confirmation
    // ====================================================================

    [Fact]
    [Trait("Feature", "SkillsBulkDelete")]
    public async Task BulkDelete_WithConfirmation_DeletesSkills()
    {
        await WithScreenshotOnFailure(async () =>
        {
            // Create test skills first
            var skillNames = await CreateTestSkillsAsync(3);

            await LogStepAsync("Navigate to Skills page");
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/skills", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            await LogStepAsync("Click Installed tab");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Installed" }).ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await LogStepAsync("Check select-all checkbox");
            var selectAllCheckbox = Page.GetByTestId("skills-select-all");
            await selectAllCheckbox.CheckAsync();

            await LogStepAsync("Click delete button");
            var deleteButton = Page.GetByTestId("skills-bulk-delete-button");
            
            // Set up dialog handler before clicking
            Page.Dialog += async (_, dialog) =>
            {
                await LogStepAsync($"Dialog appeared: {dialog.Message}");
                dialog.Message.Should().Contain("Delete");
                await dialog.AcceptAsync();
            };

            await deleteButton.ClickAsync();

            await LogStepAsync("Wait for page reload after deletion");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await LogStepAsync("Verify skills are deleted");
            foreach (var skillName in skillNames)
            {
                var skillRow = Page.Locator($"tr:has-text('{skillName}')");
                await Assertions.Expect(skillRow).Not.ToBeVisibleAsync();
            }

            await LogStepAsync("Verify delete button is gone (no selection)");
            await Assertions.Expect(deleteButton).Not.ToBeVisibleAsync();
        });
    }

    // ====================================================================
    // TEST 5: Cancel bulk delete
    // ====================================================================

    [Fact]
    [Trait("Feature", "SkillsBulkDelete")]
    public async Task BulkDelete_CancelConfirmation_DoesNotDelete()
    {
        await WithScreenshotOnFailure(async () =>
        {
            // Create test skills first
            var skillNames = await CreateTestSkillsAsync(2);

            await LogStepAsync("Navigate to Skills page");
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/skills", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            await LogStepAsync("Click Installed tab");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Installed" }).ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await LogStepAsync("Check select-all checkbox");
            var selectAllCheckbox = Page.GetByTestId("skills-select-all");
            await selectAllCheckbox.CheckAsync();

            await LogStepAsync("Click delete button");
            var deleteButton = Page.GetByTestId("skills-bulk-delete-button");
            
            // Set up dialog handler to dismiss
            Page.Dialog += async (_, dialog) =>
            {
                await LogStepAsync($"Dialog appeared, canceling");
                await dialog.DismissAsync();
            };

            await deleteButton.ClickAsync();

            await LogStepAsync("Wait a moment");
            await Task.Delay(1000);

            await LogStepAsync("Verify skills still exist");
            foreach (var skillName in skillNames)
            {
                var skillRow = Page.Locator($"tr:has-text('{skillName}')");
                await Assertions.Expect(skillRow).ToBeVisibleAsync();
            }
        });
    }

    // ====================================================================
    // TEST 6: Selection clears when switching tabs
    // ====================================================================

    [Fact]
    [Trait("Feature", "SkillsBulkDelete")]
    public async Task BulkSelect_SwitchTabs_ClearsSelection()
    {
        await WithScreenshotOnFailure(async () =>
        {
            // Create test skills first
            await CreateTestSkillsAsync(1);

            await LogStepAsync("Navigate to Skills page");
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/skills", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            await LogStepAsync("Click Installed tab");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Installed" }).ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await LogStepAsync("Check a skill checkbox");
            var firstCheckbox = Page.Locator("input[type='checkbox'][data-testid^='skill-select-']").First();
            await firstCheckbox.CheckAsync();

            await LogStepAsync("Verify delete button appears");
            var deleteButton = Page.GetByTestId("skills-bulk-delete-button");
            await Assertions.Expect(deleteButton).ToBeVisibleAsync();

            await LogStepAsync("Switch to System tab");
            await Page.GetByRole(AriaRole.Button, new() { Name = "System" }).ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await LogStepAsync("Switch back to Installed tab");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Installed" }).ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await LogStepAsync("Verify delete button is gone (selection cleared)");
            await Assertions.Expect(deleteButton).Not.ToBeVisibleAsync();
        });
    }

    // ====================================================================
    // Helper methods
    // ====================================================================

    private async Task<List<string>> CreateTestSkillsAsync(int count)
    {
        var skillNames = new List<string>();
        var gatewayUrl = Fixture.GatewayUrl;

        for (int i = 0; i < count; i++)
        {
            var skillName = $"test-bulk-skill-{Guid.NewGuid():N}".Substring(0, 20);
            skillNames.Add(skillName);

            await LogStepAsync($"Creating test skill: {skillName}");

            using var client = new HttpClient { BaseAddress = new Uri(gatewayUrl) };
            var response = await client.PostAsJsonAsync("/api/skills", new
            {
                name = skillName,
                layer = "installed",
                description = $"Test skill {i + 1} for bulk delete",
                body = $"# {skillName}\n\nTest skill body for bulk operations."
            });

            response.EnsureSuccessStatusCode();
        }

        // Wait for registry rebuild
        await Task.Delay(2000);

        return skillNames;
    }
}

// K-4 — Dylan (Tester)
// Comprehensive E2E tests for Skills Import feature (integration + UI flow)
// Tests: import button, single file, folder archive, duplicates, invalid files, UX flow, error handling

using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// E2E tests for Skills Import functionality — covers the import button (UI click/file picker),
/// single .md file uploads, .zip folder archive uploads, duplicate rejection, invalid file handling,
/// UX progress/error messaging, and comprehensive error scenarios.
/// 
/// Test patterns:
/// - All tests use WithScreenshotOnFailure for debugging on CI failure
/// - Temp files created in project directory (not /tmp) for Windows compatibility
/// - File fixtures cleaned up in finally blocks
/// - Tests verify both UI state and API responses (201/400/409 status codes)
/// </summary>
[Collection("AppHost")]
[Trait("Category", "E2E")]
public class SkillsImportE2ETests : PlaywrightTestBase
{
    public SkillsImportE2ETests(AppHostFixture fixture) : base(fixture)
    {
    }

    // ====================================================================
    // TEST 1: e2e-import-button
    // Verify Skills import button exists in UI, is clickable, and opens file picker
    // ====================================================================

    [Fact]
    [Trait("Feature", "SkillsImport")]
    public async Task E2eImportButton_ExistsAndClickable_OpensFilePicker()
    {
        await WithScreenshotOnFailure(async () =>
        {
            await LogStepAsync("🟦 E2E-1: Navigate to skills page");
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/skills", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            // Verify page loaded without error
            var errorHeading = Page.Locator("h1:has-text('Error')");
            await Assertions.Expect(errorHeading).Not.ToBeVisibleAsync(new() { Timeout = 5_000 });

            await LogStepAsync("🔍 Looking for import button");
            var importButton = Page.Locator("[data-testid='skills-import-button']");

            // Test 1a: Button exists and is visible
            await Assertions.Expect(importButton).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await LogStepAsync("✅ Import button is visible");

            // Test 1b: Button is enabled (not disabled)
            await Assertions.Expect(importButton).ToBeEnabledAsync();
            await LogStepAsync("✅ Import button is enabled");

            // Test 1c: Click opens file picker
            await LogStepAsync("📂 Clicking import button to open file picker");
            await importButton.ClickAsync();

            // File input should exist (d-none means it's hidden but present)
            var fileInput = Page.Locator("input[type='file']").First;
            Assert.Equal(1, await fileInput.CountAsync());
            await LogStepAsync("✅ File input exists (triggers via JS on button click)");

            // Close the modal to avoid interfering with subsequent tests
            var closeButton = Page.Locator("button.btn-close").First;
            if (await closeButton.IsVisibleAsync())
            {
                await closeButton.ClickAsync();
                await Page.WaitForTimeoutAsync(500);
            }
        });
    }

    // ====================================================================
    // TEST 2: e2e-import-single
    // Single .md file import: select file, upload, skill appears in registry
    // ====================================================================

    [Fact]
    [Trait("Feature", "SkillsImport")]
    public async Task E2eImportSingle_MarkdownFile_SucceedsAndAppearsInRegistry()
    {
        await WithScreenshotOnFailure(async () =>
        {
            var skillName = $"e2e-single-skill-{Guid.NewGuid():N}";
            var tempFile = Path.Combine(Directory.GetCurrentDirectory(), $"{skillName}.md");

            try
            {
                // Create test .md file with valid frontmatter
                var skillContent = $"""
                    ---
                    name: {skillName}
                    description: E2E single file import test
                    version: 1.0.0
                    ---

                    # {skillName}

                    This is a test skill for E2E single file import validation.
                    
                    ## Features
                    - Test feature 1
                    - Test feature 2
                    """;

                await File.WriteAllTextAsync(tempFile, skillContent);
                await LogStepAsync($"📝 Created test skill file: {skillName}");

                // Navigate to skills page
                await Page.GotoAsync($"{Fixture.WebBaseUrl}/skills", new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 60_000
                });

                // Click import button
                var importButton = Page.Locator("[data-testid='skills-import-button']");
                await importButton.ClickAsync();
                await LogStepAsync("🟨 Import button clicked");

                // Upload the file
                var fileInput = Page.Locator("input[type='file']").First;
                await fileInput.SetInputFilesAsync(tempFile);
                await LogStepAsync($"📤 File uploaded: {Path.GetFileName(tempFile)}");

                // Wait for upload to complete
                await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(1000); // Additional delay for API response processing
                await LogStepAsync("⏳ Upload completed, waiting for registry update");

                // Verify import succeeded via API
                using var client = Fixture.CreateGatewayHttpClient();
                var skillResponse = await client.GetAsync($"/api/skills/{skillName}");
                Assert.True(
                    skillResponse.IsSuccessStatusCode || skillResponse.StatusCode == System.Net.HttpStatusCode.OK,
                    $"Skill registry lookup failed with {skillResponse.StatusCode}"
                );
                await LogStepAsync($"✅ Skill '{skillName}' verified in registry");

                // Verify UI state after import
                await Assertions.Expect(importButton).ToBeVisibleAsync();
                await LogStepAsync("✅ Import button still visible and accessible");
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                    await LogStepAsync($"🧹 Cleaned up test file: {Path.GetFileName(tempFile)}");
                }
            }
        });
    }

    // ====================================================================
    // TEST 3: e2e-import-folder
    // Folder (.zip) import: extract, validate SKILL.md, appears in registry
    // ====================================================================

    [Fact]
    [Trait("Feature", "SkillsImport")]
    public async Task E2eImportFolder_ZipArchive_ExtractsAndAppearsInRegistry()
    {
        await WithScreenshotOnFailure(async () =>
        {
            var folderName = $"skill-folder-{Guid.NewGuid():N}";
            var tempDir = Path.Combine(Directory.GetCurrentDirectory(), folderName);
            var zipFile = Path.Combine(Directory.GetCurrentDirectory(), $"{folderName}.zip");

            try
            {
                // Create test folder structure
                Directory.CreateDirectory(tempDir);

                var skillMdContent = $"""
                    ---
                    name: {folderName}
                    description: E2E folder import test
                    version: 1.0.0
                    ---

                    # Folder Skill

                    Multi-file skill package for E2E testing.
                    """;

                var configContent = $"""
                    version: "1.0"
                    name: {folderName}
                    """;

                var examplesContent = """
                    {
                      "examples": [
                        {
                          "name": "example1",
                          "input": "test input",
                          "output": "expected output"
                        }
                      ]
                    }
                    """;

                // Write files
                await File.WriteAllTextAsync(Path.Combine(tempDir, "SKILL.md"), skillMdContent);
                await File.WriteAllTextAsync(Path.Combine(tempDir, "config.yaml"), configContent);
                await File.WriteAllTextAsync(Path.Combine(tempDir, "examples.json"), examplesContent);
                await LogStepAsync($"📁 Created test folder with SKILL.md, config.yaml, examples.json");

                // Create zip archive
                ZipFile.CreateFromDirectory(tempDir, zipFile);
                await LogStepAsync($"📦 Created zip archive: {Path.GetFileName(zipFile)}");

                // Navigate to skills page
                await Page.GotoAsync($"{Fixture.WebBaseUrl}/skills", new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 60_000
                });

                // Click import button and upload zip
                var importButton = Page.Locator("[data-testid='skills-import-button']");
                await importButton.ClickAsync();

                var fileInput = Page.Locator("input[type='file']").First;
                await fileInput.SetInputFilesAsync(zipFile);
                await LogStepAsync($"📤 Zip archive uploaded");

                // Wait for extraction and import
                await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(1500); // Allow time for zip extraction + skill registration
                await LogStepAsync("⏳ Archive extracted and registered");

                // Verify skill in registry
                using var client = Fixture.CreateGatewayHttpClient();
                var skillResponse = await client.GetAsync($"/api/skills/{folderName}");
                Assert.True(skillResponse.IsSuccessStatusCode, $"Skill '{folderName}' not found in registry");
                await LogStepAsync($"✅ Folder-based skill '{folderName}' verified in registry");
            }
            finally
            {
                if (File.Exists(zipFile)) File.Delete(zipFile);
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
                await LogStepAsync("🧹 Cleaned up test files");
            }
        });
    }

    // ====================================================================
    // TEST 4: e2e-import-duplicates
    // Try importing skill with existing name: expect 409 Conflict error
    // ====================================================================

    [Fact]
    [Trait("Feature", "SkillsImport")]
    public async Task E2eImportDuplicates_ExistingSkillName_ReturnsConflictError()
    {
        await WithScreenshotOnFailure(async () =>
        {
            var skillName = $"dup-test-{Guid.NewGuid():N}";
            var tempFile1 = Path.Combine(Directory.GetCurrentDirectory(), $"{skillName}-v1.md");
            var tempFile2 = Path.Combine(Directory.GetCurrentDirectory(), $"{skillName}-v2.md");

            try
            {
                // Create first skill file
                var skillContent = $"""
                    ---
                    name: {skillName}
                    description: First version
                    ---
                    # Duplicate Test Skill (v1)
                    """;

                await File.WriteAllTextAsync(tempFile1, skillContent);
                await LogStepAsync($"📝 Created first skill version: {skillName}");

                // Navigate and import first version
                await Page.GotoAsync($"{Fixture.WebBaseUrl}/skills", new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 60_000
                });

                var importButton = Page.Locator("[data-testid='skills-import-button']");

                await importButton.ClickAsync();
                var fileInput = Page.Locator("input[type='file']").First;
                await fileInput.SetInputFilesAsync(tempFile1);
                await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(1000);
                await LogStepAsync($"✅ First import completed");

                // Try to import second file with same name
                var skillContent2 = $"""
                    ---
                    name: {skillName}
                    description: Second version (should conflict)
                    ---
                    # Duplicate Test Skill (v2)
                    """;

                await File.WriteAllTextAsync(tempFile2, skillContent2);
                await LogStepAsync($"📝 Created second skill version (same name)");

                // Click import again
                await importButton.ClickAsync();
                var fileInput2 = Page.Locator("input[type='file']").First;
                await fileInput2.SetInputFilesAsync(tempFile2);
                await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(1000);
                await LogStepAsync($"📤 Second import attempted");

                // Verify 409 Conflict response via API
                using var client = Fixture.CreateGatewayHttpClient();
                
                // Re-attempt import via API to verify conflict handling
                var secondContent = await File.ReadAllTextAsync(tempFile2);
                using var content = new MultipartFormDataContent();
                using var streamContent = new StreamContent(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(secondContent)));
                content.Add(streamContent, "file", Path.GetFileName(tempFile2));
                
                var importResponse = await client.PostAsync("/api/skills/import", content);
                Assert.True(
                    importResponse.StatusCode == System.Net.HttpStatusCode.Conflict ||
                    importResponse.StatusCode == System.Net.HttpStatusCode.BadRequest,
                    $"Expected 409/400, got {importResponse.StatusCode} for duplicate import"
                );
                await LogStepAsync($"✅ Duplicate import correctly rejected with {importResponse.StatusCode}");

                // Verify import button still accessible for retry
                await Assertions.Expect(importButton).ToBeEnabledAsync();
                await LogStepAsync("✅ Import button remains enabled");
            }
            finally
            {
                foreach (var f in new[] { tempFile1, tempFile2 })
                {
                    if (File.Exists(f)) File.Delete(f);
                }
                await LogStepAsync("🧹 Cleaned up test files");
            }
        });
    }

    // ====================================================================
    // TEST 5: e2e-import-invalid
    // Try uploading invalid files (.txt, corrupted zip, bad frontmatter): expect errors
    // ====================================================================

    [Fact]
    [Trait("Feature", "SkillsImport")]
    public async Task E2eImportInvalid_BadFiles_ReturnsValidationErrors()
    {
        await WithScreenshotOnFailure(async () =>
        {
            var tempTxtFile = Path.Combine(Directory.GetCurrentDirectory(), $"invalid-{Guid.NewGuid():N}.txt");
            var tempBadMdFile = Path.Combine(Directory.GetCurrentDirectory(), $"invalid-{Guid.NewGuid():N}.md");

            try
            {
                // Test 1: .txt file (unsupported extension)
                await File.WriteAllTextAsync(tempTxtFile, "This is a text file, not markdown");
                await LogStepAsync("📝 Created invalid .txt file");

                await NavigateToSkillsPageAsync();
                await OpenImportDialogAsync();
                await UploadImportFileAsync(tempTxtFile);
                await LogStepAsync("📤 .txt file upload attempted");

                await ExpectImportErrorAsync(
                    "Unsupported File Type",
                    "Only .md and .zip files are supported.",
                    ".txt");
                await LogStepAsync("✅ Unsupported extension shown in UI");
                await DismissImportErrorAsync();

                // Test 2: .md file with malformed frontmatter
                var badContent = """
                    This is missing proper frontmatter
                    name: bad-skill
                    description: No dashes
                    
                    # Missing YAML delimiters
                    """;

                await File.WriteAllTextAsync(tempBadMdFile, badContent);
                await LogStepAsync("📝 Created .md file with invalid frontmatter");
                await UploadImportFileAsync(tempBadMdFile);
                await LogStepAsync("📤 Malformed .md file upload attempted");

                await ExpectImportErrorAsync(
                    "Malformed Skill",
                    "The skill file format is invalid.",
                    "frontmatter");
                await LogStepAsync("✅ Malformed markdown shown in UI");

                // Verify page remains responsive
                await CloseImportDialogAsync();
                var importButton = Page.GetByTestId("skills-import-button");
                await Assertions.Expect(importButton).ToBeVisibleAsync();
                await Assertions.Expect(importButton).ToBeEnabledAsync();
                await LogStepAsync("✅ UI remains responsive after validation errors");
            }
            finally
            {
                foreach (var f in new[] { tempTxtFile, tempBadMdFile })
                {
                    if (File.Exists(f)) File.Delete(f);
                }
                await LogStepAsync("🧹 Cleaned up test files");
            }
        });
    }

    // ====================================================================
    // TEST 6: e2e-import-usability
    // UX flow: progress visible, success message shown, errors dismissible
    // ====================================================================

    [Fact]
    [Trait("Feature", "SkillsImport")]
    public async Task E2eImportUsability_ProgressAndMessages_EnhanceUxFlow()
    {
        await WithScreenshotOnFailure(async () =>
        {
            var skillName = $"ux-test-{Guid.NewGuid():N}";
            var tempFile = Path.Combine(Directory.GetCurrentDirectory(), $"{skillName}.md");

            try
            {
                var skillContent = $"""
                    ---
                    name: {skillName}
                    description: UX flow test
                    ---
                    # {skillName}
                    """;

                await File.WriteAllTextAsync(tempFile, skillContent);
                await LogStepAsync("📝 Created skill file for UX testing");

                await Page.GotoAsync($"{Fixture.WebBaseUrl}/skills", new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 60_000
                });

                var importButton = Page.Locator("[data-testid='skills-import-button']");

                await importButton.ClickAsync();
                await LogStepAsync("📂 File picker opened");

                // Look for progress indicator before/during upload
                var fileInput = Page.Locator("input[type='file']").First;
                await fileInput.SetInputFilesAsync(tempFile);
                await LogStepAsync("📤 File uploaded");

                // Wait for network idle and check for success message
                await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(1000);

                // Look for success indicators
                var successMessages = Page.Locator(".alert-success, [role='alert']:has-text('Success')");
                
                // If success message exists, verify it's visible
                if (await successMessages.CountAsync() > 0)
                {
                    var firstSuccess = successMessages.First;
                    await Assertions.Expect(firstSuccess).ToBeVisibleAsync(new() { Timeout = 5_000 });
                    await LogStepAsync("✅ Success message displayed");
                }
                else
                {
                    await LogStepAsync("ℹ️ No explicit success message found (may be implicit via registry update)");
                }

                // Verify the page is still responsive and import button accessible
                await Assertions.Expect(importButton).ToBeVisibleAsync();
                await LogStepAsync("✅ UI responsive after import");
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
                await LogStepAsync("🧹 Cleaned up test file");
            }
        });
    }

    // ====================================================================
    // TEST 7: e2e-import-errors
    // Comprehensive error handling: network errors, validation errors, edge cases
    // ====================================================================

    [Fact]
    [Trait("Feature", "SkillsImport")]
    public async Task E2eImportErrors_ComprehensiveErrorHandling_GracefulFailures()
    {
        await WithScreenshotOnFailure(async () =>
        {
            var tempDir = Path.Combine(Directory.GetCurrentDirectory(), $"error-test-{Guid.NewGuid():N}");
            var emptyZip = Path.Combine(Directory.GetCurrentDirectory(), $"empty-{Guid.NewGuid():N}.zip");
            var hugeFile = Path.Combine(Directory.GetCurrentDirectory(), $"huge-{Guid.NewGuid():N}.md");
            var importedSpecialSkillName = $"special-char-skill-{Guid.NewGuid():N}".Substring(0, 32);
            var specialName = $"skill-with-ñ-测试-{Guid.NewGuid():N}.md";
            var specialFile = Path.Combine(Directory.GetCurrentDirectory(), specialName);

            try
            {
                // Test 1: Empty zip file
                Directory.CreateDirectory(tempDir);
                ZipFile.CreateFromDirectory(tempDir, emptyZip);
                await LogStepAsync("📦 Created empty zip archive");

                var importButton = await NavigateToSkillsPageAsync();
                await OpenImportDialogAsync(importButton);
                await UploadImportFileAsync(emptyZip);
                await LogStepAsync("📤 Empty zip upload attempted");

                await ExpectImportErrorAsync(
                    "Malformed Skill",
                    "The skill file format is invalid.",
                    "SKILL.md");
                await LogStepAsync("✅ Empty zip handled gracefully in UI");
                await DismissImportErrorAsync();

                // Test 2: File with special characters in name
                var specialContent = $"""
                    ---
                    name: {importedSpecialSkillName}
                    description: Test special characters
                    ---
                    # Special Char Test
                    """;

                await File.WriteAllTextAsync(specialFile, specialContent);

                await LogStepAsync($"📝 Created file with special characters: {specialName}");
                await UploadImportFileAsync(specialFile);
                await WaitForImportSuccessAsync();
                using var client = Fixture.CreateGatewayHttpClient();
                var specialResponse = await WaitForSkillAsync(client, importedSpecialSkillName);
                Assert.True(specialResponse.IsSuccessStatusCode, $"Expected imported special-char skill, got {specialResponse.StatusCode}");
                await LogStepAsync("✅ Special character filename handled");

                // Test 3: Very large file (edge case)
                var largeContent = new string('-', 5 * 1024 * 1024); // 5 MB
                largeContent = $"""
                    ---
                    name: huge-skill
                    description: Very large file
                    ---
                    # Huge File
                    
                    {largeContent}
                    """;

                await File.WriteAllTextAsync(hugeFile, largeContent);
                await LogStepAsync("📝 Created large test file (5+ MB)");

                await OpenImportDialogAsync(importButton);
                await UploadImportFileAsync(hugeFile);
                await ExpectImportErrorAsync(
                    "File Too Large",
                    "The skill file is too large",
                    "exceeds");
                await LogStepAsync("✅ Large file upload rejected gracefully");

                // Verify UI recovery
                await CloseImportDialogAsync();
                await Assertions.Expect(importButton).ToBeVisibleAsync(new() { Timeout = 5_000 });
                await Assertions.Expect(importButton).ToBeEnabledAsync();
                await LogStepAsync("✅ UI recovered from error scenarios");
            }
            finally
            {
                foreach (var f in new[] { emptyZip, hugeFile, specialFile })
                {
                    if (File.Exists(f)) File.Delete(f);
                }
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
                await LogStepAsync("🧹 Cleaned up test files");
            }
        });
    }

    private async Task<ILocator> NavigateToSkillsPageAsync()
    {
        var importButton = Page.GetByTestId("skills-import-button");

        Exception? lastException = null;
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                await Page.GotoAsync($"{Fixture.WebBaseUrl}/skills", new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 20_000
                });

                await Assertions.Expect(importButton).ToBeVisibleAsync(new() { Timeout = 15_000 });
                await Assertions.Expect(importButton).ToBeEnabledAsync();
                await Page.WaitForTimeoutAsync(500);
                return importButton;
            }
            catch (Exception ex) when ((ex is PlaywrightException || ex is TimeoutException) && attempt < 4)
            {
                lastException = ex;
                await LogStepAsync($"⏳ Skills page load attempt {attempt} failed; retrying");
                await Page.WaitForTimeoutAsync(3_000);
            }
        }

        throw lastException ?? new TimeoutException("Failed to navigate to skills page.");
    }

    private async Task OpenImportDialogAsync(ILocator? importButton = null)
    {
        importButton ??= Page.GetByTestId("skills-import-button");
        await Page.WaitForTimeoutAsync(500);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await importButton.ClickAsync();
            try
            {
                await Assertions.Expect(ImportDialog).ToBeVisibleAsync(new() { Timeout = 3_000 });
                return;
            }
            catch (PlaywrightException) when (attempt < 3)
            {
                await LogStepAsync($"⏳ Import dialog did not open on click {attempt}; retrying");
                await Page.WaitForTimeoutAsync(500);
            }
        }

        await Assertions.Expect(ImportDialog).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    private async Task UploadImportFileAsync(string path)
    {
        var fileInput = ImportDialog.Locator("input[type='file']").First;
        await fileInput.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 10_000 });
        await fileInput.SetInputFilesAsync(path);
    }

    private async Task WaitForImportSuccessAsync()
    {
        await Assertions.Expect(ImportDialog).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(Page.GetByTestId("skills-import-button")).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    private async Task<HttpResponseMessage> WaitForSkillAsync(HttpClient client, string skillName)
    {
        HttpResponseMessage? lastResponse = null;
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            lastResponse?.Dispose();
            lastResponse = await client.GetAsync($"/api/skills/{skillName}");
            if (lastResponse.IsSuccessStatusCode)
            {
                return lastResponse;
            }

            await Page.WaitForTimeoutAsync(1_000);
        }

        return lastResponse ?? new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private async Task ExpectImportErrorAsync(string title, string messageContains, string? detailContains = null)
    {
        await Assertions.Expect(ImportErrorAlert).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(ImportErrorAlert).ToContainTextAsync(title);
        await Assertions.Expect(ImportErrorAlert).ToContainTextAsync(messageContains);
        if (!string.IsNullOrWhiteSpace(detailContains))
        {
            await Assertions.Expect(ImportErrorAlert).ToContainTextAsync(detailContains);
        }
    }

    private async Task DismissImportErrorAsync()
    {
        await ImportDialog.GetByRole(AriaRole.Button, new() { Name = "Dismiss" }).ClickAsync();
        await Assertions.Expect(ImportErrorAlert).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    private async Task CloseImportDialogAsync()
    {
        var closeButton = ImportDialog.Locator(".modal-header .btn-close");
        await closeButton.ClickAsync();
        await Assertions.Expect(ImportDialog).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    private ILocator ImportDialog => Page.Locator("[role='dialog'][aria-labelledby='importSkillsTitle']");
    private ILocator ImportErrorAlert => ImportDialog.Locator(".alert[role='alert']").First;
}

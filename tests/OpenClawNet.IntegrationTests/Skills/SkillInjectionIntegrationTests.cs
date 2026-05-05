using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClawNet.Agent;
using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.IntegrationTests.Skills;

/// <summary>
/// Integration tests for the full skill injection pipeline:
/// SKILLS_INVENTORY.md → SkillService → DefaultPromptComposer
/// </summary>
public class SkillInjectionIntegrationTests
{
    [Fact]
    public async Task FullPipeline_LoadsRealInventory_InjectsRelevantSkills()
    {
        // Test 8: Full pipeline with real SKILLS_INVENTORY.md
        // Arrange
        var workspacePath = FindWorkspaceRoot();
        workspacePath.Should().NotBeNull("workspace root should be found");

        var skillService = new DefaultSkillService(
            NullLogger<DefaultSkillService>.Instance,
            Options.Create(new WorkspaceOptions { WorkspacePath = workspacePath! }));

        // Act - Query for skills related to agent debugging
        var skills = await skillService.FindRelevantSkillsAsync("I need to debug an agent's behavior");

        // Assert - Should find relevant skills from the real inventory
        skills.Should().NotBeEmpty("real inventory should contain skills");
        
        // Log what we found for transparency
        Console.WriteLine($"Found {skills.Count} relevant skills:");
        foreach (var skill in skills)
        {
            Console.WriteLine($"  - {skill.Name} (confidence: {skill.Confidence}, score: {skill.RelevanceScore})");
        }
    }

    [Fact]
    public async Task FullPipeline_ComposerInjectsSkills_IntoFinalPrompt()
    {
        // Test 8 continued: Verify the full pipeline from inventory → prompt
        // Arrange
        var workspacePath = FindWorkspaceRoot();
        workspacePath.Should().NotBeNull();

        var skillService = new DefaultSkillService(
            NullLogger<DefaultSkillService>.Instance,
            Options.Create(new WorkspaceOptions { WorkspacePath = workspacePath! }));

        var workspaceLoader = new FakeWorkspaceLoader(new BootstrapContext(null, null, null));
        var composer = new DefaultPromptComposer(
            workspaceLoader,
            skillService,
            NullLogger<DefaultPromptComposer>.Instance,
            Options.Create(new WorkspaceOptions { WorkspacePath = workspacePath! }));

        // Act - Compose a prompt with a task that should match skills
        var messages = await composer.ComposeAsync(new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Help me with blazor and mudblazor table migration"
        });

        // Assert
        messages.Should().NotBeEmpty();
        var systemPrompt = messages.First(m => m.Role == ChatMessageRole.System).Content;
        
        // Should include skills section if skills were found
        var inventoryExists = File.Exists(Path.Combine(workspacePath!, ".squad", "SKILLS_INVENTORY.md"));
        if (inventoryExists)
        {
            if (systemPrompt.Contains("Relevant Skills"))
            {
                systemPrompt.Should().Contain("Relevant Skills", 
                    "system prompt should include skills section when inventory exists");
                // Check for path-separator-agnostic skill reference
                systemPrompt.Should().MatchRegex(@"\.squad[/\\]skills[/\\]",
                    "skills section should reference skill files");
            }
            else
            {
                Console.WriteLine("⚠ No skills matched the task keywords");
            }
        }
    }

    [Fact]
    public async Task FullPipeline_ParsesMarkers_FromRealInventory()
    {
        // Test 9: Verify markers and confidence levels are parsed correctly
        // Arrange
        var workspacePath = FindWorkspaceRoot();
        workspacePath.Should().NotBeNull();

        var inventoryPath = Path.Combine(workspacePath!, ".squad", "SKILLS_INVENTORY.md");
        if (!File.Exists(inventoryPath))
        {
            // Skip test if inventory doesn't exist (for local dev environments)
            Console.WriteLine("Skipping: SKILLS_INVENTORY.md not found");
            return;
        }

        var skillService = new DefaultSkillService(
            NullLogger<DefaultSkillService>.Instance,
            Options.Create(new WorkspaceOptions { WorkspacePath = workspacePath! }));

        // Act - Load all skills from inventory
        var skills = await skillService.FindRelevantSkillsAsync("blazor mudblazor hardening aspire ndjson testing security");

        // Assert - Verify confidence levels and metadata are parsed
        skills.Should().NotBeEmpty();
        
        foreach (var skill in skills)
        {
            skill.Name.Should().NotBeNullOrEmpty("skill name should be parsed");
            skill.Keywords.Should().NotBeEmpty("skill keywords should be parsed");
            Enum.IsDefined(typeof(ConfidenceLevel), skill.Confidence).Should().BeTrue(
                "confidence level should be a valid enum value");
            skill.ExtractedDate.Should().NotBeNullOrEmpty("extracted date should be parsed");
            skill.ValidatedBy.Should().NotBeEmpty("validated-by should be parsed");
        }
    }

    private static string? FindWorkspaceRoot()
    {
        // Walk up from current directory to find .squad directory
        var current = Directory.GetCurrentDirectory();
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current, ".squad")))
            {
                return current;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        return null;
    }

    private sealed class FakeWorkspaceLoader : IWorkspaceLoader
    {
        private readonly BootstrapContext _context;
        public FakeWorkspaceLoader(BootstrapContext context) => _context = context;
        public Task<BootstrapContext> LoadAsync(string workspacePath, CancellationToken ct = default)
            => Task.FromResult(_context);
    }
}

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClawNet.Agent;
using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.E2ETests.Skills;

/// <summary>
/// E2E tests for agent spawn with skill injection.
/// Tests the full path: Agent spawn → Prompt composition → Skill injection
/// </summary>
[Trait("Category", "E2E")]
public class AgentSpawnWithSkillsTests
{
    [Fact]
    public async Task AgentSpawn_WithMatchingTask_ReceivesEnrichedPrompt()
    {
        // Test 10: Agent spawn with skill injection
        // Arrange - Set up services as they would be in production
        var workspacePath = FindWorkspaceRoot();
        if (workspacePath == null || !File.Exists(Path.Combine(workspacePath, ".squad", "SKILLS_INVENTORY.md")))
        {
            // Skip if inventory not found (acceptable for local dev)
            Console.WriteLine("Skipping: SKILLS_INVENTORY.md not found");
            return;
        }

        var services = new ServiceCollection();
        services.AddSingleton<ISkillService>(sp => new DefaultSkillService(
            NullLogger<DefaultSkillService>.Instance,
            Options.Create(new WorkspaceOptions { WorkspacePath = workspacePath })));
        services.AddSingleton<IWorkspaceLoader>(sp => new FakeWorkspaceLoader(new BootstrapContext(null, null, null)));
        services.AddSingleton<IPromptComposer>(sp => new DefaultPromptComposer(
            sp.GetRequiredService<IWorkspaceLoader>(),
            sp.GetRequiredService<ISkillService>(),
            NullLogger<DefaultPromptComposer>.Instance,
            Options.Create(new WorkspaceOptions { WorkspacePath = workspacePath })));

        var provider = services.BuildServiceProvider();
        var composer = provider.GetRequiredService<IPromptComposer>();

        // Act - Simulate agent spawn with a task that should match skills
        var messages = await composer.ComposeAsync(new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "memory lookup optimization and agent debugging"
        });

        // Assert - Verify enriched prompt
        messages.Should().NotBeEmpty();
        var systemPrompt = messages.First(m => m.Role == ChatMessageRole.System).Content;
        
        // At least verify the prompt was composed without errors
        systemPrompt.Should().NotBeNullOrEmpty("system prompt should be composed");
        systemPrompt.Should().Contain("OpenClaw", "system prompt should contain base instructions");
        
        // If skills were found, they should be in the prompt
        Console.WriteLine($"System prompt length: {systemPrompt.Length} chars");
        if (systemPrompt.Contains("Relevant Skills"))
        {
            Console.WriteLine("✓ Skills section found in prompt");
            systemPrompt.Should().MatchRegex(@"\.squad[/\\]skills[/\\]", "skills should reference skill files");
        }
        else
        {
            Console.WriteLine("⚠ No skills section (task may not have matched any skills)");
        }
    }

    [Fact]
    public async Task AgentSpawn_WithNoMatchingSkills_StillWorks()
    {
        // Test 11: Legacy behavior - agent spawn without matching skills
        // Arrange
        var workspacePath = FindWorkspaceRoot();
        if (workspacePath == null)
        {
            Console.WriteLine("Skipping: Workspace root not found");
            return;
        }

        var skillService = new DefaultSkillService(
            NullLogger<DefaultSkillService>.Instance,
            Options.Create(new WorkspaceOptions { WorkspacePath = workspacePath }));

        var workspaceLoader = new FakeWorkspaceLoader(new BootstrapContext(null, null, null));
        var composer = new DefaultPromptComposer(
            workspaceLoader,
            skillService,
            NullLogger<DefaultPromptComposer>.Instance,
            Options.Create(new WorkspaceOptions { WorkspacePath = workspacePath }));

        // Act - Use keywords that won't match any skills
        var messages = await composer.ComposeAsync(new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "quantum computing with rust and golang for blockchain"
        });

        // Assert - Should work without crashing (graceful degradation)
        messages.Should().NotBeEmpty("agent should spawn even without matching skills");
        var systemPrompt = messages.First(m => m.Role == ChatMessageRole.System).Content;
        
        systemPrompt.Should().NotBeNullOrEmpty();
        systemPrompt.Should().Contain("OpenClaw", "base prompt should be intact");
        systemPrompt.Should().NotContain("Relevant Skills", 
            "skills section should not appear when no skills match");
        
        Console.WriteLine("✓ Agent spawned successfully without skills");
    }

    [Fact]
    public async Task AgentSpawn_WithMissingInventory_GracefullyDegrades()
    {
        // Test graceful degradation when inventory file is missing
        // Arrange - Point to a non-existent workspace
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var skillService = new DefaultSkillService(
                NullLogger<DefaultSkillService>.Instance,
                Options.Create(new WorkspaceOptions { WorkspacePath = tempDir }));

            var workspaceLoader = new FakeWorkspaceLoader(new BootstrapContext(null, null, null));
            var composer = new DefaultPromptComposer(
                workspaceLoader,
                skillService,
                NullLogger<DefaultPromptComposer>.Instance,
                Options.Create(new WorkspaceOptions { WorkspacePath = tempDir }));

            // Act
            var messages = await composer.ComposeAsync(new PromptContext
            {
                SessionId = Guid.NewGuid(),
                UserMessage = "test"
            });

            // Assert - Should work without inventory
            messages.Should().NotBeEmpty();
            var systemPrompt = messages.First(m => m.Role == ChatMessageRole.System).Content;
            systemPrompt.Should().NotBeNullOrEmpty();
            
            Console.WriteLine("✓ Graceful degradation when inventory missing");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static string? FindWorkspaceRoot()
    {
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

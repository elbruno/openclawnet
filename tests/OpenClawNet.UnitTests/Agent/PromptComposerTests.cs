using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClawNet.Agent;
using OpenClawNet.Memory;
using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.UnitTests.Agent;

public class PromptComposerTests
{
    private static readonly IWorkspaceLoader NoOpWorkspaceLoader = new FakeWorkspaceLoader(new BootstrapContext(null, null, null));
    private static readonly ISkillService NoOpSkillService = new FakeSkillService(Array.Empty<SkillSummary>());
    private static readonly IOptions<WorkspaceOptions> DefaultWorkspaceOptions = Options.Create(new WorkspaceOptions());

    [Fact]
    public async Task ComposeAsync_IncludesSystemPrompt()
    {
        var composer = new DefaultPromptComposer(
            NoOpWorkspaceLoader, 
            NoOpSkillService,
            NullLogger<DefaultPromptComposer>.Instance,
            DefaultWorkspaceOptions);
        
        var context = new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Hello"
        };
        
        var messages = await composer.ComposeAsync(context);
        
        messages.Should().HaveCountGreaterThanOrEqualTo(2);
        messages[0].Role.Should().Be(ChatMessageRole.System);
        messages[0].Content.Should().Contain("OpenClaw");
        messages[^1].Role.Should().Be(ChatMessageRole.User);
        messages[^1].Content.Should().Be("Hello");
    }
    
    [Fact]
    public async Task ComposeAsync_IncludesSessionSummary()
    {
        var composer = new DefaultPromptComposer(
            NoOpWorkspaceLoader,
            NoOpSkillService,
            NullLogger<DefaultPromptComposer>.Instance,
            DefaultWorkspaceOptions);
        
        var context = new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Continue",
            SessionSummary = "We discussed .NET architecture earlier."
        };
        
        var messages = await composer.ComposeAsync(context);

        messages[0].Content.Should().Contain("discussed .NET architecture");
    }

    [Fact]
    public async Task ComposeAsync_IncludesRetrievedMemories()
    {
        var composer = new DefaultPromptComposer(
            NoOpWorkspaceLoader,
            NoOpSkillService,
            NullLogger<DefaultPromptComposer>.Instance,
            DefaultWorkspaceOptions);

        var context = new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Remember the retrieval policy",
            RetrievedMemories =
            [
                new MemoryHit("mem-1", "The retrieval level defaults to Off.", 0.987),
                new MemoryHit("mem-2", "Vector DB is the semantic search source.", 0.832)
            ]
        };

        var messages = await composer.ComposeAsync(context);

        messages[0].Content.Should().Contain("# Retrieved Memory");
        messages[0].Content.Should().Contain("retrieval level defaults to Off");
        messages[0].Content.Should().Contain("Vector DB is the semantic search source");
    }
    
    [Fact]
    public async Task ComposeAsync_IncludesHistory()
    {
        var composer = new DefaultPromptComposer(
            NoOpWorkspaceLoader,
            NoOpSkillService,
            NullLogger<DefaultPromptComposer>.Instance,
            DefaultWorkspaceOptions);
        
        var history = new List<ChatMessage>
        {
            new() { Role = ChatMessageRole.User, Content = "First message" },
            new() { Role = ChatMessageRole.Assistant, Content = "First reply" }
        };
        
        var context = new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Second message",
            History = history
        };
        
        var messages = await composer.ComposeAsync(context);
        
        // System + 2 history + 1 user = 4
        messages.Should().HaveCount(4);
    }

    // ── Workspace-aware fallback prompt ──────────────────────────────────────

    [Fact]
    public async Task ComposeAsync_FallbackPrompt_ContainsWorkspacePath()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "test-workspace");
        var opts = Options.Create(new WorkspaceOptions { WorkspacePath = workspacePath });

        var composer = new DefaultPromptComposer(
            NoOpWorkspaceLoader,
            NoOpSkillService,
            NullLogger<DefaultPromptComposer>.Instance,
            opts);
        var messages = await composer.ComposeAsync(new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "ping"
        });

        messages[0].Content.Should().Contain(workspacePath,
            "the fallback system prompt should include the configured workspace path");
    }

    [Fact]
    public async Task ComposeAsync_FallbackPrompt_InstructsFileSystemToolUsage()
    {
        var composer = new DefaultPromptComposer(
            NoOpWorkspaceLoader,
            NoOpSkillService,
            NullLogger<DefaultPromptComposer>.Instance,
            DefaultWorkspaceOptions);
        var messages = await composer.ComposeAsync(new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "ping"
        });

        messages[0].Content.Should().Contain("file_system",
            "the fallback prompt should explicitly name the file_system tool");
        messages[0].Content.Should().NotContain("do not invoke any tools",
            "the old discouraging language should be removed");
    }

    [Fact]
    public async Task ComposeAsync_UsesAgentsMd_WhenWorkspaceProvides()
    {
        var agentsMdContent = "You are CUSTOM_AGENT, an expert in .NET.";
        var workspaceLoader = new FakeWorkspaceLoader(new BootstrapContext(agentsMdContent, null, null));

        var composer = new DefaultPromptComposer(
            workspaceLoader,
            NoOpSkillService,
            NullLogger<DefaultPromptComposer>.Instance,
            DefaultWorkspaceOptions);
        var messages = await composer.ComposeAsync(new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Hi"
        });

        messages[0].Content.Should().Contain("CUSTOM_AGENT",
            "AGENTS.md persona should override the built-in fallback prompt");
    }

    [Fact]
    public async Task ComposeAsync_InjectsRelevantSkills()
    {
        var testSkills = new[]
        {
            new SkillSummary
            {
                Name = "test-skill",
                Description = "Test skill",
                Keywords = new[] { "blazor", "test" },
                Confidence = ConfidenceLevel.High,
                ExtractedDate = "2026-04-27",
                ValidatedBy = new[] { "test" }
            }
        };
        
        var skillService = new FakeSkillService(testSkills);
        var composer = new DefaultPromptComposer(
            NoOpWorkspaceLoader,
            skillService,
            NullLogger<DefaultPromptComposer>.Instance,
            DefaultWorkspaceOptions);
        
        var messages = await composer.ComposeAsync(new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Help me with blazor"
        });

        messages[0].Content.Should().Contain("Relevant Skills");
        messages[0].Content.Should().Contain("test-skill");
        // Use Path.DirectorySeparatorChar-aware check or just check for the skill path pattern
        messages[0].Content.Should().MatchRegex(@"\.squad[/\\]skills[/\\]test-skill[/\\]SKILL\.md");
    }

    [Fact]
    public async Task ComposeAsync_GracefullyHandlesSkillServiceFailure()
    {
        var failingSkillService = new FailingSkillService();
        var composer = new DefaultPromptComposer(
            NoOpWorkspaceLoader,
            failingSkillService,
            NullLogger<DefaultPromptComposer>.Instance,
            DefaultWorkspaceOptions);
        
        var messages = await composer.ComposeAsync(new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Help me"
        });

        // Should still compose a prompt without crashing
        messages.Should().HaveCountGreaterThanOrEqualTo(2);
        messages[0].Role.Should().Be(ChatMessageRole.System);
    }

    [Fact]
    public async Task ComposeAsync_SkillInjection_AddsMinimalOverhead()
    {
        // Test 7: Performance validation - skill injection should add <50ms overhead
        var testSkills = new[]
        {
            new SkillSummary
            {
                Name = "skill-1",
                Description = "Test skill 1",
                Keywords = new[] { "blazor", "test" },
                Confidence = ConfidenceLevel.High,
                ExtractedDate = "2026-04-27",
                ValidatedBy = new[] { "test" }
            },
            new SkillSummary
            {
                Name = "skill-2",
                Description = "Test skill 2",
                Keywords = new[] { "test", "aspire" },
                Confidence = ConfidenceLevel.Medium,
                ExtractedDate = "2026-04-27",
                ValidatedBy = new[] { "test" }
            },
            new SkillSummary
            {
                Name = "skill-3",
                Description = "Test skill 3",
                Keywords = new[] { "hardening", "security" },
                Confidence = ConfidenceLevel.High,
                ExtractedDate = "2026-04-27",
                ValidatedBy = new[] { "test" }
            }
        };
        
        var skillService = new FakeSkillService(testSkills);
        var composer = new DefaultPromptComposer(
            NoOpWorkspaceLoader,
            skillService,
            NullLogger<DefaultPromptComposer>.Instance,
            DefaultWorkspaceOptions);
        
        // Warm up
        await composer.ComposeAsync(new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "test"
        });

        // Measure
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 10; i++)
        {
            await composer.ComposeAsync(new PromptContext
            {
                SessionId = Guid.NewGuid(),
                UserMessage = "Help me with blazor and aspire"
            });
        }
        sw.Stop();

        var avgMs = sw.ElapsedMilliseconds / 10.0;
        avgMs.Should().BeLessThan(50, "skill injection should add minimal overhead");
    }

    [Fact]
    public async Task ComposeAsync_SkipsSkillsSection_WhenNoSkillsFound()
    {
        // Test 6: Verify no "Relevant Skills" section when skill service returns empty
        var emptySkillService = new FakeSkillService(Array.Empty<SkillSummary>());
        var composer = new DefaultPromptComposer(
            NoOpWorkspaceLoader,
            emptySkillService,
            NullLogger<DefaultPromptComposer>.Instance,
            DefaultWorkspaceOptions);
        
        var messages = await composer.ComposeAsync(new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "unmatched task keywords"
        });

        messages[0].Content.Should().NotContain("Relevant Skills",
            "prompt should not include skills section when no skills are found");
        messages[0].Content.Should().Contain("OpenClaw",
            "rest of prompt should be intact");
    }

    // ── Agent Profile Instructions ────────────────────────────────────────────

    [Fact]
    public async Task ComposeAsync_IncludesProfileInstructions_InSystemMessage()
    {
        var composer = new DefaultPromptComposer(
            NoOpWorkspaceLoader,
            NoOpSkillService,
            NullLogger<DefaultPromptComposer>.Instance,
            DefaultWorkspaceOptions);

        var context = new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Ahoy",
            ProfileInstructions = "You are a pirate. Always speak like a pirate."
        };

        var messages = await composer.ComposeAsync(context);

        messages[0].Role.Should().Be(ChatMessageRole.System);
        messages[0].Content.Should().Contain("# Agent Instructions",
            "the Agent Instructions section header must be present when ProfileInstructions is set");
        messages[0].Content.Should().Contain("You are a pirate.",
            "the profile instruction text must appear in the system message");
    }

    [Fact]
    public async Task ComposeAsync_NullProfileInstructions_OmitsAgentInstructionsSection()
    {
        var composer = new DefaultPromptComposer(
            NoOpWorkspaceLoader,
            NoOpSkillService,
            NullLogger<DefaultPromptComposer>.Instance,
            DefaultWorkspaceOptions);

        var context = new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Hello",
            ProfileInstructions = null
        };

        var messages = await composer.ComposeAsync(context);

        messages[0].Content.Should().NotContain("# Agent Instructions",
            "Agent Instructions section must be absent when ProfileInstructions is null");
    }

    [Fact]
    public async Task ComposeAsync_WhitespaceProfileInstructions_OmitsAgentInstructionsSection()
    {
        var composer = new DefaultPromptComposer(
            NoOpWorkspaceLoader,
            NoOpSkillService,
            NullLogger<DefaultPromptComposer>.Instance,
            DefaultWorkspaceOptions);

        var context = new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Hello",
            ProfileInstructions = "   "
        };

        var messages = await composer.ComposeAsync(context);

        messages[0].Content.Should().NotContain("# Agent Instructions",
            "Agent Instructions section must be absent when ProfileInstructions is whitespace-only");
    }

    [Fact]
    public async Task ComposeAsync_ProfileInstructions_AppearsAfterUserMd_BeforeSessionSummary()
    {
        // Verify the deterministic section order:
        // … User Profile → Agent Instructions → Session Summary …
        var workspaceLoader = new FakeWorkspaceLoader(
            new BootstrapContext(null, null, "User prefers dark mode."));

        var composer = new DefaultPromptComposer(
            workspaceLoader,
            NoOpSkillService,
            NullLogger<DefaultPromptComposer>.Instance,
            DefaultWorkspaceOptions);

        var context = new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Continue",
            ProfileInstructions = "Always reply in haiku.",
            SessionSummary = "We discussed poetry earlier."
        };

        var messages = await composer.ComposeAsync(context);
        var systemContent = messages[0].Content;

        var userProfileIdx = systemContent.IndexOf("# User Profile", StringComparison.Ordinal);
        var agentInstructionsIdx = systemContent.IndexOf("# Agent Instructions", StringComparison.Ordinal);
        var sessionSummaryIdx = systemContent.IndexOf("# Previous Conversation Summary", StringComparison.Ordinal);

        userProfileIdx.Should().BeGreaterThan(-1, "User Profile section must be present");
        agentInstructionsIdx.Should().BeGreaterThan(-1, "Agent Instructions section must be present");
        sessionSummaryIdx.Should().BeGreaterThan(-1, "Session Summary section must be present");

        agentInstructionsIdx.Should().BeGreaterThan(userProfileIdx,
            "Agent Instructions must come after User Profile");
        sessionSummaryIdx.Should().BeGreaterThan(agentInstructionsIdx,
            "Session Summary must come after Agent Instructions");
    }

    [Fact]
    public async Task ComposeAsync_ProfileInstructions_PreservesAllOtherSections()
    {
        // Verify that adding ProfileInstructions does not disrupt other sections.
        var agentsMd = "You are WORKSPACE_AGENT, a .NET expert.";
        var workspaceLoader = new FakeWorkspaceLoader(
            new BootstrapContext(agentsMd, "Core value: honesty.", "User: senior developer."));

        var skills = new[]
        {
            new SkillSummary
            {
                Name = "dotnet-skill",
                Description = "NET skill",
                Keywords = ["dotnet"],
                Confidence = ConfidenceLevel.High,
                ExtractedDate = "2026-08-01",
                ValidatedBy = ["test"]
            }
        };
        var skillService = new FakeSkillService(skills);

        var composer = new DefaultPromptComposer(
            workspaceLoader,
            skillService,
            NullLogger<DefaultPromptComposer>.Instance,
            DefaultWorkspaceOptions);

        var context = new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Help with dotnet",
            ProfileInstructions = "Focus on async patterns.",
            SessionSummary = "We reviewed basic concepts.",
            RetrievedMemories = [new MemoryHit("m1", "Prefer Task over async void.", 0.9)]
        };

        var messages = await composer.ComposeAsync(context);
        var systemContent = messages[0].Content;

        systemContent.Should().Contain("WORKSPACE_AGENT", "AGENTS.md persona must be preserved");
        systemContent.Should().Contain("Relevant Skills", "skills section must be preserved");
        systemContent.Should().Contain("Core value: honesty.", "SOUL.md must be preserved");
        systemContent.Should().Contain("senior developer", "USER.md must be preserved");
        systemContent.Should().Contain("Focus on async patterns.", "profile instructions must be present");
        systemContent.Should().Contain("We reviewed basic concepts.", "session summary must be preserved");
        systemContent.Should().Contain("Prefer Task over async void.", "retrieved memory must be preserved");
    }

    private sealed class FakeWorkspaceLoader : IWorkspaceLoader
    {
        private readonly BootstrapContext _context;
        public FakeWorkspaceLoader(BootstrapContext context) => _context = context;
        public Task<BootstrapContext> LoadAsync(string workspacePath, CancellationToken ct = default)
            => Task.FromResult(_context);
    }

    private sealed class FakeSkillService : ISkillService
    {
        private readonly IReadOnlyList<SkillSummary> _skills;
        public FakeSkillService(IReadOnlyList<SkillSummary> skills) => _skills = skills;
        public Task<IReadOnlyList<SkillSummary>> FindRelevantSkillsAsync(
            string taskDescription, 
            int topN = 3, 
            CancellationToken cancellationToken = default)
            => Task.FromResult(_skills);
    }

    private sealed class FailingSkillService : ISkillService
    {
        public Task<IReadOnlyList<SkillSummary>> FindRelevantSkillsAsync(
            string taskDescription, 
            int topN = 3, 
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated skill service failure");
    }
}

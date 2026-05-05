using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.Agent;

public sealed class DefaultPromptComposer : IPromptComposer
{
    private readonly IWorkspaceLoader _workspaceLoader;
    private readonly ISkillService _skillService;
    private readonly ILogger<DefaultPromptComposer> _logger;
    private readonly WorkspaceOptions _workspaceOptions;
    private readonly ISemanticSkillRanker? _semanticRanker;

    private string BuildFallbackSystemPrompt() => $"""
        You are OpenClaw .NET, an AI agent built with .NET and Microsoft Agent Framework.
        You help with coding, file operations, project analysis, and software development tasks.

        ## Workspace
        Your current workspace root is: {_workspaceOptions.WorkspacePath}

        ## Tool Usage Rules
        You have access to tools. Use them proactively when they help answer the user's request:

        - **file_system**: Use whenever asked about files, directories, or .NET projects on the local machine.
          - To find .NET projects in a solution: action=find_projects, path=<directory or '.' for workspace>
          - To list a directory: action=list, path=<directory>
          - To read a file: action=read, path=<file path>
          - IMPORTANT: If the user says "in <path>" or "at <path>", pass that path to the tool directly.
          - Prefer calling the tool over asking the user to provide file contents manually.

        - **web_search**: Use for current events, documentation lookups, or anything requiring up-to-date info.

        ## Response Style
        Be concise and accurate. When using tools, show what you found. 
        If you cannot find something with one tool call, try a different path or approach.
        Do NOT ask the user to paste file contents — use the file_system tool to read them yourself.
        """;

    public DefaultPromptComposer(
        IWorkspaceLoader workspaceLoader,
        ISkillService skillService,
        ILogger<DefaultPromptComposer> logger,
        IOptions<WorkspaceOptions> workspaceOptions,
        ISemanticSkillRanker? semanticRanker = null)
    {
        _workspaceLoader = workspaceLoader;
        _skillService = skillService;
        _logger = logger;
        _workspaceOptions = workspaceOptions.Value;
        _semanticRanker = semanticRanker;
    }

    public async Task<IReadOnlyList<ChatMessage>> ComposeAsync(PromptContext context, CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>();

        // 1. Load workspace bootstrap files
        var bootstrap = await _workspaceLoader.LoadAsync(_workspaceOptions.WorkspacePath, cancellationToken);

        // 2. Build system prompt — prefer AGENTS.md persona, fall back to built-in default
        var systemContent = !string.IsNullOrWhiteSpace(bootstrap.AgentsMd)
            ? bootstrap.AgentsMd
            : BuildFallbackSystemPrompt();

        // 2.5. Inject relevant skills (Phase 1: keyword-based matching)
        var skillsSection = await EnrichPromptWithSkillsAsync(context.UserMessage, cancellationToken);
        if (!string.IsNullOrEmpty(skillsSection))
        {
            systemContent += $"\n\n{skillsSection}";
        }

        // 3. Append core values from SOUL.md
        if (!string.IsNullOrWhiteSpace(bootstrap.SoulMd))
        {
            systemContent += $"\n\n# Core Values\n{bootstrap.SoulMd}";
        }

        // 4. Append user profile from USER.md
        if (!string.IsNullOrWhiteSpace(bootstrap.UserMd))
        {
            systemContent += $"\n\n# User Profile\n{bootstrap.UserMd}";
        }

        // 5. Add session summary if available
        if (!string.IsNullOrEmpty(context.SessionSummary))
        {
            systemContent += $"\n\n# Previous Conversation Summary\n{context.SessionSummary}";
        }

        messages.Add(new ChatMessage { Role = ChatMessageRole.System, Content = systemContent });

        // 6. Add conversation history
        foreach (var msg in context.History)
        {
            messages.Add(msg);
        }

        // 7. Add current user message
        messages.Add(new ChatMessage { Role = ChatMessageRole.User, Content = context.UserMessage });

        return messages;
    }

    private async Task<string> EnrichPromptWithSkillsAsync(string taskDescription, CancellationToken cancellationToken)
    {
        try
        {
            var keywordSkills = await _skillService.FindRelevantSkillsAsync(taskDescription, topN: 3, cancellationToken);

            if (keywordSkills.Count == 0)
            {
                _logger.LogTrace("No relevant skills found for task");
                return string.Empty;
            }

            // Story 3 (#89): apply semantic re-ranking on top of keyword candidates.
            // Falls back to keyword order if ranker is unregistered, throws, or times out
            // (SemanticSkillRanker enforces a 100ms internal timeout for SLA compliance).
            var relevantSkills = await ApplySemanticRerankingAsync(taskDescription, keywordSkills, cancellationToken);

            var skillsText = new System.Text.StringBuilder();
            skillsText.AppendLine("# Relevant Skills");
            skillsText.AppendLine();
            skillsText.AppendLine("The following skills from the team's knowledge base may be relevant to this task:");
            skillsText.AppendLine();

            foreach (var skill in relevantSkills)
            {
                var skillPath = Path.Combine(".squad", "skills", skill.Name, "SKILL.md");

                var semanticMarker = skill.IsSemanticRanked ? " [semantic-ranked]" : "";
                skillsText.AppendLine($"- **{skill.Name}** (confidence: {skill.Confidence}{semanticMarker})");

                if (skill.IsSemanticRanked && skill.SemanticScore.HasValue)
                {
                    skillsText.AppendLine($"  - Semantic score: {skill.SemanticScore:F2}");
                }

                skillsText.AppendLine($"  - Keywords: {string.Join(", ", skill.Keywords)}");
                skillsText.AppendLine($"  - Path: `{skillPath}` — read this file for detailed implementation guidance");
                skillsText.AppendLine();
            }

            _logger.LogInformation("Injected {Count} relevant skills into prompt ({Semantic} semantic-ranked)",
                relevantSkills.Count,
                relevantSkills.Count(s => s.IsSemanticRanked));
            return skillsText.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Skill injection failed, continuing without skills");
            return string.Empty;
        }
    }

    private async Task<IReadOnlyList<SkillSummary>> ApplySemanticRerankingAsync(
        string taskDescription,
        IReadOnlyList<SkillSummary> keywordSkills,
        CancellationToken cancellationToken)
    {
        if (_semanticRanker is null)
        {
            _logger.LogDebug("ISemanticSkillRanker not registered; using keyword-only ranking ({Count} skills)", keywordSkills.Count);
            return keywordSkills;
        }

        try
        {
            var reranked = await _semanticRanker.RerankAsync(taskDescription, keywordSkills, cancellationToken);
            if (reranked is null || reranked.Count == 0)
            {
                _logger.LogWarning("Semantic ranker returned no results; falling back to keyword ranking");
                return keywordSkills;
            }
            return reranked;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Semantic ranker timed out; falling back to keyword-only ranking");
            return keywordSkills;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Semantic ranker threw; falling back to keyword-only ranking");
            return keywordSkills;
        }
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OpenClawNet.Agent;

public static class AgentServiceCollectionExtensions
{
    public static IServiceCollection AddAgentRuntime(this IServiceCollection services, IConfiguration? configuration = null)
    {
        // Workspace loader — singleton because it only reads files
        services.AddSingleton<IWorkspaceLoader, WorkspaceLoader>();

        // Workspace options — read from Agent:WorkspacePath, fall back to AppContext.BaseDirectory
        services.AddOptions<WorkspaceOptions>()
            .Configure<IConfiguration>((opts, cfg) =>
            {
                var path = cfg["Agent:WorkspacePath"];
                if (!string.IsNullOrWhiteSpace(path))
                    opts.WorkspacePath = path;
            });

        // K-1b — ISkillsRegistry + scoped OpenClawNetSkillsProvider are
        // registered by Skills.AddOpenClawNetSkills() at gateway boot. The
        // DefaultAgentRuntime takes the scoped provider as an optional ctor
        // dependency (null in test harnesses that don't call AddOpenClawNetSkills).

        services.AddScoped<IPromptComposer, DefaultPromptComposer>();
        services.AddScoped<ISkillService, DefaultSkillService>();

        // Issue #107 — DefaultSummaryService now reads the local-fallback model name
        // from the Summary config section instead of hard-coding "llama3.2".
        services.AddOptions<SummaryOptions>()
            .Configure<IConfiguration>((opts, cfg) =>
                cfg.GetSection(SummaryOptions.SectionName).Bind(opts));
        services.AddScoped<ISummaryService, DefaultSummaryService>();

        // Tool-approval coordinator — singleton because pending requests bridge across
        // an agent runtime call and an HTTP POST that resolves the user's decision.
        services.AddSingleton<OpenClawNet.Agent.ToolApproval.IToolApprovalCoordinator,
                              OpenClawNet.Agent.ToolApproval.ToolApprovalCoordinator>();

        // Concept-review §4a — sanitize every tool result before it re-enters the LLM context.
        // Feature 2 Story 2 — wire up configurable options for enhanced defenses.
        services.AddOptions<OpenClawNet.Agent.ToolApproval.ToolResultSanitizerOptions>()
            .Configure<IConfiguration>((opts, cfg) =>
                cfg.GetSection(OpenClawNet.Agent.ToolApproval.ToolResultSanitizerOptions.SectionName).Bind(opts));

        services.AddSingleton<OpenClawNet.Agent.ToolApproval.IToolResultSanitizer,
                              OpenClawNet.Agent.ToolApproval.DefaultToolResultSanitizer>();

        // Concept-review §4a (UX) — approval-prompt timeout (default 60s).
        services.AddOptions<OpenClawNet.Agent.ToolApproval.ToolApprovalOptions>()
            .Configure<IConfiguration>((opts, cfg) =>
                cfg.GetSection(OpenClawNet.Agent.ToolApproval.ToolApprovalOptions.SectionName).Bind(opts));

        // Internal runtime abstraction (implemented with an Agent Framework host adapter)
        services.AddScoped<IAgentRuntime, DefaultAgentRuntime>();

        // Public orchestration interface
        services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();

        return services;
    }
}


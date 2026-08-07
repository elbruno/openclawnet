using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenClawNet.Skills;

/// <summary>
/// K-1b #4 — OpenClawNet's <see cref="AIContextProvider"/>. One scoped
/// instance per request (per K-D-1). For each invocation:
/// <list type="number">
///   <item>Pins the registry's current snapshot via
///   <see cref="SkillsTurnPin"/> so the chat turn sees a stable view.</item>
///   <item>Reads the agent name from
///   <see cref="AIContextProvider.InvokingContext.Agent"/> to apply the
///   per-agent enabled.json overlay (Q1 opt-in).</item>
///   <item>Materializes filtered <see cref="LayeredSkill"/> records into
///   MAF <see cref="AgentInlineSkill"/> instances and builds a fresh
///   <see cref="AgentSkillsProvider"/> with
///   <see cref="AgentSkillsProviderOptions.DisableCaching"/> = <c>true</c>
///   (per K-D-1: no MAF-side caching — we own the cache via snapshots).</item>
///   <item>Delegates to the fresh provider's <see cref="AIContextProvider.InvokingAsync"/>
///   to produce the final <see cref="AIContext"/>.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>Logging policy (Q5): only skill names + counts are logged.
/// Resource and script bodies are NEVER logged.</para>
/// <para>If the agent has zero enabled skills, this provider returns an
/// empty <see cref="AIContext"/> rather than constructing the inner
/// provider — saves the AgentSkillsProvider construction cost on the
/// common "no skills enabled" path.</para>
/// </remarks>
public sealed class OpenClawNetSkillsProvider : AIContextProvider
{
    private readonly OpenClawNetSkillsRegistry _registry;
    private readonly SkillsTurnPin? _turnPin;
    private readonly string? _testAgentName;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<OpenClawNetSkillsProvider> _logger;

    /// <summary>K-1b #4 — DI ctor (scoped). Agent name resolved per-call from <c>InvokingContext.Agent</c>.</summary>
    public OpenClawNetSkillsProvider(
        OpenClawNetSkillsRegistry registry,
        SkillsTurnPin turnPin,
        ILoggerFactory? loggerFactory = null)
    {
        _registry = registry;
        _turnPin = turnPin;
        _testAgentName = null;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<OpenClawNetSkillsProvider>();
    }

    /// <summary>
    /// K-1b — Test-friendly ctor that bakes an agent name in (no TurnPin).
    /// Used by <c>OpenClawNetSkillsProviderTests</c> to exercise the
    /// per-agent overlay without spinning up the full DI scope. Production
    /// code should use the DI ctor above.
    /// </summary>
    public OpenClawNetSkillsProvider(
        OpenClawNetSkillsRegistry registry,
        string agentName,
        ILogger<OpenClawNetSkillsProvider>? logger = null)
    {
        _registry = registry;
        _turnPin = null;
        _testAgentName = agentName ?? throw new ArgumentNullException(nameof(agentName));
        _loggerFactory = NullLoggerFactory.Instance;
        _logger = logger ?? NullLogger<OpenClawNetSkillsProvider>.Instance;
    }

    /// <summary>
    /// K-1b — Returns the resolved skills for the baked-in test agent
    /// (test-friendly accessor; throws when constructed via the DI ctor
    /// without an agent name). The shape matches what would be projected
    /// onto MAF inline skills (name + full markdown body).
    /// </summary>
    public async Task<IReadOnlyList<ResolvedSkill>> GetEnabledSkillsAsync(CancellationToken ct = default)
    {
        var agentName = _testAgentName
            ?? throw new InvalidOperationException("GetEnabledSkillsAsync requires the test ctor with an agentName.");
        var snap = await _registry.GetSnapshotAsync(ct).ConfigureAwait(false);
        var enabledMap = _registry.GetEnabledMapForAgent(agentName);
        var list = new List<ResolvedSkill>(snap.Skills.Count);
        foreach (var record in snap.Skills)
        {
            if (!enabledMap.TryGetValue(record.Name, out var on) || !on) continue;
            list.Add(new ResolvedSkill(record.Name, record.Body));
        }
        return list;
    }

    /// <summary>
    /// K-1b — Test-only accessor returning the MAF provider options that
    /// <see cref="BuildAgentSkillsProviderAsync"/> would apply. Confirms
    /// K-D-1 contract: <c>DisableCaching = true</c>.
    /// </summary>
    public AgentSkillsProviderOptions GetMafProviderOptions()
        => new() { DisableCaching = true };

    /// <summary>
    /// K-1b — Builds a fresh MAF <see cref="AgentSkillsProvider"/> with
    /// <c>DisableCaching=true</c> for the baked-in test agent. Production
    /// callers go through <see cref="ProvideAIContextAsync"/> which builds
    /// equivalently per invocation.
    /// </summary>
    public async Task<AgentSkillsProvider> BuildAgentSkillsProviderAsync(CancellationToken ct = default)
    {
        var agentName = _testAgentName
            ?? throw new InvalidOperationException("BuildAgentSkillsProviderAsync requires the test ctor with an agentName.");
        var snap = await _registry.GetSnapshotAsync(ct).ConfigureAwait(false);
        var enabledMap = _registry.GetEnabledMapForAgent(agentName);
        var skills = new List<AgentSkill>(snap.Skills.Count);
        foreach (var record in snap.Skills)
        {
            if (!enabledMap.TryGetValue(record.Name, out var on) || !on) continue;
            skills.Add(MaterializeSkill(record));
        }
        return new AgentSkillsProvider(
            (IEnumerable<AgentSkill>)skills,
            GetMafProviderOptions(),
            _loggerFactory);
    }

    private static AgentSkill MaterializeSkill(ISkillRecord record)
    {
        var description = string.IsNullOrWhiteSpace(record.Description)
            ? record.Name // MAF requires non-empty description; fall back to name
            : record.Description;
        var frontmatter = new AgentSkillFrontmatter(
            record.Name,
            description,
            string.Empty);
        return new AgentInlineSkill(
            frontmatter,
            record.Body,
            System.Text.Json.JsonSerializerOptions.Default);
    }

    /// <inheritdoc/>
    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        // Pin the snapshot for this turn (Q2). First call wins; subsequent
        // calls in the same turn (e.g., tool-iteration sub-invocations) get
        // the same pinned reference even if the watcher rebuilds.
        var live = await _registry.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = _turnPin?.GetOrPin(live) ?? live;

        // DI flow uses InvokingContext.Agent.Name; test ctor bakes it in.
        var agentName = _testAgentName ?? context.Agent?.Name;
        if (string.IsNullOrWhiteSpace(agentName))
        {
            _logger.LogDebug("Skills provider invoked with no agent name; returning empty context.");
            return new AIContext();
        }

        // Apply per-agent enabled.json overlay against the pinned snapshot.
        // Iterating the cached map once is O(skills) and avoids per-skill
        // file IO inside the loop.
        var enabledMap = _registry.GetEnabledMapForAgent(agentName);
        var resolved = new List<(string Name, string Body)>(snapshot.Skills.Count);

        foreach (var record in snapshot.Skills)
        {
            if (!enabledMap.TryGetValue(record.Name, out var on) || !on)
                continue;

            try
            {
                resolved.Add((record.Name, record.Body));
            }
            catch (Exception ex)
            {
                // Per Q5: only log the skill name. Don't log body content.
                _logger.LogWarning(ex,
                    "Failed to materialize skill '{Skill}' for agent '{Agent}'; skipping.",
                    record.Name, agentName);
            }
        }

        if (resolved.Count == 0)
        {
            _logger.LogDebug(
                "Skills provider: agent '{Agent}' has 0 enabled skills (snapshot {SnapshotId}, total {Total}).",
                agentName, snapshot.SnapshotId, snapshot.Skills.Count);
            return new AIContext();
        }

        // K-2 — emit SkillSnapshotPinned for the audit trail. ChatId is best-
        // effort: MAF's InvokingContext doesn't expose a stable conversation
        // id pre-1.2, so fall back to "(unknown)" for now. K-future will plug
        // the real chat id when MAF surfaces it.
        var chatId = "(unknown)";
        _logger.SkillSnapshotPinned(snapshot.SnapshotId, agentName, chatId, resolved.Count);

        _logger.LogInformation(
            "Skills provider: agent '{Agent}' resolved {Count} skill(s) from snapshot {SnapshotId}.",
            agentName, resolved.Count, snapshot.SnapshotId);

        // K-1b W-7b — inject skill bodies DIRECTLY into AIContext.Instructions
        // so every enabled rule reaches the model on every turn. This replaces
        // the earlier delegation to MAF's AgentSkillsProvider, which used
        // progressive-disclosure (only name+description in the system prompt;
        // model has to call a `load_skill` tool to read the body). Progressive
        // disclosure does not match how OpenClawNet treats skills — they are
        // mandatory per-agent rules that must always be in scope, not
        // opt-in tool-loadable references.
        //
        // The output is wrapped in a minimal <available_skills> envelope so the
        // model can tell skill content from the operator's free-form
        // instructions, mirroring MAF's prompt scaffolding shape.
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You have access to the following agent skills. Treat each skill body");
        sb.AppendLine("as a mandatory instruction you must follow on every reply unless it");
        sb.AppendLine("conflicts with a higher-priority safety rule.");
        sb.AppendLine();
        sb.AppendLine("<available_skills>");
        foreach (var skill in resolved)
        {
            sb.AppendLine($"<skill name=\"{skill.Name}\">");
            sb.AppendLine(skill.Body);
            sb.AppendLine("</skill>");
        }
        sb.AppendLine("</available_skills>");

        return new AIContext
        {
            Instructions = sb.ToString()
        };
    }
}

/// <summary>
/// K-1b — A resolved skill projection (post layer-precedence + per-agent
/// enabled overlay), suitable for materialization into MAF inline skills.
/// </summary>
public sealed record ResolvedSkill(string Name, string Body);

using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Mcp.Abstractions;
using OpenClawChatMessage = OpenClawNet.Models.Abstractions.ChatMessage;
using OpenClawChatResponse = OpenClawNet.Models.Abstractions.ChatResponse;
using MEAIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ModelToolCall = OpenClawNet.Models.Abstractions.ToolCall;
using OpenClawNet.Agent.ToolApproval;
using OpenClawNet.Skills;

namespace OpenClawNet.Agent;

/// <summary>
/// Default agent runtime implementation using the Microsoft Agent Framework.
/// Uses <see cref="ChatClientAgent"/> for the first model call, then calls the adapter
/// directly for tool iterations. Skills enrichment via the K-1b OpenClawNetSkillsProvider
/// is wired into <c>ChatClientAgentOptions.AIContextProviders</c> once K-1b lands; until
/// then the array is empty and agents run with zero skill context providers.
/// </summary>
public sealed class DefaultAgentRuntime : IAgentRuntime
{
    private readonly ModelClientChatClientAdapter _adapter;
    private readonly ChatClientAgent _chatClientAgent;
    private readonly OpenClawNetSkillsProvider? _skillsProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IPromptComposer _promptComposer;
    private readonly IToolExecutor _toolExecutor;
    private readonly IToolRegistry _toolRegistry;
    private readonly IConversationStore _conversationStore;
    private readonly ISummaryService _summaryService;
    private readonly IAgentProvider? _agentProvider;
    private readonly IToolApprovalCoordinator _approvalCoordinator;
    private readonly IMcpToolProvider? _mcpToolProvider;
    private readonly IToolResultSanitizer? _sanitizer;
    private readonly IToolApprovalAuditor? _approvalAuditor;
    private readonly ToolApprovalOptions _approvalOptions;
    private readonly ILogger<DefaultAgentRuntime> _logger;
    private readonly List<AITool> _toolAIFunctions;

    // Bumped from 10 → 25 (April 2026): real-world jobs (e.g. "fetch URL → hash → write log")
    // can easily chain 6+ tool calls; 10 was hitting the ceiling and producing the
    // "I've reached the maximum number of tool iterations" fallback prematurely.
    private const int MaxToolIterations = 25;
    private const int KeepRecentMessagesAfterCompaction = 10;

    /// <summary>
    /// Bundled MCP server prefixes whose tools require user approval before execution.
    /// These correspond to the built-in servers that wrap potentially dangerous ITool
    /// implementations (BrowserTool, ShellTool, FileSystemTool, WebTool).
    /// </summary>
    private static readonly HashSet<string> _bundledMcpServersRequiringApproval = new(StringComparer.OrdinalIgnoreCase)
    {
        "browser",
        "shell",
        "file_system",
        "web"
    };

    /// <summary>
    /// Returns the storage-form name (<c>&lt;serverPrefix&gt;.&lt;toolName&gt;</c>) for a tool.
    /// MCP tools expose <see cref="IMcpAITool.StorageName"/>; legacy tools (today only
    /// the <c>scheduler</c>) are namespaced under the virtual <c>scheduler</c> server.
    /// </summary>
    internal static string GetStorageName(AITool tool)
    {
        if (tool is IMcpAITool mcp) return mcp.StorageName;
        var name = (tool as AIFunction)?.Name ?? tool.GetType().Name;
        return $"scheduler.{name}";
    }

    /// <summary>
    /// Applies the active profile's <c>EnabledTools</c> allow-list. Empty/null list ⇒ no
    /// filtering (back-compat). Logs INFO once when filtering is active and WARN once per
    /// request for every allow-list entry that doesn't resolve to a known tool.
    /// </summary>
    private List<AITool> FilterToolsForProfile(AgentContext context)
    {
        if (context.EnabledTools is not { Count: > 0 } allow)
            return _toolAIFunctions;

        var allowSet = new HashSet<string>(allow, StringComparer.Ordinal);
        var available = _toolAIFunctions
            .ToDictionary(GetStorageName, t => t, StringComparer.Ordinal);

        var filtered = new List<AITool>(allowSet.Count);
        foreach (var name in allowSet)
        {
            if (available.TryGetValue(name, out var tool))
                filtered.Add(tool);
            else
                _logger.LogWarning(
                    "AgentProfile.EnabledTools references unknown tool '{ToolName}' (interaction {InteractionId}).",
                    name, context.InteractionId);
        }

        _logger.LogInformation(
            "Agent profile '{Profile}' restricts tools to {Filtered} of {Total}",
            context.AgentProfileName ?? "(unnamed)", filtered.Count, _toolAIFunctions.Count);

        return filtered;
    }

    /// <summary>
    /// Determines if a tool requires user approval before execution.
    /// Checks the legacy tool registry first, then falls back to checking MCP server prefixes
    /// for bundled servers that wrap dangerous capabilities.
    /// </summary>
    /// <param name="toolName">The wire-form tool name (e.g., "browser_navigate" or "schedule").</param>
    /// <returns>True if the tool requires approval; false otherwise.</returns>
    private bool ToolRequiresApproval(string toolName)
    {
        // First, check the legacy tool registry (handles non-MCP tools like "schedule")
        var legacyMeta = _toolRegistry.GetTool(toolName)?.Metadata;
        if (legacyMeta is not null)
        {
            return legacyMeta.RequiresApproval;
        }

        // For MCP tools, the wire-form name is "<serverPrefix>_<toolName>" (e.g., "browser_navigate").
        // Extract the server prefix and check if it's a bundled server that requires approval.
        var underscoreIndex = toolName.IndexOf('_');
        if (underscoreIndex > 0)
        {
            var serverPrefix = toolName.Substring(0, underscoreIndex);
            if (_bundledMcpServersRequiringApproval.Contains(serverPrefix))
            {
                return true;
            }
        }

        // Unknown tool or non-bundled MCP server — default to no approval required
        return false;
    }

    /// <summary>
    /// Gets the tool description for display in approval requests.
    /// Checks the legacy tool registry first, then looks up MCP tools from the AITool list.
    /// </summary>
    /// <param name="toolName">The wire-form tool name (e.g., "browser_navigate").</param>
    /// <returns>The tool description, or null if not found.</returns>
    private string? GetToolDescription(string toolName)
    {
        // First, check the legacy tool registry
        var legacyMeta = _toolRegistry.GetTool(toolName)?.Metadata;
        if (legacyMeta is not null)
        {
            return legacyMeta.Description;
        }

        // Look up MCP tools from the AITool list
        var aiTool = _toolAIFunctions.FirstOrDefault(t => (t as AIFunction)?.Name == toolName) as AIFunction;
        return aiTool?.Description;
    }

    public DefaultAgentRuntime(
        IModelClient modelClient,
        IPromptComposer promptComposer,
        IToolExecutor toolExecutor,
        IToolRegistry toolRegistry,
        IConversationStore conversationStore,
        ISummaryService summaryService,
        IToolApprovalCoordinator approvalCoordinator,
        ILoggerFactory loggerFactory,
        ILogger<DefaultAgentRuntime> logger,
        IEnumerable<IAgentProvider>? agentProviders = null,
        IMcpToolProvider? mcpToolProvider = null,
        IToolResultSanitizer? sanitizer = null,
        IToolApprovalAuditor? approvalAuditor = null,
        Microsoft.Extensions.Options.IOptions<ToolApprovalOptions>? approvalOptions = null,
        // K-1b #4 — scoped MAF AIContextProvider, optional. When present,
        // it filters skills per agent (per-agent enabled.json overlay) and
        // pins a stable snapshot for the duration of each chat turn.
        // Placed last so existing positional callers (test harnesses)
        // continue to compile without changes.
        OpenClawNetSkillsProvider? openClawNetSkillsProvider = null)
    {
        _adapter = new ModelClientChatClientAdapter(modelClient);
        _mcpToolProvider = mcpToolProvider;
        _sanitizer = sanitizer;
        _approvalAuditor = approvalAuditor;
        _approvalOptions = approvalOptions?.Value ?? new ToolApprovalOptions();

        // Build ToolAIFunction wrappers for all registered tools so the model knows what's available.
        // Execution is still handled by our IToolExecutor — these wrappers only advertise tools.
        var legacyTools = toolRegistry.GetAllTools()
            .Select(t => (AITool)new ToolAIFunction(t, sanitizer));

        // PR-A: union MCP-provided tools with the legacy ITool wrappers. With zero
        // McpServerDefinition rows this is a no-op; PR-B seeds the bundled servers.
        // Block on the async call only at construction — the provider's cache makes
        // subsequent calls cheap, and the agent runtime itself is scoped per request.
        IReadOnlyList<AITool> mcpTools = Array.Empty<AITool>();
        if (mcpToolProvider is not null)
        {
            try
            {
                mcpTools = mcpToolProvider.GetAllToolsAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "MCP tool provider failed during agent construction; continuing with legacy tools only.");
            }
        }

        // PR-B: MCP wins on name collisions. Build the MCP set first, then drop any
        // legacy ToolAIFunction whose advertised name matches. Scheduler stays legacy
        // (no MCP wrapper exists for it per plan PR-B).
        var mcpNames = new HashSet<string>(
            mcpTools.Select(m => (m as AIFunction)?.Name ?? string.Empty)
                    .Where(n => !string.IsNullOrEmpty(n)),
            StringComparer.Ordinal);

        _toolAIFunctions = new List<AITool>(mcpTools);
        foreach (var legacy in legacyTools)
        {
            var name = (legacy as AIFunction)?.Name;
            if (name is not null && mcpNames.Contains(name))
            {
                logger.LogInformation("MCP tool '{Name}' overrides legacy tool of the same name", name);
                continue;
            }
            _toolAIFunctions.Add(legacy);
        }

        var agentOptions = new ChatClientAgentOptions
        {
            // K-1b #4 — wire the scoped OpenClawNetSkillsProvider (per K-D-1).
            // When the provider is null (DI not registered, e.g. test harness),
            // pass an empty list so behavior matches the K-1a state.
            AIContextProviders = openClawNetSkillsProvider is not null
                ? [openClawNetSkillsProvider]
                : [],
            // UseProvidedChatClientAsIs = true prevents the ChatClientAgent from wrapping our adapter
            // with a FunctionInvokingChatClient — we manage the tool loop ourselves.
            UseProvidedChatClientAsIs = true,
            ChatOptions = new ChatOptions { Tools = _toolAIFunctions }
        };
        _chatClientAgent = new ChatClientAgent(_adapter, agentOptions, loggerFactory, null);
        _skillsProvider = openClawNetSkillsProvider;
        _loggerFactory = loggerFactory;

        _promptComposer = promptComposer;
        _toolExecutor = toolExecutor;
        _toolRegistry = toolRegistry;
        _conversationStore = conversationStore;
        _summaryService = summaryService;
        _approvalCoordinator = approvalCoordinator;
        _logger = logger;

        // Resolve an IAgentProvider from the injected collection (Phase 1 — optional).
        // The last provider in the collection is typically the routing provider.
        _agentProvider = agentProviders?.LastOrDefault();
    }

    public async Task<AgentContext> ExecuteAsync(AgentContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting agent execution: InteractionId={InteractionId}, SessionId={SessionId}",
            context.InteractionId, context.SessionId);

        try
        {
            await _conversationStore.AddMessageAsync(context.SessionId, "user", context.UserMessage, cancellationToken: cancellationToken);

            var storedMessages = await _conversationStore.GetMessagesAsync(context.SessionId, cancellationToken);
            var history = storedMessages.Select(m => new OpenClawChatMessage
            {
                Role = Enum.Parse<ChatMessageRole>(m.Role, ignoreCase: true),
                Content = m.Content,
                ToolCallId = m.ToolCallId
            }).ToList();

            var summary = await _summaryService.SummarizeIfNeededAsync(context.SessionId, history, cancellationToken);

            if (summary is not null && history.Count > KeepRecentMessagesAfterCompaction)
            {
                _logger.LogDebug("Context compaction: pruning old messages for SessionId={SessionId}, keeping last {Count}",
                    context.SessionId, KeepRecentMessagesAfterCompaction);
                await _conversationStore.PruneOldMessagesAsync(
                    context.SessionId, KeepRecentMessagesAfterCompaction, cancellationToken);
            }

            var toolManifest = _toolRegistry.GetToolManifest();
            var toolDefs = toolManifest
                .Select(t => new ToolDefinition
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = t.ParameterSchema
                }).ToList();

            var promptContext = new PromptContext
            {
                SessionId = context.SessionId,
                UserMessage = context.UserMessage,
                History = history.SkipLast(1).ToList(),
                SessionSummary = summary
            };

            var messages = await _promptComposer.ComposeAsync(promptContext, cancellationToken);
            context.ComposedMessages = messages;
            context.AvailableTools = toolDefs;

            var allToolResults = new List<ToolResult>();
            var totalTokens = 0;
            var iterations = 0;
            var currentMessages = messages.ToList();
            var executedToolCalls = new List<ToolCall>();
            // AC-WIRE-1/2 — build a per-turn ChatClientAgent with Name set from
            // the resolved AgentProfile so OpenClawNetSkillsProvider can resolve
            // the per-agent enabled.json overlay via InvokingContext.Agent.Name.
            // The shared _chatClientAgent (no name) stays as a fallback for
            // test harnesses that don't set a profile.
            var turnAgent = BuildAgentForTurn(context.AgentProfileName);
            var agentSession = await turnAgent.CreateSessionAsync(cancellationToken);
            var effectiveTools = FilterToolsForProfile(context);
            var chatOptions = new ChatOptions { Tools = effectiveTools };

            while (iterations < MaxToolIterations)
            {
                _logger.LogDebug("Invoking model: model={Model}, iterations={Iteration}", context.ModelName, iterations);

                OpenClawChatResponse response;
                if (iterations == 0)
                    response = await InvokeAgentFirstCallAsync(currentMessages, turnAgent, agentSession, chatOptions, cancellationToken);
                else
                    response = await InvokeAdapterCallAsync(currentMessages, chatOptions, cancellationToken);

                totalTokens += response.Usage?.TotalTokens ?? 0;

                if (response.ToolCalls is { Count: > 0 })
                {
                    currentMessages.Add(new OpenClawChatMessage
                    {
                        Role = ChatMessageRole.Assistant,
                        Content = response.Content ?? string.Empty,
                        ToolCalls = response.ToolCalls
                    });

                    foreach (var toolCall in response.ToolCalls)
                    {
                        _logger.LogDebug("Executing tool: {ToolName}", toolCall.Name);
                        var result = await _toolExecutor.ExecuteAsync(toolCall.Name, toolCall.Arguments, cancellationToken);
                        allToolResults.Add(result);
                        executedToolCalls.Add(new ToolCall { Id = toolCall.Id, Name = toolCall.Name, Arguments = toolCall.Arguments });

                        currentMessages.Add(new OpenClawChatMessage
                        {
                            Role = ChatMessageRole.Tool,
                            Content = _sanitizer is not null
                                ? _sanitizer.Sanitize(result.Success ? result.Output : $"Error: {result.Error}", toolCall.Name)
                                : (result.Success ? result.Output : $"Error: {result.Error}"),
                            ToolCallId = toolCall.Id
                        });
                    }

                    iterations++;
                }
                else
                {
                    var content = response.Content ?? string.Empty;
                    await _conversationStore.AddMessageAsync(context.SessionId, "assistant", content, cancellationToken: cancellationToken);

                    context.FinalResponse = content;
                    context.ToolResults = allToolResults;
                    context.ExecutedToolCalls = executedToolCalls;
                    context.TotalTokens = totalTokens;
                    context.IsComplete = true;
                    context.CompletedAt = DateTime.UtcNow;

                    _logger.LogDebug("Agent execution completed: InteractionId={InteractionId}, ToolCount={ToolCount}, Tokens={Tokens}",
                        context.InteractionId, executedToolCalls.Count, totalTokens);

                    return context;
                }
            }

            var fallback = "I've reached the maximum number of tool iterations. Here's what I've done so far.";
            await _conversationStore.AddMessageAsync(context.SessionId, "assistant", fallback, cancellationToken: cancellationToken);

            context.FinalResponse = fallback;
            context.ToolResults = allToolResults;
            context.ExecutedToolCalls = executedToolCalls;
            context.TotalTokens = totalTokens;
            context.IsComplete = true;
            context.CompletedAt = DateTime.UtcNow;

            _logger.LogWarning("Agent execution reached max iterations: InteractionId={InteractionId}", context.InteractionId);

            return context;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent execution failed: InteractionId={InteractionId}", context.InteractionId);
            throw;
        }
    }

    public async IAsyncEnumerable<AgentStreamEvent> ExecuteStreamAsync(AgentContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting streaming execution: InteractionId={InteractionId}, SessionId={SessionId}", context.InteractionId, context.SessionId);

        AgentStreamEvent? errorEvent = null;

        try
        {
            await _conversationStore.AddMessageAsync(context.SessionId, "user", context.UserMessage, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store user message: SessionId={SessionId}", context.SessionId);
            errorEvent = new AgentStreamEvent { Type = AgentStreamEventType.Error, Content = $"Storage error: {ex.Message}" };
        }

        if (errorEvent is not null)
        {
            yield return errorEvent;
            yield break;
        }

        List<OpenClawChatMessage> currentMessages;
        ChatOptions? chatOptions = null;
        ChatClientAgent? streamingAgent = null;
        AgentStreamEvent? setupError = null;

        try
        {
            var storedMessages = await _conversationStore.GetMessagesAsync(context.SessionId, cancellationToken);
            var history = storedMessages.Select(m => new OpenClawChatMessage
            {
                Role = Enum.Parse<ChatMessageRole>(m.Role, ignoreCase: true),
                Content = m.Content,
                ToolCallId = m.ToolCallId
            }).ToList();

            var summary = await _summaryService.SummarizeIfNeededAsync(context.SessionId, history, cancellationToken);

            var toolManifest = _toolRegistry.GetToolManifest();
            var effectiveTools = FilterToolsForProfile(context);
            chatOptions = new ChatOptions { Tools = effectiveTools };

            // AC-WIRE-1/2 — build a per-turn ChatClientAgent with Name set from
            // the resolved AgentProfile so the OpenClawNetSkillsProvider's
            // AIContextProvider fires on the streaming route and can apply the
            // per-agent enabled.json overlay via InvokingContext.Agent.Name.
            streamingAgent = BuildAgentForTurn(context.AgentProfileName);

            var promptContext = new PromptContext
            {
                SessionId = context.SessionId,
                UserMessage = context.UserMessage,
                History = history.SkipLast(1).ToList(),
                SessionSummary = summary
            };

            var messages = await _promptComposer.ComposeAsync(promptContext, cancellationToken);
            currentMessages = messages.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prepare streaming context: SessionId={SessionId}", context.SessionId);
            setupError = new AgentStreamEvent { Type = AgentStreamEventType.Error, Content = $"Setup error: {ex.Message}" };
            currentMessages = [];
        }

        if (setupError is not null)
        {
            yield return setupError;
            yield break;
        }

        var allToolResults = new List<ToolResult>();
        var totalTokens = 0;
        var iterations = 0;

        while (iterations < MaxToolIterations)
        {
            var shouldBreak = false;
            var contentBuffer = string.Empty;
            // Coalesce streaming FunctionCallContent deltas: Microsoft.Extensions.AI emits
            // the same logical call across multiple updates. Keying by CallId (last-write-wins
            // on Name/Arguments) ensures one approval prompt per real tool call — not one per
            // delta. Without this, the UI receives N tool_approval events with N distinct
            // RequestIds and the user's "Approve" click ends up posting a Guid the runtime
            // is no longer awaiting.
            var streamedToolCallsById = new Dictionary<string, ModelToolCall>(StringComparer.Ordinal);
            AgentStreamEvent? streamError = null;

            // Yield content deltas immediately as they arrive from the model.
            // Uses MoveNextAsync pattern so we can catch errors without
            // violating C#'s no-yield-in-catch constraint.
            // AC-WIRE-1 — route through ChatClientAgent.RunStreamingAsync (NOT the
            // raw adapter) so the AIContextProvider pipeline (incl. our
            // OpenClawNetSkillsProvider) fires on the live streaming chat path.
            // The TurnPin held in scope ensures the snapshot is pinned ONCE per
            // turn even though RunStreamingAsync invokes the providers each
            // tool-iteration (AC-WIRE-3 / Q2 hot-reload safety).
            var aiMessages = currentMessages.Select(ModelClientChatClientAdapter.ToMEAIMessage).ToList();
            var runOptions = new ChatClientAgentRunOptions(chatOptions);
            var enumerator = streamingAgent!
                .RunStreamingAsync(aiMessages, session: null, runOptions, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            try
            {
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = await enumerator.MoveNextAsync();
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Streaming agent loop failed: InteractionId={InteractionId}, Iteration={Iteration}",
                            context.InteractionId, iterations);
                        streamError = new AgentStreamEvent
                        {
                            Type = AgentStreamEventType.Error,
                            Content = ex is HttpRequestException
                                ? $"Model provider unreachable ({ex.Message}). Is Ollama running with the correct model?"
                                : $"Agent error: {ex.Message}"
                        };
                        break;
                    }

                    if (!hasNext) break;

                    var update = enumerator.Current;
                    if (!string.IsNullOrEmpty(update.Text))
                    {
                        contentBuffer += update.Text;
                        // Yield each token immediately — true streaming to the client
                        yield return new AgentStreamEvent
                        {
                            Type = AgentStreamEventType.ContentDelta,
                            Content = update.Text
                        };
                    }
                    foreach (var fcc in update.Contents.OfType<FunctionCallContent>())
                    {
                        var callKey = fcc.CallId ?? $"__anon_{streamedToolCallsById.Count}";
                        streamedToolCallsById[callKey] = new ModelToolCall
                        {
                            Id = callKey,
                            Name = fcc.Name,
                            Arguments = JsonSerializer.Serialize(fcc.Arguments)
                        };
                    }
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }

            if (streamError is not null)
            {
                yield return streamError;
                yield break;
            }

            // Tool execution and completion — discrete events, deferred yield is fine
            var toolEventsToYield = new List<AgentStreamEvent>();
            var deniedSyntheticMessage = (string?)null;
            var execFailed = false;

            if (streamedToolCallsById.Count > 0)
            {
                var streamedToolCalls = streamedToolCallsById.Values.ToList();
                currentMessages.Add(new OpenClawChatMessage
                {
                    Role = ChatMessageRole.Assistant,
                    Content = contentBuffer,
                    ToolCalls = streamedToolCalls
                });

                foreach (var toolCall in streamedToolCalls)
                {
                    _logger.LogDebug("Executing tool: {ToolName}", toolCall.Name);

                    // Get legacy tool metadata for description (may be null for MCP tools)
                    var toolMeta = _toolRegistry.GetTool(toolCall.Name)?.Metadata;
                    
                    // Check if approval is required - uses helper that checks both legacy and MCP tools
                    var toolRequiresApproval = ToolRequiresApproval(toolCall.Name);

                    // ── Approval gate ────────────────────────────────────────────────
                    // Approval is requested when ALL of:
                    //   • The active AgentProfile.RequireToolApproval is true (master switch).
                    //   • The tool itself declares RequiresApproval = true (legacy or MCP).
                    //   • The tool isn't on the exempt list (e.g. `schedule`).
                    //   • The user hasn't already chosen "remember for session" for this tool.
                    var needsApproval = context.RequireToolApproval
                        && toolRequiresApproval
                        && !ToolApprovalExemptions.IsExempt(toolCall.Name)
                        && !_approvalCoordinator.IsToolApprovedForSession(context.SessionId, toolCall.Name);

                    // Concept-review §4a: session-memory hits are still audit-worthy events.
                    if (!needsApproval
                        && context.RequireToolApproval
                        && toolRequiresApproval
                        && _approvalCoordinator.IsToolApprovedForSession(context.SessionId, toolCall.Name)
                        && _approvalAuditor is not null)
                    {
                        _ = _approvalAuditor.RecordAsync(new ToolApprovalAuditEntry(
                            RequestId: Guid.Empty,
                            SessionId: context.SessionId,
                            ToolName: toolCall.Name,
                            AgentProfileName: context.AgentProfileName,
                            Approved: true,
                            RememberForSession: true,
                            Source: ToolApprovalAuditSource.SessionMemory,
                            ToolArgsJson: System.Text.Json.JsonSerializer.Serialize(toolCall.Arguments)),
                            CancellationToken.None);
                        
                        // Phase B: Emit tool_approval_resolved for auto-approved tools
                        yield return new AgentStreamEvent
                        {
                            Type = AgentStreamEventType.ToolApprovalResolved,
                            ToolName = toolCall.Name,
                            Approved = true,
                            DecisionSource = "SessionMemory",
                            DecidedAt = DateTime.UtcNow,
                            ToolArgsJson = System.Text.Json.JsonSerializer.Serialize(toolCall.Arguments),
                            RequestId = Guid.Empty
                        };
                    }

                    if (needsApproval)
                    {
                        var requestId = Guid.NewGuid();
                        _logger.LogDebug("Tool {ToolName} requires approval, RequestId={RequestId}", toolCall.Name, requestId);
                        
                        // Concept-review §4a/UX: enforce a configurable timeout so a stuck approval
                        // doesn't block the agent loop forever. 0/-ve seconds = wait indefinitely.
                        using var timeoutCts = new CancellationTokenSource();
                        if (_approvalOptions.TimeoutSeconds > 0)
                        {
                            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_approvalOptions.TimeoutSeconds));
                        }
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken, timeoutCts.Token);

                        var approvalTask = _approvalCoordinator.RequestApprovalAsync(requestId, linkedCts.Token);

                        DateTime? expiresAt = _approvalOptions.TimeoutSeconds > 0
                            ? DateTime.UtcNow.AddSeconds(_approvalOptions.TimeoutSeconds)
                            : null;

                        yield return new AgentStreamEvent
                        {
                            Type = AgentStreamEventType.ToolApprovalRequest,
                            ToolName = toolCall.Name,
                            ToolDescription = toolMeta?.Description ?? GetToolDescription(toolCall.Name),
                            ToolArgsJson = toolCall.Arguments,
                            RequestId = requestId,
                            ApprovalExpiresAt = expiresAt,
                        };

                        ApprovalDecision decision;
                        var timedOut = false;
                        try
                        {
                            decision = await approvalTask;
                            _logger.LogDebug("Approval decision received: Approved={Approved}, RequestId={RequestId}", decision.Approved, requestId);
                        }
                        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                        {
                            _logger.LogWarning("Tool approval timed out for RequestId={RequestId}", requestId);
                            // Timeout fired before the user responded — auto-deny.
                            timedOut = true;
                            decision = new ApprovalDecision(Approved: false, RememberForSession: false);
                        }
                        catch (OperationCanceledException)
                        {
                            yield break;
                        }

                        // Concept-review §4a: persist every decision, including timeouts.
                        if (_approvalAuditor is not null)
                        {
                            _ = _approvalAuditor.RecordAsync(new ToolApprovalAuditEntry(
                                RequestId: requestId,
                                SessionId: context.SessionId,
                                ToolName: toolCall.Name,
                                AgentProfileName: context.AgentProfileName,
                                Approved: decision.Approved,
                                RememberForSession: decision.RememberForSession,
                                Source: timedOut ? ToolApprovalAuditSource.Timeout : ToolApprovalAuditSource.User,
                                ToolArgsJson: System.Text.Json.JsonSerializer.Serialize(toolCall.Arguments)),
                                CancellationToken.None);
                        }
                        
                        // Phase B: Emit tool_approval_resolved event for UI bubble
                        var decisionSource = timedOut ? "Timeout" : 
                            (decision.RememberForSession ? "SessionMemory" : "User");
                        yield return new AgentStreamEvent
                        {
                            Type = AgentStreamEventType.ToolApprovalResolved,
                            ToolName = toolCall.Name,
                            Approved = decision.Approved,
                            DecisionSource = decisionSource,
                            DecidedAt = DateTime.UtcNow,
                            ToolArgsJson = System.Text.Json.JsonSerializer.Serialize(toolCall.Arguments),
                            RequestId = requestId
                        };

                        if (!decision.Approved)
                        {
                            deniedSyntheticMessage = timedOut
                                ? $"The tool '{toolCall.Name}' was auto-denied because the approval prompt timed out after {_approvalOptions.TimeoutSeconds}s."
                                : $"The tool '{toolCall.Name}' was denied by the user. The requested action was not executed.";
                            await _conversationStore.AddMessageAsync(context.SessionId, "assistant", deniedSyntheticMessage, cancellationToken: cancellationToken);

                            yield return new AgentStreamEvent
                            {
                                Type = AgentStreamEventType.Complete,
                                Content = deniedSyntheticMessage,
                                IsComplete = true
                            };

                            context.FinalResponse = deniedSyntheticMessage;
                            context.ToolResults = allToolResults;
                            context.TotalTokens = totalTokens;
                            context.IsComplete = true;
                            context.CompletedAt = DateTime.UtcNow;
                            yield break;
                        }

                        if (decision.RememberForSession)
                        {
                            _approvalCoordinator.RememberApproval(context.SessionId, toolCall.Name);
                        }
                    }

                    // Now execute — events go through the deferred list so any exception
                    // can be surfaced as a single Error event without violating the
                    // no-yield-in-catch rule.
                    toolEventsToYield.Add(new AgentStreamEvent
                    {
                        Type = AgentStreamEventType.ToolCallStart,
                        ToolName = toolCall.Name
                    });

                    ToolResult result;
                    var cancellationDuringExec = false;
                    try
                    {
                        result = await _toolExecutor.ExecuteAsync(toolCall.Name, toolCall.Arguments, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        cancellationDuringExec = true;
                        result = ToolResult.Fail(toolCall.Name, "Stream was cancelled", TimeSpan.Zero);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Tool execution failed: InteractionId={InteractionId}, Iteration={Iteration}",
                            context.InteractionId, iterations);
                        toolEventsToYield.Add(new AgentStreamEvent
                        {
                            Type = AgentStreamEventType.Error,
                            Content = $"Agent error: {ex.Message}"
                        });
                        execFailed = true;
                        break;
                    }

                    if (cancellationDuringExec)
                    {
                        toolEventsToYield.Add(new AgentStreamEvent
                        {
                            Type = AgentStreamEventType.Error,
                            Content = "Stream was cancelled"
                        });
                        foreach (var ev in toolEventsToYield) yield return ev;
                        cancellationToken.ThrowIfCancellationRequested();
                        yield break;
                    }

                    allToolResults.Add(result);

                    toolEventsToYield.Add(new AgentStreamEvent
                    {
                        Type = AgentStreamEventType.ToolCallComplete,
                        ToolName = toolCall.Name,
                        ToolResult = result
                    });

                    currentMessages.Add(new OpenClawChatMessage
                    {
                        Role = ChatMessageRole.Tool,
                        // Concept-review §4a: sanitize tool output before it re-enters the LLM context.
                        Content = _sanitizer is not null
                            ? _sanitizer.Sanitize(result.Success ? result.Output : $"Error: {result.Error}", toolCall.Name)
                            : (result.Success ? result.Output : $"Error: {result.Error}"),
                        ToolCallId = toolCall.Id
                    });
                }

                if (!execFailed)
                {
                    iterations++;
                }
            }
            else
            {
                var finalContent = contentBuffer;
                await _conversationStore.AddMessageAsync(context.SessionId, "assistant", finalContent, cancellationToken: cancellationToken);

                toolEventsToYield.Add(new AgentStreamEvent
                {
                    Type = AgentStreamEventType.Complete,
                    Content = finalContent,
                    IsComplete = true
                });

                context.FinalResponse = finalContent;
                context.ToolResults = allToolResults;
                context.TotalTokens = totalTokens;
                context.IsComplete = true;
                context.CompletedAt = DateTime.UtcNow;

                shouldBreak = true;
            }

            foreach (var @event in toolEventsToYield)
            {
                yield return @event;
            }

            if (execFailed || shouldBreak)
            {
                yield break;
            }
        }

        var fallbackMsg = "I've reached the maximum number of tool iterations.";
        await _conversationStore.AddMessageAsync(context.SessionId, "assistant", fallbackMsg, cancellationToken: cancellationToken);

        yield return new AgentStreamEvent
        {
            Type = AgentStreamEventType.Complete,
            Content = fallbackMsg,
            IsComplete = true
        };

        context.FinalResponse = fallbackMsg;
        context.ToolResults = allToolResults;
        context.TotalTokens = totalTokens;
        context.IsComplete = true;
        context.CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// AC-WIRE-1/2 — Builds a per-turn <see cref="ChatClientAgent"/> with
    /// <see cref="ChatClientAgentOptions.Name"/> set to the resolved agent
    /// profile name, and the same <see cref="OpenClawNetSkillsProvider"/>
    /// (when registered) wired in as an <see cref="AIContextProvider"/>.
    /// Used by both the streaming and non-streaming paths so MAF's context
    /// provider pipeline fires uniformly. When <paramref name="agentName"/>
    /// is null/whitespace (e.g. test harness), the agent is built without a
    /// name and the skills overlay short-circuits to empty.
    /// </summary>
    private ChatClientAgent BuildAgentForTurn(string? agentName)
    {
        var agentOptions = new ChatClientAgentOptions
        {
            Name = string.IsNullOrWhiteSpace(agentName) ? null : agentName,
            AIContextProviders = _skillsProvider is not null
                ? [_skillsProvider]
                : [],
            UseProvidedChatClientAsIs = true,
            ChatOptions = new ChatOptions { Tools = _toolAIFunctions }
        };
        return new ChatClientAgent(_adapter, agentOptions, _loggerFactory, null);
    }

    private async Task<OpenClawChatResponse> InvokeAgentFirstCallAsync(
        IReadOnlyList<OpenClawChatMessage> messages,
        ChatClientAgent agent,
        AgentSession? session,
        ChatOptions chatOptions,
        CancellationToken cancellationToken)
    {
        var aiMessages = messages.Select(ModelClientChatClientAdapter.ToMEAIMessage).ToList();
        // Override the agent's construction-time tool list with the per-request filtered set.
        var runOptions = new ChatClientAgentRunOptions(chatOptions);
        var agentResponse = await agent.RunAsync(aiMessages, session, runOptions, cancellationToken);

        var content = agentResponse.Text ?? string.Empty;
        var toolCalls = agentResponse.Messages
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .Select(c => new ModelToolCall
            {
                Id = c.CallId ?? Guid.NewGuid().ToString("N"),
                Name = c.Name,
                Arguments = JsonSerializer.Serialize(c.Arguments)
            })
            .ToList();

        var usage = agentResponse.Usage is { } u
            ? new UsageInfo
            {
                PromptTokens = (int)(u.InputTokenCount ?? 0),
                CompletionTokens = (int)(u.OutputTokenCount ?? 0),
                TotalTokens = (int)(u.TotalTokenCount ?? 0)
            }
            : null;

        return new OpenClawChatResponse
        {
            Content = content,
            Role = ChatMessageRole.Assistant,
            Model = string.Empty,
            FinishReason = toolCalls.Count > 0 ? "tool_calls" : "stop",
            ToolCalls = toolCalls.Count > 0 ? toolCalls : null,
            Usage = usage
        };
    }

    private async Task<OpenClawChatResponse> InvokeAdapterCallAsync(
        IReadOnlyList<OpenClawChatMessage> messages,
        ChatOptions chatOptions,
        CancellationToken cancellationToken)
    {
        var aiMessages = messages.Select(ModelClientChatClientAdapter.ToMEAIMessage).ToList();
        var chatResponse = await _adapter.GetResponseAsync(aiMessages, chatOptions, cancellationToken);

        var content = chatResponse.Text ?? string.Empty;
        var toolCalls = chatResponse.Messages
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .Select(c => new ModelToolCall
            {
                Id = c.CallId ?? Guid.NewGuid().ToString("N"),
                Name = c.Name,
                Arguments = JsonSerializer.Serialize(c.Arguments)
            })
            .ToList();

        var usage = chatResponse.Usage is { } u
            ? new UsageInfo
            {
                PromptTokens = (int)(u.InputTokenCount ?? 0),
                CompletionTokens = (int)(u.OutputTokenCount ?? 0),
                TotalTokens = (int)(u.TotalTokenCount ?? 0)
            }
            : null;

        return new OpenClawChatResponse
        {
            Content = content,
            Role = ChatMessageRole.Assistant,
            Model = string.Empty,
            FinishReason = toolCalls.Count > 0 ? "tool_calls" : "stop",
            ToolCalls = toolCalls.Count > 0 ? toolCalls : null,
            Usage = usage
        };
    }
}
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

namespace OpenClawNet.Agent;

/// <summary>
/// Default agent runtime implementation using the Microsoft Agent Framework.
/// Uses <see cref="ChatClientAgent"/> with <see cref="AgentSkillsProvider"/> for the first model call
/// (skills enrichment via progressive disclosure), then calls the adapter directly for tool iterations.
/// </summary>
public sealed class DefaultAgentRuntime : IAgentRuntime
{
    private readonly ModelClientChatClientAdapter _adapter;
    private readonly ChatClientAgent _chatClientAgent;
    private readonly IPromptComposer _promptComposer;
    private readonly IToolExecutor _toolExecutor;
    private readonly IToolRegistry _toolRegistry;
    private readonly IConversationStore _conversationStore;
    private readonly ISummaryService _summaryService;
    private readonly IAgentProvider? _agentProvider;
    private readonly IToolApprovalCoordinator _approvalCoordinator;
    private readonly IMcpToolProvider? _mcpToolProvider;
    private readonly ILogger<DefaultAgentRuntime> _logger;
    private readonly List<AITool> _toolAIFunctions;

    private const int MaxToolIterations = 10;
    private const int KeepRecentMessagesAfterCompaction = 10;

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

    public DefaultAgentRuntime(
        IModelClient modelClient,
        IPromptComposer promptComposer,
        IToolExecutor toolExecutor,
        IToolRegistry toolRegistry,
        IConversationStore conversationStore,
        ISummaryService summaryService,
        AgentSkillsProvider agentSkillsProvider,
        IToolApprovalCoordinator approvalCoordinator,
        ILoggerFactory loggerFactory,
        ILogger<DefaultAgentRuntime> logger,
        IEnumerable<IAgentProvider>? agentProviders = null,
        IMcpToolProvider? mcpToolProvider = null)
    {
        _adapter = new ModelClientChatClientAdapter(modelClient);
        _mcpToolProvider = mcpToolProvider;

        // Build ToolAIFunction wrappers for all registered tools so the model knows what's available.
        // Execution is still handled by our IToolExecutor — these wrappers only advertise tools.
        var legacyTools = toolRegistry.GetAllTools()
            .Select(t => (AITool)new ToolAIFunction(t));

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
            AIContextProviders = [agentSkillsProvider],
            // UseProvidedChatClientAsIs = true prevents the ChatClientAgent from wrapping our adapter
            // with a FunctionInvokingChatClient — we manage the tool loop ourselves.
            UseProvidedChatClientAsIs = true,
            ChatOptions = new ChatOptions { Tools = _toolAIFunctions }
        };
        _chatClientAgent = new ChatClientAgent(_adapter, agentOptions, loggerFactory, null);

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
            var agentSession = await _chatClientAgent.CreateSessionAsync(cancellationToken);
            var effectiveTools = FilterToolsForProfile(context);
            var chatOptions = new ChatOptions { Tools = effectiveTools };

            while (iterations < MaxToolIterations)
            {
                _logger.LogDebug("Invoking model: model={Model}, iterations={Iteration}", context.ModelName, iterations);

                OpenClawChatResponse response;
                if (iterations == 0)
                    response = await InvokeAgentFirstCallAsync(currentMessages, agentSession, chatOptions, cancellationToken);
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
                            Content = result.Success ? result.Output : $"Error: {result.Error}",
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
            var streamedToolCalls = new List<ModelToolCall>();
            AgentStreamEvent? streamError = null;

            // Yield content deltas immediately as they arrive from the model.
            // Uses MoveNextAsync pattern so we can catch errors without
            // violating C#'s no-yield-in-catch constraint.
            var aiMessages = currentMessages.Select(ModelClientChatClientAdapter.ToMEAIMessage).ToList();
            var enumerator = _adapter.GetStreamingResponseAsync(aiMessages, chatOptions, cancellationToken).GetAsyncEnumerator(cancellationToken);
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
                        streamedToolCalls.Add(new ModelToolCall
                        {
                            Id = fcc.CallId ?? Guid.NewGuid().ToString("N"),
                            Name = fcc.Name,
                            Arguments = JsonSerializer.Serialize(fcc.Arguments)
                        });
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

            if (streamedToolCalls.Count > 0)
            {
                currentMessages.Add(new OpenClawChatMessage
                {
                    Role = ChatMessageRole.Assistant,
                    Content = contentBuffer,
                    ToolCalls = streamedToolCalls
                });

                foreach (var toolCall in streamedToolCalls)
                {
                    _logger.LogDebug("Executing tool: {ToolName}", toolCall.Name);

                    var toolMeta = _toolRegistry.GetTool(toolCall.Name)?.Metadata;

                    // ── Approval gate ────────────────────────────────────────────────
                    // Approval is requested when ALL of:
                    //   • The active AgentProfile.RequireToolApproval is true (master switch).
                    //   • The tool itself declares RequiresApproval = true.
                    //   • The tool isn't on the exempt list (e.g. `schedule`).
                    //   • The user hasn't already chosen "remember for session" for this tool.
                    var needsApproval = context.RequireToolApproval
                        && toolMeta?.RequiresApproval is true
                        && !ToolApprovalExemptions.IsExempt(toolCall.Name)
                        && !_approvalCoordinator.IsToolApprovedForSession(context.SessionId, toolCall.Name);

                    if (needsApproval)
                    {
                        var requestId = Guid.NewGuid();
                        // Register the pending request BEFORE yielding so the gateway endpoint
                        // can resolve it the instant the consumer reacts to the event.
                        // (Yielding gives control back to the consumer, which may POST a decision
                        //  faster than we'd reach the await line.)
                        var approvalTask = _approvalCoordinator.RequestApprovalAsync(requestId, cancellationToken);

                        yield return new AgentStreamEvent
                        {
                            Type = AgentStreamEventType.ToolApprovalRequest,
                            ToolName = toolCall.Name,
                            ToolDescription = toolMeta?.Description,
                            ToolArgsJson = toolCall.Arguments,
                            RequestId = requestId
                        };

                        ApprovalDecision decision;
                        try
                        {
                            decision = await approvalTask;
                        }
                        catch (OperationCanceledException)
                        {
                            yield break;
                        }

                        _logger.LogInformation("Tool approval decision received: ToolName={ToolName}, RequestId={RequestId}, Approved={Approved}, RememberForSession={RememberForSession}",
                            toolCall.Name, requestId, decision.Approved, decision.RememberForSession);

                        if (!decision.Approved)
                        {
                            deniedSyntheticMessage = $"The tool '{toolCall.Name}' was denied by the user. The requested action was not executed.";
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
                        Content = result.Success ? result.Output : $"Error: {result.Error}",
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

    private async Task<OpenClawChatResponse> InvokeAgentFirstCallAsync(
        IReadOnlyList<OpenClawChatMessage> messages,
        AgentSession? session,
        ChatOptions chatOptions,
        CancellationToken cancellationToken)
    {
        var aiMessages = messages.Select(ModelClientChatClientAdapter.ToMEAIMessage).ToList();
        // Override the agent's construction-time tool list with the per-request filtered set.
        var runOptions = new ChatClientAgentRunOptions(chatOptions);
        var agentResponse = await _chatClientAgent.RunAsync(aiMessages, session, runOptions, cancellationToken);

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
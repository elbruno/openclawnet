using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenClawNet.Storage;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Agent;

/// <summary>
/// Public orchestration interface for agent operations. Coordinates conversation, tool execution, and response generation.
/// Delegates internal execution to IAgentRuntime, which can be swapped for alternative implementations
/// (e.g., Microsoft Agent Framework integration) without changing this public interface.
/// </summary>
public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private readonly IAgentRuntime _runtime;
    private readonly IConversationStore _conversationStore;
    private readonly IWorkspaceLoader _workspaceLoader;
    private readonly WorkspaceOptions _workspaceOptions;
    private readonly IAgentContextAccessor? _agentContextAccessor;
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(
        IAgentRuntime runtime,
        IConversationStore conversationStore,
        IWorkspaceLoader workspaceLoader,
        IOptions<WorkspaceOptions> workspaceOptions,
        ILogger<AgentOrchestrator> logger,
        IAgentContextAccessor? agentContextAccessor = null)
    {
        _runtime = runtime;
        _conversationStore = conversationStore;
        _workspaceLoader = workspaceLoader;
        _workspaceOptions = workspaceOptions.Value;
        _agentContextAccessor = agentContextAccessor;
        _logger = logger;
    }

    private IDisposable PushAgentContext(string? agentProfileName)
    {
        if (_agentContextAccessor is null || string.IsNullOrWhiteSpace(agentProfileName))
            return NullDisposable.Instance;
        return _agentContextAccessor.Push(new AgentExecutionContext(agentProfileName));
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }

    public async Task<AgentResponse> ProcessAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing agent request: SessionId={SessionId}, Provider={Provider}",
            request.SessionId, request.Provider ?? "default");

        var context = new AgentContext
        {
            SessionId = request.SessionId,
            UserMessage = request.UserMessage,
            ModelName = request.Model ?? string.Empty,
            ProviderName = request.Provider,
            AgentProfileInstructions = request.AgentProfileInstructions,
            ResolvedProvider = request.ResolvedProvider,
            RequireToolApproval = request.RequireToolApproval,
            EnabledTools = request.EnabledTools,
            AgentProfileName = request.AgentProfileName
        };

        using var _agentScope = PushAgentContext(request.AgentProfileName);
        var executedContext = await _runtime.ExecuteAsync(context, cancellationToken);

        return new AgentResponse
        {
            Content = executedContext.FinalResponse ?? string.Empty,
            ToolResults = executedContext.ToolResults,
            ToolCallCount = executedContext.ExecutedToolCalls.Count,
            TotalTokens = executedContext.TotalTokens
        };
    }

    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(AgentRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Streaming agent request: SessionId={SessionId}",
            request.SessionId);

        var context = new AgentContext
        {
            SessionId = request.SessionId,
            UserMessage = request.UserMessage,
            ModelName = request.Model ?? string.Empty,
            ProviderName = request.Provider,
            AgentProfileInstructions = request.AgentProfileInstructions,
            ResolvedProvider = request.ResolvedProvider,
            RequireToolApproval = request.RequireToolApproval,
            EnabledTools = request.EnabledTools,
            AgentProfileName = request.AgentProfileName
        };

        await foreach (var @event in StreamWithAgentScopeAsync(context, request.AgentProfileName, cancellationToken))
        {
            yield return @event;
        }
    }

    private async IAsyncEnumerable<AgentStreamEvent> StreamWithAgentScopeAsync(
        AgentContext context,
        string? agentProfileName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var _agentScope = PushAgentContext(agentProfileName);
        await foreach (var @event in _runtime.ExecuteStreamAsync(context, cancellationToken))
        {
            yield return @event;
        }
    }

    public async Task<AgentResponse> ProcessIsolatedAsync(AgentRequest request, IsolatedSessionOptions options, CancellationToken ct = default)
    {
        // Generate a fresh session ID — isolated sessions never share history
        var isolatedSessionId = Guid.NewGuid();

        _logger.LogInformation(
            "Processing isolated agent request: IsolatedSessionId={SessionId}, Purpose={Purpose}, Persist={Persist}",
            isolatedSessionId, options.Purpose, options.PersistMessages);

        // If persistence is requested, create a proper session record
        if (options.PersistMessages)
        {
            await _conversationStore.CreateSessionAsync(
                title: $"[{options.Purpose}] {DateTime.UtcNow:u}",
                cancellationToken: ct);
        }

        // Choose the conversation store: persistent or in-memory
        // The runtime uses the injected IConversationStore, so for non-persistent sessions
        // we wrap execution using an in-memory store injected via the context's session ID.
        // Since the runtime will auto-create the session on first AddMessageAsync, we simply
        // use a transient session ID and delete it when done (if non-persistent).
        var context = new AgentContext
        {
            SessionId = isolatedSessionId,
            UserMessage = request.UserMessage,
            ModelName = request.Model ?? string.Empty,
            ProviderName = request.Provider,
            AgentProfileInstructions = request.AgentProfileInstructions,
            ResolvedProvider = request.ResolvedProvider,
            RequireToolApproval = request.RequireToolApproval,
            EnabledTools = request.EnabledTools,
            AgentProfileName = request.AgentProfileName
        };

        AgentContext executedContext;
        try
        {
            using var _agentScope = PushAgentContext(request.AgentProfileName);
            executedContext = await _runtime.ExecuteAsync(context, ct);
        }
        finally
        {
            // Clean up: if messages should not be persisted, remove the auto-created session
            if (!options.PersistMessages)
            {
                try
                {
                    await _conversationStore.DeleteSessionAsync(isolatedSessionId, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to clean up isolated session {SessionId} after non-persistent run",
                        isolatedSessionId);
                }
            }
        }

        return new AgentResponse
        {
            Content = executedContext.FinalResponse ?? string.Empty,
            ToolResults = executedContext.ToolResults,
            ToolCallCount = executedContext.ExecutedToolCalls.Count,
            TotalTokens = executedContext.TotalTokens
        };
    }
}

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenClawNet.Memory;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Tools.Memory;

/// <summary>
/// Read-path tool for the agent memory subsystem (issue #100). Performs a semantic
/// search against the per-agent vector store via <see cref="IAgentMemoryStore"/>
/// and returns the top-K hits as JSON the LLM can quote. Mirrors §11 of the
/// memory-service proposal.
/// </summary>
public sealed class RecallTool : ITool
{
    public const string ToolName = "recall";

    /// <summary>Default top-K when the LLM doesn't specify one.</summary>
    public const int DefaultTopK = 5;

    /// <summary>Hard upper bound on top-K to keep payloads bounded.</summary>
    public const int MaxTopK = 25;

    private readonly IAgentMemoryStore _store;
    private readonly IAgentContextAccessor _agentContextAccessor;
    private readonly ILogger<RecallTool> _logger;

    public RecallTool(
        IAgentMemoryStore store,
        IAgentContextAccessor agentContextAccessor,
        ILogger<RecallTool> logger)
    {
        _store = store;
        _agentContextAccessor = agentContextAccessor;
        _logger = logger;
    }

    public string Name => ToolName;

    public string Description =>
        "Retrieve previously-remembered facts, preferences, or observations relevant to a query " +
        "from the agent's long-term memory. Returns up to topK ranked hits with similarity scores. " +
        "Use this whenever the user asks about something they may have told you before, or when " +
        "context from prior sessions might help.";

    public ToolMetadata Metadata => new()
    {
        Name = Name,
        Description = Description,
        ParameterSchema = JsonDocument.Parse($$"""
        {
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "Natural-language search query."
                },
                "topK": {
                    "type": "integer",
                    "description": "Maximum number of hits to return. Defaults to {{DefaultTopK}}, capped at {{MaxTopK}}.",
                    "minimum": 1,
                    "maximum": {{MaxTopK}}
                }
            },
            "required": ["query"]
        }
        """),
        RequiresApproval = false,
        Category = "memory",
        Tags = ["memory", "recall", "search", "read"]
    };

    public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var query = input.GetStringArgument("query");
        if (string.IsNullOrWhiteSpace(query))
            return ToolResult.Fail(Name, "'query' is required", sw.Elapsed);

        var agentId = _agentContextAccessor.Current?.AgentId;
        if (string.IsNullOrWhiteSpace(agentId))
            return ToolResult.Fail(
                Name,
                "No active agent context — recall requires a scoped agent.",
                sw.Elapsed);

        var topK = DefaultTopK;
        try
        {
            var raw = input.GetArgument<int?>("topK");
            if (raw is { } v && v > 0) topK = Math.Min(v, MaxTopK);
        }
        catch (JsonException) { /* tolerate noisy LLM args */ }

        try
        {
            var hits = await _store.SearchAsync(agentId, query, topK, cancellationToken).ConfigureAwait(false);
            sw.Stop();

            var payload = new
            {
                agentId,
                query,
                topK,
                count = hits.Count,
                hits = hits.Select(h => new
                {
                    id = h.Id,
                    content = h.Content,
                    score = h.Score,
                    metadata = h.Metadata
                })
            };
            return ToolResult.Ok(Name, JsonSerializer.Serialize(payload), sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RecallTool failed for agent {AgentId}", agentId);
            return ToolResult.Fail(Name, $"Failed to search memory: {ex.Message}", sw.Elapsed);
        }
    }
}

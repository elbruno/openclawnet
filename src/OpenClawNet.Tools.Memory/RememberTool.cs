using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenClawNet.Memory;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Tools.Memory;

/// <summary>
/// Write-path tool for the agent memory subsystem (issue #100). Persists a salient
/// fact, observation, or instruction into the per-agent vector store via
/// <see cref="IAgentMemoryStore"/>. Schema mirrors §10 of the memory-service proposal.
/// </summary>
/// <remarks>
/// The agent identity used for isolation is sourced exclusively from the ambient
/// <see cref="IAgentContextAccessor"/>. Tool arguments may carry hint metadata but
/// never the agent id itself — that protects per-agent memory from cross-agent
/// access via crafted tool calls.
/// </remarks>
public sealed class RememberTool : ITool
{
    public const string ToolName = "remember";

    private readonly IAgentMemoryStore _store;
    private readonly IAgentContextAccessor _agentContextAccessor;
    private readonly ILogger<RememberTool> _logger;

    public RememberTool(
        IAgentMemoryStore store,
        IAgentContextAccessor agentContextAccessor,
        ILogger<RememberTool> logger)
    {
        _store = store;
        _agentContextAccessor = agentContextAccessor;
        _logger = logger;
    }

    public string Name => ToolName;

    public string Description =>
        "Store a salient fact, preference, or observation in the agent's long-term memory " +
        "so it can be recalled in future turns and sessions. Use for things the user explicitly " +
        "asks you to remember, or for stable facts about the user/project that will matter later. " +
        "Returns the memory id.";

    public ToolMetadata Metadata => new()
    {
        Name = Name,
        Description = Description,
        ParameterSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "content": {
                    "type": "string",
                    "description": "The fact or note to remember. Keep it short and self-contained."
                },
                "kind": {
                    "type": "string",
                    "description": "Optional category, e.g. 'fact', 'preference', 'episode'."
                },
                "importance": {
                    "type": "number",
                    "description": "Optional importance score in [0,1]. Defaults to 0.5.",
                    "minimum": 0,
                    "maximum": 1
                },
                "sourceSessionId": {
                    "type": "string",
                    "description": "Optional chat/session identifier this memory came from."
                }
            },
            "required": ["content"]
        }
        """),
        RequiresApproval = false,
        Category = "memory",
        Tags = ["memory", "remember", "write"]
    };

    public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var content = input.GetStringArgument("content");
        if (string.IsNullOrWhiteSpace(content))
            return ToolResult.Fail(Name, "'content' is required", sw.Elapsed);

        var agentId = _agentContextAccessor.Current?.AgentId;
        if (string.IsNullOrWhiteSpace(agentId))
            return ToolResult.Fail(
                Name,
                "No active agent context — remember requires a scoped agent.",
                sw.Elapsed);

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        var kind = input.GetStringArgument("kind");
        if (!string.IsNullOrWhiteSpace(kind)) metadata["kind"] = kind.Trim();

        var importance = 0.5;
        try
        {
            var parsed = input.GetArgument<double?>("importance");
            if (parsed is { } v)
            {
                importance = Math.Clamp(v, 0.0, 1.0);
            }
        }
        catch (JsonException) { /* tolerate noisy LLM args */ }
        metadata["importance"] = importance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var sourceSessionId = input.GetStringArgument("sourceSessionId");

        var entry = new MemoryEntry(
            Content: content,
            Kind: string.IsNullOrWhiteSpace(kind) ? null : kind.Trim(),
            Importance: importance,
            Timestamp: DateTimeOffset.UtcNow,
            SourceSessionId: string.IsNullOrWhiteSpace(sourceSessionId) ? null : sourceSessionId.Trim(),
            Metadata: metadata.Count == 0 ? null : metadata);

        try
        {
            var id = await _store.StoreAsync(agentId, entry, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            _logger.LogInformation(
                "RememberTool stored memory for agent {AgentId} memoryId {MemoryId} contentHash {ContentHash}",
                agentId, id, ComputeHash(content));

            var output = JsonSerializer.Serialize(new
            {
                id,
                agentId,
                stored = true
            });
            return ToolResult.Ok(Name, output, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RememberTool failed for agent {AgentId}", agentId);
            return ToolResult.Fail(Name, $"Failed to store memory: {ex.Message}", sw.Elapsed);
        }
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes[..8]);
    }
}

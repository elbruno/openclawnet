using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenClawNet.Memory;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Tools.Memory;

/// <summary>
/// Delete-path tool for the agent memory subsystem (issue #113). Removes a single
/// memory entry from the per-agent vector store via
/// <see cref="IAgentMemoryStore.DeleteAsync(string, string, CancellationToken)"/>.
/// Companion to <see cref="RememberTool"/> and <see cref="RecallTool"/>.
/// </summary>
/// <remarks>
/// Scope is intentionally narrow: callers must pass an explicit memory id (typically
/// obtained from a prior <c>recall</c> hit). Content-match, tag, and age-based
/// purge policies are deferred. The agent identity used for isolation is sourced
/// exclusively from the ambient <see cref="IAgentContextAccessor"/>, mirroring the
/// sibling tools — never accept agentId from tool arguments.
/// </remarks>
public sealed class ForgetTool : ITool
{
    public const string ToolName = "forget";

    private readonly IAgentMemoryStore _store;
    private readonly IAgentContextAccessor _agentContextAccessor;
    private readonly ILogger<ForgetTool> _logger;

    public ForgetTool(
        IAgentMemoryStore store,
        IAgentContextAccessor agentContextAccessor,
        ILogger<ForgetTool> logger)
    {
        _store = store;
        _agentContextAccessor = agentContextAccessor;
        _logger = logger;
    }

    public string Name => ToolName;

    public string Description =>
        "Delete a previously-remembered entry from the agent's long-term memory by its id. " +
        "Use this when the user explicitly asks you to forget something, or when a recalled " +
        "fact is stale or wrong. The id must come from a prior 'recall' hit.";

    public ToolMetadata Metadata => new()
    {
        Name = Name,
        Description = Description,
        ParameterSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "id": {
                    "type": "string",
                    "description": "The memory id to delete (as returned by 'recall' or 'remember')."
                }
            },
            "required": ["id"]
        }
        """),
        RequiresApproval = false,
        Category = "memory",
        Tags = ["memory", "forget", "delete", "write"]
    };

    public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var id = input.GetStringArgument("id");
        if (string.IsNullOrWhiteSpace(id))
            return ToolResult.Fail(Name, "'id' is required", sw.Elapsed);

        var agentId = _agentContextAccessor.Current?.AgentId;
        if (string.IsNullOrWhiteSpace(agentId))
            return ToolResult.Fail(
                Name,
                "No active agent context — forget requires a scoped agent.",
                sw.Elapsed);

        try
        {
            await _store.DeleteAsync(agentId, id, cancellationToken).ConfigureAwait(false);
            sw.Stop();

            var output = JsonSerializer.Serialize(new
            {
                id,
                agentId,
                deleted = true
            });
            return ToolResult.Ok(Name, output, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ForgetTool failed for agent {AgentId} id {MemoryId}", agentId, id);
            return ToolResult.Fail(Name, $"Failed to delete memory: {ex.Message}", sw.Elapsed);
        }
    }
}

using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenClawNet.Agent.ToolApproval;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Agent;

/// <summary>
/// Wraps an <see cref="ITool"/> as an <see cref="AIFunction"/> so the Microsoft Agent Framework
/// can advertise and invoke it through the standard tool-calling protocol.
/// </summary>
internal sealed class ToolAIFunction : AIFunction
{
    private readonly ITool _tool;
    private readonly IToolResultSanitizer? _sanitizer;

    public ToolAIFunction(ITool tool, IToolResultSanitizer? sanitizer = null)
    {
        _tool = tool;
        _sanitizer = sanitizer;
    }

    public override string Name => _tool.Name;
    public override string Description => _tool.Description;
    public override JsonElement JsonSchema => _tool.Metadata.ParameterSchema.RootElement;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(
            arguments.ToDictionary(k => k.Key, v => v.Value));
        var input = new ToolInput { ToolName = _tool.Name, RawArguments = json };
        var result = await _tool.ExecuteAsync(input, cancellationToken);
        var content = result.Success ? result.Output : $"Error: {result.Error}";
        return _sanitizer?.Sanitize(content, _tool.Name) ?? content;
    }
}

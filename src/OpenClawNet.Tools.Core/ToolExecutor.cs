using Microsoft.Extensions.Logging;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Tools.Core;

public sealed class ToolExecutor : IToolExecutor
{
    private readonly IToolRegistry _registry;
    private readonly IToolApprovalPolicy _approvalPolicy;
    private readonly ILogger<ToolExecutor> _logger;
    private readonly IToolExecutionLoggingState? _loggingState;
    
    public ToolExecutor(
        IToolRegistry registry,
        IToolApprovalPolicy approvalPolicy,
        ILogger<ToolExecutor> logger,
        IToolExecutionLoggingState? loggingState = null)
    {
        _registry = registry;
        _approvalPolicy = approvalPolicy;
        _logger = logger;
        _loggingState = loggingState;
    }
    
    public async Task<ToolResult> ExecuteAsync(string toolName, string arguments, CancellationToken cancellationToken = default)
    {
        var options = _loggingState?.Current ?? new ToolExecutionLoggingOptions();
        var invocationId = Guid.NewGuid().ToString("N");

        var tool = _registry.GetTool(toolName);
        if (tool is null)
        {
            return ToolResult.Fail(toolName, $"Tool '{toolName}' not found", TimeSpan.Zero);
        }
        
        // Check approval
        if (await _approvalPolicy.RequiresApprovalAsync(toolName, arguments) &&
            !await _approvalPolicy.IsApprovedAsync(toolName, arguments))
        {
            return ToolResult.Fail(toolName, $"Tool '{toolName}' requires approval", TimeSpan.Zero);
        }
        
        var input = new ToolInput { ToolName = toolName, RawArguments = arguments };

        if (options.Enabled)
        {
            _logger.LogInformation(
                "Tool invocation start [{InvocationId}] {ToolName}. ArgsLength={ArgsLength}, ArgsPreview={ArgsPreview}",
                invocationId,
                toolName,
                arguments?.Length ?? 0,
                Preview(arguments, options.ArgumentPreviewLength));
        }
        else
        {
            _logger.LogInformation("Executing tool {ToolName}", toolName);
        }
        
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await tool.ExecuteAsync(input, cancellationToken);
            sw.Stop();

            if (options.Enabled)
            {
                _logger.LogInformation(
                    "Tool invocation end [{InvocationId}] {ToolName}. Success={Success}, DurationMs={DurationMs}, OutputLength={OutputLength}, OutputPreview={OutputPreview}, Error={Error}",
                    invocationId,
                    toolName,
                    result.Success,
                    sw.ElapsedMilliseconds,
                    result.Output?.Length ?? 0,
                    Preview(result.Output, options.OutputPreviewLength),
                    result.Error ?? string.Empty);
            }
            else
            {
                _logger.LogInformation(
                    "Tool {ToolName} completed in {Duration}ms: Success={Success}",
                    toolName,
                    sw.ElapsedMilliseconds,
                    result.Success);
            }

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            if (options.Enabled)
            {
                _logger.LogError(
                    ex,
                    "Tool invocation failed [{InvocationId}] {ToolName} after {Duration}ms. ArgsPreview={ArgsPreview}",
                    invocationId,
                    toolName,
                    sw.ElapsedMilliseconds,
                    Preview(arguments, options.ArgumentPreviewLength));
            }
            else
            {
                _logger.LogError(ex, "Tool {ToolName} failed after {Duration}ms", toolName, sw.ElapsedMilliseconds);
            }

            return ToolResult.Fail(toolName, ex.Message, sw.Elapsed);
        }
    }
    
    public async Task<IReadOnlyList<ToolResult>> ExecuteBatchAsync(IReadOnlyList<(string ToolName, string Arguments)> calls, CancellationToken cancellationToken = default)
    {
        var results = new List<ToolResult>();
        foreach (var (toolName, arguments) in calls)
        {
            results.Add(await ExecuteAsync(toolName, arguments, cancellationToken));
        }
        return results;
    }

    private static string Preview(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.ReplaceLineEndings(" ").Trim();
        if (maxLength <= 0 || normalized.Length <= maxLength)
            return normalized;

        return normalized[..maxLength] + "...";
    }
}

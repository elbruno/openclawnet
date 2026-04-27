using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Tools.Shell;

public sealed class ShellTool : ITool
{
    private readonly IHttpClientFactory _factory;
    private readonly ILogger<ShellTool> _logger;

    public ShellTool(IHttpClientFactory factory, ILogger<ShellTool> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public string Name => "shell";
    public string Description => "Run shell commands (e.g., echo, ls, curl, dotnet). Executes arbitrary commands in an isolated shell service and returns stdout, stderr, and exit codes.";

    public ToolMetadata Metadata => new()
    {
        Name = Name,
        Description = Description,
        ParameterSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "command": { "type": "string", "description": "The command to execute" },
                "workingDirectory": { "type": "string", "description": "Working directory (optional)" }
            },
            "required": ["command"]
        }
        """),
        RequiresApproval = true,
        Category = "shell",
        Tags = ["shell", "command", "execute"]
    };

    public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var command = input.GetStringArgument("command");
            var workingDir = input.GetStringArgument("workingDirectory");
            if (string.IsNullOrEmpty(command))
                return ToolResult.Fail(Name, "'command' is required", sw.Elapsed);

            _logger.LogInformation("Forwarding shell command to shell-service: {Command}", command);
            var client = _factory.CreateClient("shell-service");
            var response = await client.PostAsJsonAsync("/api/shell/execute",
                new { command, workingDirectory = workingDir }, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ShellServiceResponse>(cancellationToken: cancellationToken);
            sw.Stop();

            if (result is null) return ToolResult.Fail(Name, "Empty response from shell service", sw.Elapsed);
            if (result.TimedOut) return ToolResult.Fail(Name, result.Stderr, sw.Elapsed);

            var output = result.Stdout;
            if (!string.IsNullOrEmpty(result.Stderr)) output += $"\n--- stderr ---\n{result.Stderr}";
            return result.Success
                ? ToolResult.Ok(Name, string.IsNullOrEmpty(output) ? "(no output)" : output, sw.Elapsed)
                : ToolResult.Fail(Name, $"Exit code {result.ExitCode}:\n{output}", sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Shell tool proxy error");
            return ToolResult.Fail(Name, $"Shell service unavailable: {ex.Message}", sw.Elapsed);
        }
    }

    private sealed record ShellServiceResponse(bool Success, int ExitCode, string Stdout, string Stderr, bool TimedOut);
}

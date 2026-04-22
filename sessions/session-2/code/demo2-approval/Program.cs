using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.Core;

// ──────────────────────────────────────────────────────────────────────
// Session 2 · Demo 2 — The approval gate
// ──────────────────────────────────────────────────────────────────────
// What this demo shows:
//   1. The same dangerous tool ("delete_file"), executed twice
//   2. First run: AlwaysApprovePolicy → succeeds (BAD for prod!)
//   3. Second run: DenyDangerousArgsPolicy → blocked at the gate
//
// Lesson: the security gate is a DI swap, not a code rewrite.
// ──────────────────────────────────────────────────────────────────────

await RunWithPolicy<AlwaysApprovePolicy>("Run #1 — AlwaysApprovePolicy (the default)");
Console.WriteLine();
await RunWithPolicy<DenyDangerousArgsPolicy>("Run #2 — DenyDangerousArgsPolicy (production-style)");

static async Task RunWithPolicy<TPolicy>(string banner)
    where TPolicy : class, IToolApprovalPolicy
{
    Console.WriteLine($"━━━ {banner} ━━━");

    var services = new ServiceCollection();
    services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
    services.AddToolFramework();
    // Override the default AlwaysApprovePolicy with whatever the caller asked for.
    services.RemoveAll<IToolApprovalPolicy>();
    services.AddSingleton<IToolApprovalPolicy, TPolicy>();
    services.AddTool<DeleteFileTool>();

    await using var sp = services.BuildServiceProvider();

    // Populate the registry from DI (the registry doesn't auto-discover ITool).
    var registry = sp.GetRequiredService<IToolRegistry>();
    foreach (var tool in sp.GetServices<ITool>())
        registry.Register(tool);

    var executor = sp.GetRequiredService<IToolExecutor>();

    var safeArgs    = JsonSerializer.Serialize(new { path = "C:\\temp\\junk.txt" });
    var dangerArgs  = JsonSerializer.Serialize(new { path = "C:\\Windows\\System32\\drivers\\etc\\hosts" });

    var safe   = await executor.ExecuteAsync("delete_file", safeArgs);
    Console.WriteLine($"  safe   → {(safe.Success ? "✅ ALLOWED" : "❌ BLOCKED")}: {Status(safe)}");
    var danger = await executor.ExecuteAsync("delete_file", dangerArgs);
    Console.WriteLine($"  danger → {(danger.Success ? "✅ ALLOWED" : "❌ BLOCKED")}: {Status(danger)}");

    static string Status(ToolResult r) => r.Success ? r.Output : (r.Error ?? "unknown");
}

// ────────────────── Custom approval policy ──────────────────

internal sealed class DenyDangerousArgsPolicy : IToolApprovalPolicy
{
    private static readonly string[] SensitivePathFragments =
    [
        "system32", "windows\\", "/etc/", ".ssh", "appdata\\local",
        "program files", "boot.ini"
    ];

    public Task<bool> RequiresApprovalAsync(string toolName, string arguments)
        => Task.FromResult(toolName == "delete_file");

    public Task<bool> IsApprovedAsync(string toolName, string arguments)
    {
        // Pretend an operator reviewed the args. They auto-deny anything that
        // touches a sensitive system path.
        var lower = arguments.ToLowerInvariant();
        var dangerous = SensitivePathFragments.Any(f => lower.Contains(f));
        return Task.FromResult(!dangerous);
    }
}

// ────────────────── Custom tool ──────────────────

internal sealed class DeleteFileTool : ITool
{
    public string Name => "delete_file";

    public string Description => "Delete a file at the given path. DANGEROUS — requires approval.";

    public ToolMetadata Metadata => new()
    {
        Name = Name,
        Description = Description,
        ParameterSchema = JsonDocument.Parse("""
        { "type":"object",
          "properties":{ "path":{"type":"string"} },
          "required":["path"] }
        """),
        RequiresApproval = true,         // The policy decides per-call
        Category = "files",
        Tags = ["destructive"]
    };

    public Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var path = input.GetStringArgument("path") ?? "<unknown>";
        // Demo only: we don't actually delete anything.
        return Task.FromResult(ToolResult.Ok(Name, $"would-delete: {path}", sw.Elapsed));
    }
}

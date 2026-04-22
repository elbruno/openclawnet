using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.Core;

// ──────────────────────────────────────────────────────────────────────
// Session 2 · Demo 1 — Implementing ITool
// ──────────────────────────────────────────────────────────────────────
// What this demo shows:
//   1. A 30-line custom ITool ("greeter") with parameter schema
//   2. How to register it via AddToolFramework + AddTool<T>
//   3. How to invoke it through IToolExecutor (the production path)
//
// Run:  dotnet run
// Try:  dotnet run -- "Bruno"
// ──────────────────────────────────────────────────────────────────────

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));

services.AddToolFramework();          // registry + executor + AlwaysApprovePolicy
services.AddTool<GreeterTool>();      // our custom tool

await using var sp = services.BuildServiceProvider();

// Populate the registry from DI (the registry doesn't auto-discover ITool).
// In the Gateway this happens once at startup in Program.cs.
var registry = sp.GetRequiredService<IToolRegistry>();
foreach (var tool in sp.GetServices<ITool>())
    registry.Register(tool);

// 1) Registry knows what exists.
Console.WriteLine("\n📚 Tools in registry:");
foreach (var t in registry.GetAllTools())
    Console.WriteLine($"   • {t.Name} — {t.Description}");

// 2) Executor knows how to run safely.
var executor = sp.GetRequiredService<IToolExecutor>();
var name = args.Length > 0 ? args[0] : "world";
var argsJson = JsonSerializer.Serialize(new { name, shout = true });

Console.WriteLine($"\n⚙️  Executing 'greeter' with: {argsJson}");
var result = await executor.ExecuteAsync("greeter", argsJson);

Console.WriteLine(result.Success
    ? $"\n✅ {result.Output}  (took {result.Duration.TotalMilliseconds:F1} ms)"
    : $"\n❌ {result.Error}");

// ────────────────── Custom tool ──────────────────

public sealed class GreeterTool : ITool
{
    public string Name => "greeter";

    public string Description =>
        "Greets a person by name. Set 'shout' to true to upper-case the result.";

    public ToolMetadata Metadata => new()
    {
        Name = Name,
        Description = Description,
        ParameterSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "name":  { "type": "string", "description": "Person to greet." },
                "shout": { "type": "boolean", "default": false }
            },
            "required": ["name"]
        }
        """),
        RequiresApproval = false,
        Category = "demo",
        Tags = ["greeting", "session-2"]
    };

    public Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var name = input.GetStringArgument("name");
        if (string.IsNullOrWhiteSpace(name))
            return Task.FromResult(ToolResult.Fail(Name, "'name' is required", sw.Elapsed));

        var shout = input.GetArgument<bool>("shout");
        var greeting = $"Hello, {name}!";
        if (shout) greeting = greeting.ToUpperInvariant();

        return Task.FromResult(ToolResult.Ok(Name, greeting, sw.Elapsed));
    }
}

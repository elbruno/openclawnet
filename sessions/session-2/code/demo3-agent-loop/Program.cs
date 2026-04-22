using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCalc;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Models.Ollama;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

// ──────────────────────────────────────────────────────────────────────
// Session 2 · Demo 3 — A real agent loop with Ollama + 2 tools
// ──────────────────────────────────────────────────────────────────────
// What this demo shows:
//   1. The agent loop, end-to-end:
//        prompt → model → tool calls → execute → loop
//   2. Two tools exposed to the LLM via Microsoft.Extensions.AI:
//        - calculator(expression)     — safe arithmetic
//        - now(timezone)              — current datetime in a TZ
//   3. The model decides which tool to call (or none).
//
// Prereq:
//   ollama pull gemma3:4b   (any tool-capable model works)
//
// Run:
//   dotnet run "What is sqrt(2) * pi rounded to 4 decimals?"
//   dotnet run "What time is it in Tokyo?"
// ──────────────────────────────────────────────────────────────────────

var prompt = args.Length > 0
    ? string.Join(' ', args)
    : "What is the square root of 2024?";

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

services.Configure<OllamaOptions>(o =>
{
    o.Endpoint = "http://localhost:11434";
    // Model must support tool calling. Good picks:
    //   gemma3:4b, llama3.1, qwen2.5, phi4
    o.Model = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "llama3.2";
});
services.AddSingleton<IAgentProvider, OllamaAgentProvider>();

await using var sp = services.BuildServiceProvider();

var provider = sp.GetRequiredService<IAgentProvider>();
if (!await provider.IsAvailableAsync())
{
    Console.Error.WriteLine("❌ Ollama is not reachable at http://localhost:11434");
    Console.Error.WriteLine("   Start it (e.g. `ollama serve`) and pull a tool-capable model:");
    Console.Error.WriteLine("   ollama pull gemma3:4b");
    return 1;
}

var profile = new AgentProfile
{
    Name = "session2-demo3",
    Provider = "ollama",
    Instructions = "You are a concise assistant. When a user asks anything that needs " +
                   "arithmetic or the current time, call the appropriate tool BEFORE replying."
};

using var raw = provider.CreateChatClient(profile);

// FunctionInvokingChatClient runs the tool-call loop for us so the loop is
// visible in logs but we don't have to hand-roll it. The same building blocks
// (IChatClient + AIFunction) are what OpenClawNet.Agent uses under the hood.
using var client = raw
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

// ── Tools the model can call ───────────────────────────────────────────

AIFunction calculator = AIFunctionFactory.Create(
    method: (string expression) =>
    {
        Console.WriteLine($"\n   🛠️  tool: calculator({expression})");
        try
        {
            // NCalc supports sqrt, abs, pow, sin, cos, log, etc.
            var expr = new Expression(expression, ExpressionOptions.IgnoreCaseAtBuiltInFunctions)
            {
                CultureInfo = CultureInfo.InvariantCulture
            };
            var value = expr.Evaluate();
            var result = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
            Console.WriteLine($"   ✅ result: {result}");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ error: {ex.Message}");
            return $"error: {ex.Message}";
        }
    },
    name: "calculator",
    description: "Evaluate an arithmetic expression. Supports operators (+, -, *, /, %, ^) and " +
                 "functions: sqrt, abs, pow, log, log10, exp, sin, cos, tan, min, max, round, floor, ceiling. " +
                 "Examples: 'sqrt(2024)', 'pow(2, 10)', '12 * (4 + 3) / 2'. " +
                 "Use whenever the answer requires math the model cannot do reliably.");

AIFunction now = AIFunctionFactory.Create(
    method: (string timezone) =>
    {
        Console.WriteLine($"\n   🛠️  tool: now({timezone})");
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            var t = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            var result = t.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture);
            Console.WriteLine($"   ✅ result: {result}");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ error: {ex.Message}");
            return $"error: {ex.Message}";
        }
    },
    name: "now",
    description: "Get the current local time in a given IANA/Windows time-zone id, " +
                 "e.g. 'Tokyo Standard Time', 'Pacific Standard Time', 'UTC'.");

// ── Run the loop ───────────────────────────────────────────────────────

Console.WriteLine($"📨 user: {prompt}\n");

var options = new ChatOptions
{
    Tools = [calculator, now],
    ToolMode = ChatToolMode.Auto
};

var messages = new List<ChatMessage>
{
    new(ChatRole.System, profile.Instructions),
    new(ChatRole.User,   prompt)
};

var response = await client.GetResponseAsync(messages, options);

Console.WriteLine($"\n💬 assistant:\n{response.Text}\n");
return 0;

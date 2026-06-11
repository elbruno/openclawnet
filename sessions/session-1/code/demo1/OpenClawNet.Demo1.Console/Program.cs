using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Models.Ollama;

// ──────────────────────────────────────────────
// Demo 1: Console Chat with IAgentProvider
// ──────────────────────────────────────────────
// This app uses the IAgentProvider abstraction to
// create an IChatClient and stream a chat response.
// Switch providers by uncommenting sections below.

// --- Configuration ---
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets<Program>(optional: true)
    .Build();

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

// ╔══════════════════════════════════════════════╗
// ║  PROVIDER 1: Ollama (Local LLM) — DEFAULT    ║
// ╚══════════════════════════════════════════════╝
// services.Configure<OllamaOptions>(o =>
// {
//     o.Endpoint = "http://localhost:11434";
//     o.Model = "llama3.2";   // Try: "phi4", "llama3.2", "gemma4:e4b"
// });
// services.AddSingleton<IAgentProvider, OllamaAgentProvider>();

// ╔══════════════════════════════════════════════╗
// ║  PROVIDER 2: Azure OpenAI (Cloud LLM)        ║
// ║  Uncomment below and comment out Ollama above ║
// ╚══════════════════════════════════════════════╝
// // Set credentials via User Secrets:
// //   dotnet user-secrets set "AzureOpenAI:Endpoint" "https://YOUR-RESOURCE.openai.azure.com/"
// //   dotnet user-secrets set "AzureOpenAI:ApiKey" "YOUR-KEY"
// //   dotnet user-secrets set "AzureOpenAI:DeploymentName" "gpt-5-mini"
// services.Configure<OpenClawNet.Models.AzureOpenAI.AzureOpenAIOptions>(config.GetSection("AzureOpenAI"));
// services.AddSingleton<IAgentProvider, OpenClawNet.Models.AzureOpenAI.AzureOpenAIAgentProvider>();

// ╔══════════════════════════════════════════════╗
// ║  PROVIDER 3: GitHub Copilot SDK               ║
// ║  Uncomment below and comment out others above  ║
// ╚══════════════════════════════════════════════╝
// // Auth: gh auth login  OR  set GitHubCopilot:GitHubToken via User Secrets
// // Uses your GitHub Copilot subscription — no separate API key needed.
// // Default model: gpt-5-mini (change via GitHubCopilot:Model)
services.Configure<OpenClawNet.Models.GitHubCopilot.GitHubCopilotOptions>(o =>
{
    o.Model = "gpt-5-mini"; // Try: "gpt-5", "claude-sonnet-4.5"
});
services.AddSingleton<IAgentProvider, OpenClawNet.Models.GitHubCopilot.GitHubCopilotAgentProvider>();

// --- Build the service provider ---
using var sp = services.BuildServiceProvider();
var provider = sp.GetRequiredService<IAgentProvider>();

Console.WriteLine($"🦀 OpenClaw .NET — Demo 1: Console Chat");
Console.WriteLine($"   Provider: {provider.ProviderName}");
Console.WriteLine();

// Check provider availability
if (!await provider.IsAvailableAsync())
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"❌ Provider '{provider.ProviderName}' is not available.");
    Console.WriteLine("   Make sure Ollama is running: ollama serve");
    Console.ResetColor();
    return;
}

// Create the agent profile and chat client
var profile = new AgentProfile
{
    Name = "demo-agent",
    DisplayName = "Demo Agent",
    Instructions = "You are a helpful .NET expert. Keep answers concise — 2-3 sentences max."
};

IChatClient chatClient = provider.CreateChatClient(profile);

// The question to ask
var question = "What is Aspire (formerly know as Aspire) and why should I use it? What is the latest version and when was it released?";

Console.ForegroundColor = ConsoleColor.Cyan;
Console.Write("You: ");
Console.ResetColor();
Console.WriteLine(question);
Console.WriteLine();

Console.ForegroundColor = ConsoleColor.Green;
Console.Write("Assistant: ");
Console.ResetColor();

// Stream the response token by token
await foreach (var update in chatClient.GetStreamingResponseAsync(question))
{
    Console.Write(update.Text);
}

Console.WriteLine();
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine("✅ Done. Try switching providers by editing Program.cs!");
Console.ResetColor();

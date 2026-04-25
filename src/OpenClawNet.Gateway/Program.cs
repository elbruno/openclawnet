using OpenClawNet.Agent;
using Microsoft.AspNetCore.DataProtection;
using OpenClawNet.Gateway.Endpoints;
using OpenClawNet.Gateway.Hubs;
using OpenClawNet.Gateway.Services;
using OpenClawNet.Gateway.Configuration;
using OpenClawNet.Mcp.Core;
using OpenClawNet.Memory;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Models.AzureOpenAI;
using OpenClawNet.Models.Foundry;
using OpenClawNet.Models.FoundryLocal;
using OpenClawNet.Models.GitHubCopilot;
using OpenClawNet.Models.Ollama;
using OpenClawNet.Skills;
using OpenClawNet.Storage;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.Browser;
using OpenClawNet.Tools.Core;
using OpenClawNet.Tools.FileSystem;
using OpenClawNet.Tools.MarkItDown;
using OpenClawNet.Tools.Calculator;
using OpenClawNet.Tools.Embeddings;
using OpenClawNet.Tools.GitHub;
using OpenClawNet.Tools.HtmlQuery;
using OpenClawNet.Tools.ImageEdit;
using OpenClawNet.Tools.Text2Image;
using OpenClawNet.Tools.TextToSpeech;
using OpenClawNet.Tools.YouTube;
using OpenClawNet.Tools.Shell;
using OpenClawNet.Tools.Web;
using OpenClawNet.Tools.Scheduler;
using ElBruno.MarkItDotNet;
using OpenClawNet.Mcp.Browser;
using OpenClawNet.Mcp.Core;
using OpenClawNet.Mcp.FileSystem;
using OpenClawNet.Mcp.Shell;
using OpenClawNet.Mcp.Web;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults
builder.AddServiceDefaults();

// OpenAPI
builder.Services.AddOpenApi();

// SignalR for real-time streaming
builder.Services.AddSignalR(o =>
{
    if (builder.Environment.IsDevelopment())
        o.EnableDetailedErrors = true;
});

// CORS for Blazor Web UI
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// OpenClawNet options (StorageDir, StorageRetention, etc.)
builder.Services.Configure<OpenClawNet.Gateway.Configuration.OpenClawNetOptions>(
    builder.Configuration.GetSection("OpenClawNet"));

// Storage (SQLite via Aspire integration)
builder.AddSqliteConnection("openclawnet-db");
builder.Services.AddOpenClawStorage();

// Storage directory provider — manages agent output paths and directory creation
// Registered with a factory to ensure it's instantiated after all infrastructure is ready.
builder.Services.AddSingleton<IStorageDirectoryProvider>(sp =>
    ActivatorUtilities.CreateInstance<StorageDirectoryProvider>(sp)
);

// Tools-facing adapter so tools (which can't reference Gateway types) can persist
// artifacts under the same storage root the rest of the app uses.
builder.Services.AddSingleton<OpenClawNet.Tools.Abstractions.IToolStorageProvider, ToolStorageProviderAdapter>();

// DataProtection — needed to encrypt the Secrets table at rest. Persist keys
// to the storage root so they survive container/host restarts; without this,
// every restart rotates the key and existing ciphertexts become unreadable.
builder.Services
    .AddDataProtection()
    .SetApplicationName("OpenClawNet")
    .PersistKeysToFileSystem(new DirectoryInfo(
        Path.Combine(
            builder.Configuration.GetValue<string>("Storage:RootPath")
                ?? new OpenClawNet.Storage.StorageOptions().RootPath,
            "dataprotection-keys")));

// Model provider — runtime-switchable via the Settings UI (no restart needed).
// RuntimeModelSettings loads the initial provider from IConfiguration / user secrets,
// then persists any UI-driven changes to model-settings.json.
// RuntimeModelClient delegates each call to the appropriate sub-client based on current settings.
builder.Services.AddHttpClient(); // required by RuntimeModelClient for Ollama sub-client
builder.Services.AddSingleton<RuntimeModelSettings>();
builder.Services.AddScoped<ProviderResolver>();
builder.Services.AddSingleton<IModelClient, RuntimeModelClient>();
builder.Services.Configure<ModelOptions>(builder.Configuration.GetSection("Model"));

// Bind provider-specific options from IConfiguration so IAgentProvider
// implementations receive endpoint + secrets from user secrets / env vars.
builder.Services.Configure<AzureOpenAIOptions>(o =>
{
    var modelSection = builder.Configuration.GetSection("Model");
    o.Endpoint = modelSection["Endpoint"] ?? string.Empty;
    o.ApiKey = modelSection["ApiKey"];
    o.DeploymentName = modelSection["DeploymentName"] ?? "gpt-5-mini";
    o.AuthMode = modelSection["AuthMode"] ?? "api-key";
});

builder.Services.Configure<OllamaOptions>(o =>
{
    var modelSection = builder.Configuration.GetSection("Model");
    o.Endpoint = modelSection["Endpoint"] ?? "http://localhost:11434";
    o.Model = modelSection["Model"] ?? "gemma4:e2b";
});

builder.Services.Configure<OpenClawNet.Models.GitHubCopilot.GitHubCopilotOptions>(o =>
{
    var copilotSection = builder.Configuration.GetSection("GitHubCopilot");
    o.GitHubToken = copilotSection["GitHubToken"]
                    ?? builder.Configuration["COPILOT_GITHUB_TOKEN"];
    o.Model = copilotSection["Model"] ?? "gpt-5-mini";
    o.CliPath = copilotSection["CliPath"];
});

// MAF IAgentProvider registrations (Phase 1 — alongside existing IModelClient)
builder.Services.AddSingleton<OllamaAgentProvider>();
builder.Services.AddSingleton<IAgentProvider>(sp => sp.GetRequiredService<OllamaAgentProvider>());
builder.Services.AddSingleton<AzureOpenAIAgentProvider>();
builder.Services.AddSingleton<IAgentProvider>(sp => sp.GetRequiredService<AzureOpenAIAgentProvider>());
builder.Services.AddSingleton<FoundryAgentProvider>();
builder.Services.AddSingleton<IAgentProvider>(sp => sp.GetRequiredService<FoundryAgentProvider>());
builder.Services.AddSingleton<FoundryLocalAgentProvider>();
builder.Services.AddSingleton<IAgentProvider>(sp => sp.GetRequiredService<FoundryLocalAgentProvider>());
builder.Services.AddSingleton<GitHubCopilotAgentProvider>();
builder.Services.AddSingleton<IAgentProvider>(sp => sp.GetRequiredService<GitHubCopilotAgentProvider>());

// RuntimeAgentProvider routes to the active provider based on settings
builder.Services.AddSingleton<RuntimeAgentProvider>();
builder.Services.AddSingleton<IAgentProvider>(sp => sp.GetRequiredService<RuntimeAgentProvider>());

// Skills
builder.Services.AddSkills();

// Memory (with embedded defaults for now)
builder.Services.AddMemory(builder.Configuration);

// Tool framework + tools
builder.Services.AddToolFramework();
// PR-B: also register the concrete tool types so the bundled MCP wrappers can
// inject the existing ITool implementations through DI without duplicating logic.
builder.Services.AddSingleton<FileSystemTool>();
builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<FileSystemTool>());
builder.Services.AddSingleton<ShellTool>();
builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<ShellTool>());
builder.Services.AddHttpClient<WebTool>();
builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<WebTool>());
builder.Services.Configure<WebToolOptions>(builder.Configuration.GetSection("Tools:Web"));
builder.Services.AddTool<SchedulerTool>();
builder.Services.AddSingleton<BrowserTool>();
builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<BrowserTool>());

// Markdown converter — uses ElBruno.MarkItDotNet to fetch a URL and return clean Markdown.
builder.Services.AddMarkItDotNet();
builder.Services.AddHttpClient(nameof(MarkItDownTool));
builder.Services.AddSingleton<MarkItDownTool>();
builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<MarkItDownTool>());

// Calculator — NCalc-powered safe arithmetic / boolean expression evaluator.
builder.Services.AddSingleton<CalculatorTool>();
builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<CalculatorTool>());

// YouTube transcript — YoutubeExplode-based metadata + closed-caption fetcher.
builder.Services.AddSingleton<YouTubeTranscriptTool>();
builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<YouTubeTranscriptTool>());

// Text-to-image — local Stable Diffusion 1.5 via ElBruno.Text2Image.Cpu.
// First call downloads the ~4 GB ONNX model to the user's HuggingFace cache.
builder.Services.AddSingleton<Text2ImageTool>();
builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<Text2ImageTool>());

// Embeddings — local ONNX text embeddings via ElBruno.LocalEmbeddings.
builder.Services.AddSingleton<EmbeddingsTool>();
builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<EmbeddingsTool>());

// Text-to-speech — local Qwen3-TTS 0.6B WAV synthesis via ElBruno.QwenTTS.
builder.Services.AddSingleton<TextToSpeechTool>();
builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<TextToSpeechTool>());

// GitHub — read-only repo browsing via Octokit. Optional GITHUB_TOKEN secret enables higher rate limits.
builder.Services.AddSingleton<GitHubTool>();
builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<GitHubTool>());

// Image editing — resize/convert/crop local images via SixLabors.ImageSharp.
builder.Services.AddSingleton<ImageEditTool>();
builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<ImageEditTool>());

// HTML query — fetch a URL, parse with AngleSharp, run a CSS selector.
builder.Services.AddSingleton<HtmlQueryTool>();
builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<HtmlQueryTool>());

// Named HTTP clients for external tool services (Aspire service discovery resolves the URLs)
builder.Services.AddHttpClient("shell-service", c => c.BaseAddress = new Uri("https+http://shell-service"));
builder.Services.AddHttpClient("browser-service", c => c.BaseAddress = new Uri("https+http://browser-service"));
builder.Services.AddHttpClient("memory-service", c => c.BaseAddress = new Uri("https+http://memory-service"));

// Channel registry— all IChannel implementations are discoverable via IChannelRegistry
builder.Services.AddSingleton<IChannelRegistry, ChannelRegistry>();

// Smart schedule parser (uses IModelClient to parse natural-language schedules)
builder.Services.AddSingleton<SmartScheduleParser>();

// Job executor service
builder.Services.AddScoped<JobExecutor>();
builder.Services.AddSingleton<OpenClawNet.Gateway.Services.JobTemplates.JobTemplatesProvider>();

// Artifact storage and retention
builder.Services.AddSingleton<ArtifactStorageService>();
builder.Services.Configure<ArtifactRetentionOptions>(builder.Configuration.GetSection("Channels:Retention"));

// Agent runtime
builder.Services.AddAgentRuntime();

// MCP foundation (PR-A): secret store, in-process + stdio hosts, tool provider,
// and the lifecycle hosted service that starts every enabled server on app boot.
// IMcpServerCatalog is registered by AddOpenClawStorage above.
builder.Services.AddOpenClawMcp();

// PR-B: register the bundled in-process MCP server wrappers around the existing
// Web/Shell/Browser/FileSystem ITool implementations. AddBundledMcpServers() decorates
// IMcpServerCatalog so these defs surface to McpToolProvider, and adds a hosted service
// that registers their tools + starts each server on boot. Not persisted to the DB
// (PR-E owns that seed). Scheduler intentionally stays legacy — see plan PR-B.
builder.Services.AddSingleton<IBundledMcpServerRegistration, WebBundledMcp>();
builder.Services.AddSingleton<IBundledMcpServerRegistration, ShellBundledMcp>();
builder.Services.AddSingleton<IBundledMcpServerRegistration, BrowserBundledMcp>();
builder.Services.AddSingleton<IBundledMcpServerRegistration, FileSystemBundledMcp>();
builder.Services.AddBundledMcpServers();

// PR-C: settings UI services — catalog (CRUD + merged view), curated suggestions
// loader, and the typed HttpClient over the official MCP registry.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<OpenClawNet.Gateway.Services.Mcp.McpServerCatalogService>();
builder.Services.AddSingleton<OpenClawNet.Gateway.Services.Mcp.McpSuggestionsProvider>();
builder.Services.AddHttpClient(OpenClawNet.Gateway.Services.Mcp.McpRegistryClient.HttpClientName, c =>
{
    c.BaseAddress = new Uri("https://registry.modelcontextprotocol.io/");
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("OpenClawNet/0.1 (+https://github.com/elbruno/openclawnet-plan)");
});
builder.Services.AddSingleton<OpenClawNet.Gateway.Services.Mcp.IMcpRegistryClient,
                              OpenClawNet.Gateway.Services.Mcp.McpRegistryClient>();

// Warmup: pre-load the Ollama model so the first user request isn't slow.
// Only registered when the initial provider is Ollama (RuntimeModelSettings reads from config).
var initialProvider = builder.Configuration.GetValue<string>("Model:Provider") ?? "ollama";
if (initialProvider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddHostedService<OllamaWarmupService>();

var app = builder.Build();

// Aspire default endpoints
app.MapDefaultEndpoints();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapDevEndpoints();
}

app.UseCors();

// Ensure database is created and schema is up to date
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<OpenClawNet.Storage.OpenClawDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();
    await OpenClawNet.Storage.SchemaMigrator.MigrateAsync(db);

    // Seed default model provider definitions
    var providerStore = scope.ServiceProvider.GetRequiredService<IModelProviderDefinitionStore>();
    await providerStore.SeedDefaultsAsync();

    // Seed default agent profile (ensures at least one agent always exists)
    var profileStore = scope.ServiceProvider.GetRequiredService<IAgentProfileStore>();
    await profileStore.GetDefaultAsync();
}

// Register tools with the registry
using (var scope = app.Services.CreateScope())
{
    var registry = scope.ServiceProvider.GetRequiredService<IToolRegistry>();
    var tools = scope.ServiceProvider.GetServices<ITool>();
    foreach (var tool in tools)
    {
        registry.Register(tool);
    }
}

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// API version
app.MapGet("/api/version", () => Results.Ok(new { version = "0.1.0", name = "OpenClawNet" }));

// Map API endpoints
app.MapChatEndpoints();
app.MapChatStreamEndpoints();
app.MapSessionEndpoints();
app.MapToolEndpoints();
app.MapToolTestEndpoints();
app.MapSkillEndpoints();
app.MapMemoryEndpoints();
app.MapJobEndpoints();
app.MapSchedulerHelpersEndpoints();
app.MapScheduleEndpoints();
app.MapWebhookEndpoints();
app.MapSettingsEndpoints();
app.MapChatDebugEndpoints();
app.MapChannelEndpoints();
app.MapChannelsApiEndpoints();
app.MapJobChannelConfigEndpoints();
app.MapDemoEndpoints();
app.MapAgentProfileEndpoints();
app.MapModelProviderEndpoints();
app.MapToolApprovalEndpoints();
app.MapMcpServerEndpoints();
app.MapSecretsEndpoints();

// Map SignalR hub
app.MapHub<ChatHub>("/hubs/chat");

app.Run();

// Expose Program for WebApplicationFactory in integration tests
public partial class Program { }

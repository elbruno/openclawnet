var builder = DistributedApplication.CreateBuilder(args);

var dbPath = builder.Configuration["OpenClawNet:ConnectionStrings:DbPath"]
    ?? Path.Combine(builder.AppHostDirectory, ".data");

var sqlite = builder.AddSqlite("openclawnet-db", databasePath: dbPath, databaseFileName: "openclawnet.db")
    .WithSqliteWeb();

// Ollama is expected to be running locally (localhost:11434).
// The gateway falls back to local Ollama via RuntimeModelSettings.

// Default model name — overridable via OPENCLAW_OLLAMA_MODEL env var or
// the OpenClawNet:Model:Default config key (appsettings.json / user secrets).
var defaultOllamaModel =
    Environment.GetEnvironmentVariable("OPENCLAW_OLLAMA_MODEL")
    ?? builder.Configuration["OpenClawNet:Model:Default"]
    ?? "gemma4:e2b";

// External Shell service — isolated shell command execution (security boundary)
var shellService = builder.AddProject<Projects.OpenClawNet_Services_Shell>("shell-service")
    .WithHttpHealthCheck("/health");

// External Browser service — isolated Playwright execution (stability boundary)
var browserService = builder.AddProject<Projects.OpenClawNet_Services_Browser>("browser-service")
    .WithHttpHealthCheck("/health");

var gateway = builder.AddProject<Projects.OpenClawNet_Gateway>("gateway")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(sqlite)
    .WithReference(shellService)
    .WithReference(browserService)
    .WaitFor(shellService)
    .WaitFor(browserService);

gateway.WithEnvironment("Model__Model", defaultOllamaModel);
gateway.WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_GENAI_EMIT_EVENTS", "true");

var scheduler = builder.AddProject<Projects.OpenClawNet_Services_Scheduler>("scheduler")
    .WithHttpHealthCheck("/health")
    .WithReference(sqlite)
    .WithReference(gateway)
    .WaitFor(gateway);

// Self-reference so Blazor components can discover their own service via service discovery
scheduler.WithReference(scheduler);

var web = builder.AddProject<Projects.OpenClawNet_Web>("web")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(gateway)
    .WithReference(scheduler)
    .WaitFor(gateway)
    .WaitFor(scheduler);

// NEW: Channels website — job output dashboard (Blazor Server)
var channelsWebsite = builder.AddProject<Projects.OpenClawNet_Channels>("channels-website")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(gateway)
    .WaitFor(gateway);

// Pass Channels website URL to Web so it can deep-link to /channels/{jobId}
web.WithEnvironment("Services__channels-website__https__0", channelsWebsite.GetEndpoint("https"));

// External Channels service — Teams Bot Framework webhook, forwards to Gateway
builder.AddProject<Projects.OpenClawNet_Services_Channels>("channels")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(gateway)
    .WaitFor(gateway);

var memoryService = builder.AddProject<Projects.OpenClawNet_Services_Memory>("memory-service")
    .WithHttpHealthCheck("/health");

builder.Build().Run();

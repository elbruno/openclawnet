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

// W-2 (Drummond P1 #5): if OPENCLAWNET_STORAGE_ROOT is set in the AppHost
// process environment, propagate it to gateway + web (and any other child
// resource that needs storage scoping). Done at the AppHost level — NOT at
// runtime in each service — to avoid the "process env vars leak across
// Aspire siblings unpredictably" failure mode Drummond flagged on Day 1.
// If unset: do nothing. The default kicks in inside Gateway via
// OpenClawNetPaths.ResolveRoot.
var storageRootOverride = Environment.GetEnvironmentVariable("OPENCLAWNET_STORAGE_ROOT");
if (!string.IsNullOrWhiteSpace(storageRootOverride))
{
    gateway.WithEnvironment("OPENCLAWNET_STORAGE_ROOT", storageRootOverride);
    // 'web' is declared further down — defer the call until after.
}

// W-3 (Drummond AC #4): when the storage root is overridden, also project
// the per-runtime model-cache locations onto the relevant resources. We
// keep the same "AppHost is the single projection point" rule W-2 #5
// established — defaulting still happens INSIDE Storage (via
// OpenClawNetPaths.ResolveModelsRoot), so we only project when the
// operator has explicitly set a root override.
//
// OLLAMA_MODELS — Ollama runtime reads this to find / write its blob cache.
// HF_HOME       — HuggingFace tooling reads this to find / write its cache.
//
// Both children (gateway + web) need the same value so adapter code in
// either process resolves the same on-disk location.
string? modelsRootForChildren = null;
if (!string.IsNullOrWhiteSpace(storageRootOverride))
{
    var trimmed = storageRootOverride.TrimEnd('\\', '/');
    modelsRootForChildren = System.IO.Path.Combine(trimmed, "models");
    gateway.WithEnvironment("OLLAMA_MODELS", modelsRootForChildren);
    gateway.WithEnvironment("HF_HOME", modelsRootForChildren);
}

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

// W-2 (Drummond P1 #5): propagate storage root override to web as well.
// Same env var must reach both children so they agree on the resolved root.
if (!string.IsNullOrWhiteSpace(storageRootOverride))
{
    web.WithEnvironment("OPENCLAWNET_STORAGE_ROOT", storageRootOverride);
}

// W-3 (Drummond AC #4): also project OLLAMA_MODELS + HF_HOME to web.
// Same rule as gateway above — only when the operator has set the override.
if (modelsRootForChildren is not null)
{
    web.WithEnvironment("OLLAMA_MODELS", modelsRootForChildren);
    web.WithEnvironment("HF_HOME", modelsRootForChildren);
}

// NEW: Channels website — job output dashboard (Blazor Server)
var channelsWebsite = builder.AddProject<Projects.OpenClawNet_Channels>("channels-website")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(gateway)
    .WaitFor(gateway);

// Pass Channels website URL to Web AND Scheduler so they can deep-link to /channels/{jobId}.
// Use explicit Channels__BaseUrl (not Services__*) — Service discovery is for HttpClient only.
// Browser deep-links need the actual external endpoint.
web.WithEnvironment("Channels__BaseUrl", channelsWebsite.GetEndpoint("https"));
scheduler.WithEnvironment("Channels__BaseUrl", channelsWebsite.GetEndpoint("https"));

// External Channels service — Teams Bot Framework webhook, forwards to Gateway
builder.AddProject<Projects.OpenClawNet_Services_Channels>("channels")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(gateway)
    .WaitFor(gateway);

var memoryService = builder.AddProject<Projects.OpenClawNet_Services_Memory>("memory-service")
    .WithHttpHealthCheck("/health");

builder.Build().Run();

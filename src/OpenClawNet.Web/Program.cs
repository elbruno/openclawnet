using MudBlazor.Services;
using OpenClawNet.Web.Components;
using OpenClawNet.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MudBlazor services (theme provider, popovers, dialogs, snackbars, JS interop).
// Bootstrap remains the layout/CSS framework; MudBlazor is scoped to data tables
// (and other components we opt-in to). See AppTheme.cs for palette mapping.
builder.Services.AddMudServices();

// Named HttpClient for Gateway API calls (used by Settings page and other pages).
// BaseAddress uses the Aspire service-discovery scheme `https+http://gateway`;
// the ResolvingHttpDelegatingHandler (registered by AddServiceDefaults ->
// AddServiceDiscovery) resolves the scheme+host to the actual endpoint at
// request time. An explicit OpenClawNet:GatewayBaseUrl override wins for
// standalone runs where service discovery is not configured.
builder.Services.AddHttpClient("gateway", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var gatewayUrl = config["OpenClawNet:GatewayBaseUrl"]
        ?? "https+http://gateway";
    client.BaseAddress = new Uri(gatewayUrl.TrimEnd('/') + "/");
});

// Named HttpClient for Scheduler service (service-discovery resolved).
builder.Services.AddHttpClient("scheduler", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var schedulerUrl = config["OpenClawNet:SchedulerBaseUrl"]
        ?? "https+http://scheduler";
    client.BaseAddress = new Uri(schedulerUrl.TrimEnd('/') + "/");
});

// Typed clients for Jobs API
builder.Services.AddScoped<JobsClient>();

// W-4: typed client for user-folder gateway endpoints. Reuses the named
// "gateway" HttpClient (Aspire service discovery) — see the named-client
// registration above. Scoped lifetime matches the per-circuit Razor pages
// that consume it.
builder.Services.AddScoped<UserFolderClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return new UserFolderClient(factory.CreateClient("gateway"));
});

// K-3: typed client for the skills gateway endpoints (snapshot / list / get /
// create / enable-for / delete / changes-since). Same Aspire-resolved
// "gateway" base address as UserFolderClient.
builder.Services.AddScoped<SkillsClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return new SkillsClient(factory.CreateClient("gateway"));
});

// Secrets Vault typed client for /api/secrets lifecycle operations.
builder.Services.AddScoped<SecretsVaultClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return new SecretsVaultClient(factory.CreateClient("gateway"));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();

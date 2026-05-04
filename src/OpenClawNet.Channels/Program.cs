using MudBlazor.Services;
using OpenClawNet.Channels.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MudBlazor services (theme provider, popovers, dialogs, snackbars, JS interop)
builder.Services.AddMudServices();

// Named HttpClient for Gateway API calls.
// BaseAddress uses the Aspire service-discovery scheme `https+http://gateway`;
// the ResolvingHttpDelegatingHandler (registered by AddServiceDefaults) resolves
// the scheme+host to the actual endpoint at request time.
builder.Services.AddHttpClient("gateway", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var gatewayUrl = config["Gateway:BaseUrl"]
        ?? "https+http://gateway";
    client.BaseAddress = new Uri(gatewayUrl.TrimEnd('/') + "/");
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

// Proxy artifact downloads from the Channels site to the Gateway. The browser
// hits this site (localhost:7030) and we forward server-side via service
// discovery — this keeps the Gateway URL hidden from clients and satisfies
// the Gateway's loopback-only access gate on /api/channels/* endpoints.
app.MapGet("/api/channels/{jobId:guid}/runs/{runId:guid}/artifacts/{artifactId:guid}/content",
    async (Guid jobId, Guid runId, Guid artifactId, IHttpClientFactory factory, CancellationToken ct) =>
    {
        var client = factory.CreateClient("gateway");
        var upstream = await client.GetAsync(
            $"api/channels/{jobId}/runs/{runId}/artifacts/{artifactId}/content",
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        if (!upstream.IsSuccessStatusCode)
            return Results.StatusCode((int)upstream.StatusCode);

        var contentType = upstream.Content.Headers.ContentType?.ToString()
            ?? "application/octet-stream";
        var fileName = upstream.Content.Headers.ContentDisposition?.FileName?.Trim('"');

        var stream = await upstream.Content.ReadAsStreamAsync(ct);
        return Results.File(stream, contentType, fileName);
    });

app.MapDefaultEndpoints();

app.Run();

using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using OpenClawNet.Services.Channels;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Bot Framework setup
builder.Services.AddSingleton<BotFrameworkAuthentication, ConfigurationBotFrameworkAuthentication>();
builder.Services.AddSingleton<IBotFrameworkHttpAdapter, ChannelAdapterErrorHandler>();
builder.Services.AddHttpClient<GatewayForwardingBot>(c => c.BaseAddress = new Uri("https+http://gateway"));
builder.Services.AddTransient<IBot>(sp => sp.GetRequiredService<GatewayForwardingBot>());

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Friendly landing page. This service is a Teams Bot Framework webhook,
// not the user-facing dashboard, so we redirect curious browsers to the
// real Channels website and explain what this endpoint does.
const string LandingHtml = """
<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8">
<title>OpenClawNet — Channels Webhook</title>
<style>
  body{font-family:system-ui,-apple-system,"Segoe UI",sans-serif;max-width:720px;margin:4rem auto;padding:0 1.5rem;color:#222;line-height:1.6}
  h1{margin:0 0 .5rem 0}
  .badge{display:inline-block;background:#0d6efd;color:#fff;padding:2px 10px;border-radius:12px;font-size:.8rem;font-weight:600}
  code{background:#f4f4f4;padding:2px 6px;border-radius:4px;font-size:.9em}
  .muted{color:#666}
</style></head><body>
<span class="badge">Webhook Service</span>
<h1>OpenClawNet — Channels</h1>
<p class="muted">This endpoint is the <strong>Teams Bot Framework webhook</strong>.
It accepts POSTs to <code>/api/messages</code> from Azure Bot Service and forwards
them to the Gateway. It has no user interface.</p>
<h3>Looking for the Job Output dashboard?</h3>
<p>The Channels <strong>website</strong> (artifact streams, Slack-style job output) is a
separate Blazor app. In a local Aspire run it typically lives at
<code>http://localhost:5030</code> (or its HTTPS endpoint). Check
<code>aspire describe</code> for the exact URL in your environment.</p>
<p><strong>Available endpoints on this service:</strong></p>
<ul>
  <li><code>GET /health</code> — health probe</li>
  <li><code>POST /api/messages</code> — Bot Framework webhook (not for browsers)</li>
</ul>
</body></html>
""";

app.MapGet("/", () => Results.Content(LandingHtml, "text/html"));

// Teams Bot Framework webhook
app.MapPost("/api/messages", async (HttpContext ctx, IBotFrameworkHttpAdapter adapter, IBot bot) =>
    await adapter.ProcessAsync(ctx.Request, ctx.Response, bot))
    .WithTags("Channels")
    .WithName("TeamsWebhook");

app.Run();
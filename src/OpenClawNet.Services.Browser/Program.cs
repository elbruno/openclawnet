using OpenClawNet.Services.Browser;
using OpenClawNet.Services.Browser.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.Configure<BrowserOptions>(builder.Configuration.GetSection("Services:Browser"));

var app = builder.Build();

// Run Playwright health check at startup to detect Windows binary blocking issues
try
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    await PlaywrightHealthCheck.CheckPlaywrightBinariesAsync(logger);
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "Failed to run Playwright health check");
}

app.MapDefaultEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));
app.MapBrowserEndpoints();
app.Run();

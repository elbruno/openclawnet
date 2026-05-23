using OpenClawNet.Services.Browser;
using OpenClawNet.Services.Browser.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.Configure<BrowserOptions>(builder.Configuration.GetSection("Services:Browser"));

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));
app.MapBrowserEndpoints();
app.Run();

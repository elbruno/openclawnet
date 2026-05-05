using OpenClawNet.Gateway.Services;

namespace OpenClawNet.Gateway.Endpoints;

public static class RuntimeSettingsEndpoints
{
    public static void MapRuntimeSettingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/runtime-settings").WithTags("Runtime Settings");

        // GET /api/runtime-settings — read-only inspection of the actual RuntimeModelSettings in effect
        group.MapGet("/", (RuntimeModelSettings settings) =>
        {
            var config = settings.Current;

            return Results.Ok(new RuntimeSettingsDto(
                config.Provider,
                config.Model,
                config.Endpoint,
                !string.IsNullOrEmpty(config.ApiKey),
                config.AuthMode,
                config.DeploymentName
            ));
        })
        .WithName("GetRuntimeSettings")
        .WithDescription("Get the currently active runtime model settings (read-only)");
    }
}

public sealed record RuntimeSettingsDto(
    string Provider,
    string? Model,
    string? Endpoint,
    bool HasApiKey,
    string? AuthMode,
    string? DeploymentName
);

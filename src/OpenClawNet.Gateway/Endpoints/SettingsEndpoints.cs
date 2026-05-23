using System.Text.Json;
using OpenClawNet.Gateway.Services;
using OpenClawNet.Tools.Core;

namespace OpenClawNet.Gateway.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/settings").WithTags("Settings");

        // GET /api/settings — returns the current model provider configuration.
        // The ApiKey is masked (never returned to the UI for security).
        group.MapGet("/", (RuntimeModelSettings settings) =>
        {
            var cfg = settings.Current;
            // For Azure OpenAI, the "model" shown to users is DeploymentName, not Model.
            // Model is Ollama-specific; DeploymentName is the Azure concept.
            var displayModel = cfg.Provider.Equals("azure-openai", StringComparison.OrdinalIgnoreCase)
                ? cfg.DeploymentName ?? cfg.Model
                : cfg.Model;
            return Results.Ok(new SettingsResponse(
                Provider:       cfg.Provider,
                Model:          displayModel,
                Endpoint:       cfg.Endpoint,
                DeploymentName: cfg.DeploymentName,
                AuthMode:       cfg.AuthMode ?? "api-key",
                HasApiKey:      !string.IsNullOrEmpty(cfg.ApiKey),
                FoundryProjectEndpoint: cfg.FoundryProjectEndpoint,
                FoundryAuthMode:        cfg.FoundryAuthMode,
                CopilotEnabled:         cfg.CopilotEnabled,
                CopilotModel:           cfg.CopilotModel
            ));
        })
        .WithName("GetSettings")
        .WithDescription("Returns the current model provider configuration");

        // PUT /api/settings — updates the active model provider at runtime.
        // If the caller does not include an ApiKey and HasApiKey was true before, the old key is preserved.
        group.MapPut("/", (SettingsRequest request, RuntimeModelSettings settings) =>
        {
            var previous = settings.Current;

            var updated = new ModelProviderConfig
            {
                Provider       = request.Provider,
                Model          = NullIfEmpty(request.Model),
                Endpoint       = NullIfEmpty(request.Endpoint),
                DeploymentName = NullIfEmpty(request.DeploymentName),
                AuthMode       = NullIfEmpty(request.AuthMode) ?? "api-key",
                // Preserve the existing API key when the caller sends an empty/null value
                // (the UI masks it and the user may not re-enter it on every save)
                ApiKey         = string.IsNullOrEmpty(request.ApiKey)
                                     ? previous.ApiKey
                                     : request.ApiKey,
                FoundryProjectEndpoint = NullIfEmpty(request.FoundryProjectEndpoint),
                FoundryAuthMode        = NullIfEmpty(request.FoundryAuthMode),
                CopilotEnabled         = request.CopilotEnabled ?? previous.CopilotEnabled,
                CopilotModel           = NullIfEmpty(request.CopilotModel),
            };

            settings.Update(updated);

            return Results.Ok(new SettingsResponse(
                Provider:       updated.Provider,
                Model:          updated.Model,
                Endpoint:       updated.Endpoint,
                DeploymentName: updated.DeploymentName,
                AuthMode:       updated.AuthMode ?? "api-key",
                HasApiKey:      !string.IsNullOrEmpty(updated.ApiKey),
                FoundryProjectEndpoint: updated.FoundryProjectEndpoint,
                FoundryAuthMode:        updated.FoundryAuthMode,
                CopilotEnabled:         updated.CopilotEnabled,
                CopilotModel:           updated.CopilotModel
            ));
        })
        .WithName("UpdateSettings")
        .WithDescription("Updates the active model provider configuration without requiring a restart");

        group.MapGet("/tool-logging", (IToolExecutionLoggingState state) =>
        {
            var current = state.Current;
            return Results.Ok(new ToolLoggingSettingsResponse(
                Enabled: current.Enabled,
                ArgumentPreviewLength: current.ArgumentPreviewLength,
                OutputPreviewLength: current.OutputPreviewLength));
        })
        .WithName("GetToolLoggingSettings")
        .WithDescription("Returns current tool execution logging settings");

        group.MapPut("/tool-logging", async (ToolLoggingSettingsRequest request, IToolExecutionLoggingState state, IHostEnvironment hostEnvironment, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("OpenClawNet.Gateway.SettingsEndpoints");
            try
            {
                var current = state.Current;
                var updated = new ToolExecutionLoggingOptions
                {
                    Enabled = request.Enabled,
                    ArgumentPreviewLength = current.ArgumentPreviewLength,
                    OutputPreviewLength = current.OutputPreviewLength
                };

                var settingsPath = Path.Combine(hostEnvironment.ContentRootPath, "appsettings.json");
                await UpdateToolLoggingConfigurationFile(settingsPath, updated);
                state.Update(updated);

                return Results.Ok(new ToolLoggingSettingsResponse(
                    Enabled: updated.Enabled,
                    ArgumentPreviewLength: updated.ArgumentPreviewLength,
                    OutputPreviewLength: updated.OutputPreviewLength));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update tool logging settings");
                return Results.Problem(
                    title: "Failed to update tool logging settings",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("UpdateToolLoggingSettings")
        .WithDescription("Enables or disables extensive tool execution logging globally");
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task UpdateToolLoggingConfigurationFile(string settingsPath, ToolExecutionLoggingOptions options)
    {
        if (!File.Exists(settingsPath))
            throw new FileNotFoundException("appsettings.json not found", settingsPath);

        var json = await File.ReadAllTextAsync(settingsPath);
        using var doc = JsonDocument.Parse(json);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.NameEquals("OpenClawNet"))
                {
                    WriteOpenClawNetWithToolLogging(writer, property.Value, options);
                }
                else
                {
                    property.WriteTo(writer);
                }
            }

            if (!doc.RootElement.TryGetProperty("OpenClawNet", out _))
            {
                writer.WriteStartObject("OpenClawNet");
                WriteToolExecutionLogging(writer, options);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        await File.WriteAllTextAsync(settingsPath, System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static void WriteOpenClawNetWithToolLogging(
        Utf8JsonWriter writer,
        JsonElement openClawNetElement,
        ToolExecutionLoggingOptions options)
    {
        writer.WriteStartObject("OpenClawNet");

        foreach (var child in openClawNetElement.EnumerateObject())
        {
            if (!child.NameEquals("ToolExecutionLogging"))
            {
                child.WriteTo(writer);
            }
        }

        WriteToolExecutionLogging(writer, options);
        writer.WriteEndObject();
    }

    private static void WriteToolExecutionLogging(Utf8JsonWriter writer, ToolExecutionLoggingOptions options)
    {
        writer.WriteStartObject("ToolExecutionLogging");
        writer.WriteBoolean("Enabled", options.Enabled);
        writer.WriteNumber("ArgumentPreviewLength", options.ArgumentPreviewLength);
        writer.WriteNumber("OutputPreviewLength", options.OutputPreviewLength);
        writer.WriteEndObject();
    }
}

public sealed record SettingsResponse(
    string Provider,
    string? Model,
    string? Endpoint,
    string? DeploymentName,
    string AuthMode,
    bool HasApiKey,
    string? FoundryProjectEndpoint,
    string? FoundryAuthMode,
    bool CopilotEnabled,
    string? CopilotModel);

public sealed record SettingsRequest(
    string Provider,
    string? Model,
    string? Endpoint,
    string? ApiKey,
    string? DeploymentName,
    string? AuthMode,
    string? FoundryProjectEndpoint,
    string? FoundryAuthMode,
    bool? CopilotEnabled,
    string? CopilotModel);

public sealed record ToolLoggingSettingsResponse(
    bool Enabled,
    int ArgumentPreviewLength,
    int OutputPreviewLength);

public sealed record ToolLoggingSettingsRequest(
    bool Enabled);

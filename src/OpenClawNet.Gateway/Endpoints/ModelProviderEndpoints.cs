using Microsoft.AspNetCore.Mvc;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Gateway.Endpoints;

public static class ModelProviderEndpoints
{
    public static void MapModelProviderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/model-providers").WithTags("Model Providers");

        group.MapGet("/", async (IModelProviderDefinitionStore store, CancellationToken ct) =>
        {
            var definitions = await store.ListAsync(ct);
            return Results.Ok(definitions.Select(d => ToResponse(d)));
        });

        group.MapGet("/{name}", async (string name, IModelProviderDefinitionStore store, CancellationToken ct) =>
        {
            var def = await store.GetAsync(name, ct);
            return def is null ? Results.NotFound() : Results.Ok(ToResponse(def));
        });

        group.MapPost("/", async (ModelProviderRequest request, IModelProviderDefinitionStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "Provider name is required." });

            var existing = await store.GetAsync(request.Name, ct);
            var def = BuildDefinition(request.Name, request, existing);

            await store.SaveAsync(def, ct);
            return existing is null
                ? Results.Created($"/api/model-providers/{def.Name}", ToResponse(def))
                : Results.Ok(ToResponse(def));
        });

        group.MapPut("/{name}", async (string name, ModelProviderRequest request, IModelProviderDefinitionStore store, CancellationToken ct) =>
        {
            var existing = await store.GetAsync(name, ct);
            var def = BuildDefinition(name, request, existing);

            await store.SaveAsync(def, ct);
            return Results.Ok(ToResponse(def));
        });

        group.MapDelete("/{name}", async (string name, IModelProviderDefinitionStore store, CancellationToken ct) =>
        {
            await store.DeleteAsync(name, ct);
            return Results.NoContent();
        });

        group.MapDelete("/", async ([FromBody] BulkDeleteModelProvidersRequest request, IModelProviderDefinitionStore store, CancellationToken ct) =>
        {
            if (request.Names is not { Count: > 0 })
                return Results.BadRequest("No provider names provided.");

            var deleted = new List<string>();
            var skipped = new List<SkippedProvider>();

            foreach (var name in request.Names.Distinct(StringComparer.Ordinal))
            {
                var provider = await store.GetAsync(name, ct);
                if (provider is null)
                {
                    skipped.Add(new SkippedProvider(name, "not-found"));
                    continue;
                }
                await store.DeleteAsync(name, ct);
                deleted.Add(name);
            }

            return Results.Ok(new BulkDeleteModelProvidersResponse(deleted, skipped));
        })
        .WithName("DeleteModelProvidersBulk")
        .WithDescription("Bulk-deletes model providers")
        .Accepts<BulkDeleteModelProvidersRequest>("application/json");

        // POST /api/model-providers/{name}/test — sends a real chat completion
        group.MapPost("/{name}/test", async (
            string name,
            IModelProviderDefinitionStore store,
            IEnumerable<IAgentProvider> providers,
            ILogger<GatewayProgramMarker> logger,
            CancellationToken ct) =>
        {
            var def = await store.GetAsync(name, ct);
            if (def is null) return Results.NotFound();

            logger.LogInformation("Testing model provider '{Name}' (type={Type}, endpoint={Endpoint}, model={Model})",
                name, def.ProviderType, def.Endpoint, def.Model);

            def.LastTestedAt = DateTime.UtcNow;

            try
            {
                var provider = providers
                    .Where(p => p.GetType().Name != "RuntimeAgentProvider")
                    .FirstOrDefault(p => p.ProviderName.Equals(def.ProviderType, StringComparison.OrdinalIgnoreCase));

                if (provider is null)
                {
                    def.LastTestSucceeded = false;
                    def.LastTestError = $"No provider registered for type '{def.ProviderType}'";
                    def.IsSupported = false;
                    def.UpdatedAt = DateTime.UtcNow;
                    await store.SaveAsync(def, ct);
                    return Results.Ok(new { 
                        success = false, 
                        message = def.LastTestError,
                        lastTestedAt = def.LastTestedAt,
                        lastTestSucceeded = def.LastTestSucceeded,
                        lastTestError = def.LastTestError
                    });
                }

                var testProfile = new AgentProfile
                {
                    Name = $"test-{name}",
                    Provider = def.ProviderType,
                    Endpoint = def.Endpoint,
                    ApiKey = def.ApiKey,
                    DeploymentName = def.DeploymentName,
                    AuthMode = def.AuthMode,
                };

                logger.LogInformation("Creating chat client for test: provider={Provider}, model={Model}",
                    def.ProviderType, def.Model ?? "(provider default)");

                var chatClient = provider.CreateChatClient(testProfile);

                var messages = new List<Microsoft.Extensions.AI.ChatMessage>
                {
                    new(Microsoft.Extensions.AI.ChatRole.User, "Hi, respond with just one word.")
                };

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

                logger.LogInformation("Sending test chat completion to '{Name}'...", name);
                var response = await chatClient.GetResponseAsync(messages, cancellationToken: timeoutCts.Token);

                var responseText = response.Text ?? "(empty)";
                var truncated = responseText.Length > 100 ? responseText[..100] + "..." : responseText;

                logger.LogInformation("Provider '{Name}' responded: {Response}", name, truncated);

                def.IsSupported = true;
                def.LastTestSucceeded = true;
                def.LastTestError = null;
                def.UpdatedAt = DateTime.UtcNow;
                await store.SaveAsync(def, ct);

                return Results.Ok(new { 
                    success = true, 
                    message = $"Model responded: \"{truncated}\"",
                    lastTestedAt = def.LastTestedAt,
                    lastTestSucceeded = def.LastTestSucceeded,
                    lastTestError = def.LastTestError
                });
            }
            catch (TaskCanceledException)
            {
                var errorMsg = "Test timed out (30s). The model may need to be downloaded first.";
                def.IsSupported = false;
                def.LastTestSucceeded = false;
                def.LastTestError = errorMsg.Length > 1000 ? errorMsg[..1000] : errorMsg;
                def.UpdatedAt = DateTime.UtcNow;
                await store.SaveAsync(def, ct);
                return Results.Ok(new { 
                    success = false, 
                    message = errorMsg,
                    lastTestedAt = def.LastTestedAt,
                    lastTestSucceeded = def.LastTestSucceeded,
                    lastTestError = def.LastTestError
                });
            }
            catch (Exception ex)
            {
                var errorMsg = $"Test failed: {ex.Message}";
                def.IsSupported = false;
                def.LastTestSucceeded = false;
                def.LastTestError = errorMsg.Length > 1000 ? errorMsg[..1000] : errorMsg;
                def.UpdatedAt = DateTime.UtcNow;
                await store.SaveAsync(def, ct);
                return Results.Ok(new { 
                    success = false, 
                    message = errorMsg,
                    lastTestedAt = def.LastTestedAt,
                    lastTestSucceeded = def.LastTestSucceeded,
                    lastTestError = def.LastTestError
                });
            }
        });
    }

    private static ModelProviderResponse ToResponse(ModelProviderDefinition d) => new(
        d.Name, d.ProviderType, d.DisplayName, d.Endpoint, d.Model,
        HasApiKey: !string.IsNullOrEmpty(d.ApiKey),
        d.DeploymentName, d.AuthMode, d.IsSupported, d.CreatedAt, d.UpdatedAt,
        d.LastTestedAt, d.LastTestSucceeded, d.LastTestError,
        ApiKey: GetVaultReferenceOrNull(d.ApiKey)
    );

    private static ModelProviderDefinition BuildDefinition(string name, ModelProviderRequest request, ModelProviderDefinition? existing) => new()
    {
        Name = name,
        ProviderType = request.ProviderType,
        DisplayName = request.DisplayName,
        Endpoint = request.Endpoint,
        Model = request.Model,
        ApiKey = string.IsNullOrEmpty(request.ApiKey) ? existing?.ApiKey : request.ApiKey,
        DeploymentName = request.DeploymentName,
        AuthMode = request.AuthMode,
        IsSupported = request.IsSupported ?? existing?.IsSupported ?? false,
        CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static string? GetVaultReferenceOrNull(string? value) =>
        VaultConfigurationResolver.TryParseVaultReference(value, out _)
            ? value
            : null;
}

public sealed record ModelProviderRequest(
    string ProviderType,
    string? DisplayName,
    string? Endpoint,
    string? Model,
    string? ApiKey,
    string? DeploymentName,
    string? AuthMode,
    bool? IsSupported,
    string? Name = null);

public sealed record ModelProviderResponse(
    string Name,
    string ProviderType,
    string? DisplayName,
    string? Endpoint,
    string? Model,
    bool HasApiKey,
    string? DeploymentName,
    string? AuthMode,
    bool IsSupported,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? LastTestedAt,
    bool? LastTestSucceeded,
    string? LastTestError,
    string? ApiKey = null);

public sealed record BulkDeleteModelProvidersRequest
{
    public List<string> Names { get; init; } = [];
}

public sealed record SkippedProvider(string Name, string Reason);
public sealed record BulkDeleteModelProvidersResponse(List<string> Deleted, List<SkippedProvider> Skipped);

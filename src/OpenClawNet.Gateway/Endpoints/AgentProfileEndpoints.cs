using Microsoft.AspNetCore.Mvc;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Gateway.Endpoints;

public static class AgentProfileEndpoints
{
    public static void MapAgentProfileEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/agent-profiles").WithTags("Agent Profiles");

        group.MapGet("/", async (string? kind, IAgentProfileStore store, CancellationToken ct) =>
        {
            var profiles = await store.ListAsync(ct);
            if (!string.IsNullOrWhiteSpace(kind))
            {
                if (!Enum.TryParse<ProfileKind>(kind, ignoreCase: true, out var filterKind))
                {
                    return Results.BadRequest(new { error = $"Unknown kind '{kind}'. Use Standard, System, or ToolTester." });
                }
                profiles = profiles.Where(p => p.Kind == filterKind).ToList();
            }
            return Results.Ok(profiles.Select(ToResponse));
        })
        .WithName("ListAgentProfiles")
        .WithDescription("Returns configured agent profiles. Optional ?kind= filter: Standard|System|ToolTester.");

        group.MapGet("/{name}", async (string name, IAgentProfileStore store, CancellationToken ct) =>
        {
            var profile = await store.GetAsync(name, ct);
            return profile is null ? Results.NotFound() : Results.Ok(ToResponse(profile));
        })
        .WithName("GetAgentProfile")
        .WithDescription("Returns a specific agent profile by name");

        group.MapPut("/{name}", async (string name, AgentProfileRequest request, IAgentProfileStore store, CancellationToken ct) =>
        {
            var kind = ProfileKind.Standard;
            if (!string.IsNullOrWhiteSpace(request.Kind) &&
                !Enum.TryParse(request.Kind, ignoreCase: true, out kind))
            {
                return Results.BadRequest(new { error = $"Unknown kind '{request.Kind}'. Use Standard, System, or ToolTester." });
            }

            var profile = new AgentProfile
            {
                Name = name,
                DisplayName = request.DisplayName,
                Provider = request.Provider,
                Instructions = request.Instructions,
                EnabledTools = request.EnabledTools is { Length: > 0 }
                    ? string.Join(", ", request.EnabledTools)
                    : null,
                Temperature = request.Temperature,
                MaxTokens = request.MaxTokens,
                IsDefault = request.IsDefault,
                Kind = kind,
                RequireToolApproval = request.RequireToolApproval,
                IsEnabled = request.IsEnabled ?? true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            // Only Standard profiles may be marked as default. Defensively coerce so
            // a malformed client request doesn't promote a System/ToolTester to default.
            if (profile.Kind != ProfileKind.Standard)
            {
                profile.IsDefault = false;
            }

            await store.SaveAsync(profile, ct);
            return Results.Ok(ToResponse(profile));
        })
        .WithName("UpsertAgentProfile")
        .WithDescription("Creates or updates an agent profile");

        group.MapPatch("/{name}/enabled", async (string name, [FromBody] SetEnabledRequest request, IAgentProfileStore store, CancellationToken ct) =>
        {
            var profile = await store.GetAsync(name, ct);
            if (profile is null) return Results.NotFound();

            // Prevent disabling the default profile without warning
            if (profile.IsDefault && !request.IsEnabled)
            {
                return Results.BadRequest(new { 
                    error = "Cannot disable the default profile. Please set another profile as default first." 
                });
            }

            profile.IsEnabled = request.IsEnabled;
            profile.UpdatedAt = DateTime.UtcNow;
            await store.SaveAsync(profile, ct);
            return Results.Ok(ToResponse(profile));
        })
        .WithName("SetAgentProfileEnabled")
        .WithDescription("Enable or disable an agent profile");

        group.MapPost("/{name}/set-default", async (string name, IAgentProfileStore store, CancellationToken ct) =>
        {
            var profile = await store.GetAsync(name, ct);
            if (profile is null) return Results.NotFound();

            if (!profile.IsEnabled)
            {
                return Results.BadRequest(new
                {
                    error = "Cannot set a disabled profile as default. Enable it first."
                });
            }

            if (profile.IsDefault)
            {
                // No-op — already default. Return current state.
                return Results.Ok(ToResponse(profile));
            }

            profile.IsDefault = true;
            profile.UpdatedAt = DateTime.UtcNow;
            // SaveAsync clears IsDefault on every other profile in the same transaction.
            await store.SaveAsync(profile, ct);
            return Results.Ok(ToResponse(profile));
        })
        .WithName("SetAgentProfileDefault")
        .WithDescription("Marks the named profile as the default. Clears IsDefault on all other profiles.");

        group.MapGet("/default", async (IAgentProfileStore store, CancellationToken ct) =>
        {
            var defaultProfile = await store.GetDefaultAsync(ct);
            return defaultProfile is null ? Results.NotFound() : Results.Ok(ToResponse(defaultProfile));
        })
        .WithName("GetDefaultAgentProfile")
        .WithDescription("Returns the currently configured default agent profile");

        group.MapPost("/import",async (ImportAgentProfileRequest request, IAgentProfileStore store, CancellationToken ct) =>
        {
            var profile = AgentProfileMarkdownParser.Parse(request.Markdown, request.FallbackName);
            await store.SaveAsync(profile, ct);
            return Results.Ok(ToResponse(profile));
        })
        .WithName("ImportAgentProfile")
        .WithDescription("Imports an agent profile from a Markdown definition");

        group.MapDelete("/{name}", async (string name, IAgentProfileStore store, CancellationToken ct) =>
        {
            await store.DeleteAsync(name, ct);
            return Results.NoContent();
        })
        .WithName("DeleteAgentProfile")
        .WithDescription("Deletes an agent profile");

        group.MapDelete("/", async ([FromBody] BulkDeleteAgentProfilesRequest request, IAgentProfileStore store, CancellationToken ct) =>
        {
            if (request.Names is not { Count: > 0 })
                return Results.BadRequest("No profile names provided.");

            var deleted = new List<string>();
            var skipped = new List<SkippedProfile>();

            foreach (var name in request.Names.Distinct(StringComparer.Ordinal))
            {
                var profile = await store.GetAsync(name, ct);
                if (profile is null)
                {
                    skipped.Add(new SkippedProfile(name, "not-found"));
                    continue;
                }
                if (profile.IsDefault)
                {
                    skipped.Add(new SkippedProfile(name, "default-profile"));
                    continue;
                }
                await store.DeleteAsync(name, ct);
                deleted.Add(name);
            }

            return Results.Ok(new BulkDeleteAgentProfilesResponse(deleted, skipped));
        })
        .WithName("DeleteAgentProfilesBulk")
        .WithDescription("Bulk-deletes agent profiles. The default profile is never deleted and is returned under 'skipped'.")
        .Accepts<BulkDeleteAgentProfilesRequest>("application/json");

        group.MapPost("/{name}/test", async (
            string name,
            IAgentProfileStore profileStore,
            IModelProviderDefinitionStore providerStore,
            IEnumerable<IAgentProvider> providers,
            ILogger<GatewayProgramMarker> logger,
            CancellationToken ct) =>
        {
            var profile = await profileStore.GetAsync(name, ct);
            if (profile is null) return Results.NotFound();

            logger.LogInformation("Testing agent profile '{Name}' (provider={Provider})", name, profile.Provider);

            var entity = await profileStore.GetEntityAsync(name, ct);
            if (entity is not null)
            {
                entity.LastTestedAt = DateTime.UtcNow;
            }

            // Resolve the model provider definition
            ModelProviderDefinition? definition = null;
            if (!string.IsNullOrEmpty(profile.Provider))
                definition = await providerStore.GetAsync(profile.Provider, ct);

            if (definition is null)
            {
                if (entity is not null)
                {
                    entity.LastTestSucceeded = false;
                    entity.LastTestError = $"Provider '{profile.Provider}' not found";
                    entity.UpdatedAt = DateTime.UtcNow;
                    await profileStore.SaveEntityAsync(entity, ct);
                }
                return Results.Ok(new { 
                    success = false, 
                    message = $"Provider '{profile.Provider}' not found",
                    lastTestedAt = entity?.LastTestedAt,
                    lastTestSucceeded = entity?.LastTestSucceeded,
                    lastTestError = entity?.LastTestError
                });
            }

            try
            {
                // Find the IAgentProvider for this type
                var agentProvider = providers
                    .Where(p => p.GetType().Name != "RuntimeAgentProvider")
                    .FirstOrDefault(p => p.ProviderName.Equals(definition.ProviderType, StringComparison.OrdinalIgnoreCase));

                if (agentProvider is null)
                {
                    var errorMsg = $"No provider for type '{definition.ProviderType}'";
                    if (entity is not null)
                    {
                        entity.LastTestSucceeded = false;
                        entity.LastTestError = errorMsg;
                        entity.UpdatedAt = DateTime.UtcNow;
                        await profileStore.SaveEntityAsync(entity, ct);
                    }
                    return Results.Ok(new { 
                        success = false, 
                        message = errorMsg,
                        lastTestedAt = entity?.LastTestedAt,
                        lastTestSucceeded = entity?.LastTestSucceeded,
                        lastTestError = entity?.LastTestError
                    });
                }

                // Create a profile enriched with definition's connection details
                var testProfile = new AgentProfile
                {
                    Name = $"test-{name}",
                    Provider = definition.ProviderType,
                    Endpoint = definition.Endpoint,
                    ApiKey = definition.ApiKey,
                    DeploymentName = definition.DeploymentName,
                    AuthMode = definition.AuthMode,
                    Instructions = profile.Instructions,
                };

                var chatClient = agentProvider.CreateChatClient(testProfile);

                var messages = new List<Microsoft.Extensions.AI.ChatMessage>
                {
                    new(Microsoft.Extensions.AI.ChatRole.User, "Hi, respond with one word.")
                };

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(30));

                var response = await chatClient.GetResponseAsync(messages, cancellationToken: cts.Token);
                var text = response.Text ?? "(empty)";
                var truncated = text.Length > 100 ? text[..100] + "..." : text;

                logger.LogInformation("Agent '{Name}' test response: {Response}", name, truncated);
                
                if (entity is not null)
                {
                    entity.LastTestSucceeded = true;
                    entity.LastTestError = null;
                    entity.UpdatedAt = DateTime.UtcNow;
                    await profileStore.SaveEntityAsync(entity, ct);
                }

                return Results.Ok(new { 
                    success = true, 
                    message = $"Agent responded: \"{truncated}\"",
                    lastTestedAt = entity?.LastTestedAt,
                    lastTestSucceeded = entity?.LastTestSucceeded,
                    lastTestError = entity?.LastTestError
                });
            }
            catch (TaskCanceledException)
            {
                var errorMsg = "Test timed out (30s)";
                if (entity is not null)
                {
                    entity.LastTestSucceeded = false;
                    entity.LastTestError = errorMsg;
                    entity.UpdatedAt = DateTime.UtcNow;
                    await profileStore.SaveEntityAsync(entity, ct);
                }
                return Results.Ok(new { 
                    success = false, 
                    message = errorMsg,
                    lastTestedAt = entity?.LastTestedAt,
                    lastTestSucceeded = entity?.LastTestSucceeded,
                    lastTestError = entity?.LastTestError
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Agent test '{Name}' failed", name);
                var errorMsg = $"Test failed: {ex.Message}";
                var truncatedError = errorMsg.Length > 1000 ? errorMsg[..1000] : errorMsg;
                if (entity is not null)
                {
                    entity.LastTestSucceeded = false;
                    entity.LastTestError = truncatedError;
                    entity.UpdatedAt = DateTime.UtcNow;
                    await profileStore.SaveEntityAsync(entity, ct);
                }
                return Results.Ok(new { 
                    success = false, 
                    message = errorMsg,
                    lastTestedAt = entity?.LastTestedAt,
                    lastTestSucceeded = entity?.LastTestSucceeded,
                    lastTestError = entity?.LastTestError
                });
            }
        })
        .WithName("TestAgentProfile")
        .WithDescription("Tests an agent profile by sending a chat completion through its configured provider");
    }

    private static AgentProfileResponse ToResponse(AgentProfile p) => new(
        p.Name, p.DisplayName, p.Provider, p.Instructions,
        string.IsNullOrWhiteSpace(p.EnabledTools)
            ? null
            : p.EnabledTools.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        p.Temperature, p.MaxTokens, p.IsDefault, p.RequireToolApproval, p.IsEnabled,
        p.LastTestedAt, p.LastTestSucceeded, p.LastTestError, p.Kind.ToString());
}

public sealed record AgentProfileResponse(
    string Name,
    string? DisplayName,
    string? Provider,
    string? Instructions,
    string[]? EnabledTools,
    double? Temperature,
    int? MaxTokens,
    bool IsDefault,
    bool RequireToolApproval,
    bool IsEnabled,
    DateTime? LastTestedAt,
    bool? LastTestSucceeded,
    string? LastTestError,
    string Kind);

public sealed record SetEnabledRequest(bool IsEnabled);

public sealed record ImportAgentProfileRequest(string Markdown, string? FallbackName = null);

public sealed record BulkDeleteAgentProfilesRequest
{
    public List<string> Names { get; init; } = [];
}

public sealed record SkippedProfile(string Name, string Reason);
public sealed record BulkDeleteAgentProfilesResponse(List<string> Deleted, List<SkippedProfile> Skipped);

public sealed record AgentProfileRequest(
    string? DisplayName,
    string? Provider,
    string? Instructions,
    string[]? EnabledTools,
    double? Temperature,
    int? MaxTokens,
    bool IsDefault,
    bool RequireToolApproval = true,
    bool? IsEnabled = true,
    string? Kind = "Standard");

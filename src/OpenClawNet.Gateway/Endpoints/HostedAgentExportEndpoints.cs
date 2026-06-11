using OpenClawNet.Gateway.Services;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;

namespace OpenClawNet.Gateway.Endpoints;

public static class HostedAgentExportEndpoints
{
    public static void MapHostedAgentExportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/agent-profiles").WithTags("Agent Profiles");

        group.MapPost("/export/hosted-agent", async (
            HostedAgentExportRequest request,
            IAgentProfileStore store,
            CancellationToken ct) =>
        {
            if (request.ProfileNames is not { Count: > 0 })
            {
                return Results.BadRequest(new { error = "At least one profile name is required." });
            }

            if (string.IsNullOrWhiteSpace(request.NamePrefix))
                return Results.BadRequest(new { error = "namePrefix is required." });
            if (string.IsNullOrWhiteSpace(request.Location))
                return Results.BadRequest(new { error = "location is required." });
            if (string.IsNullOrWhiteSpace(request.ContainerImage))
                return Results.BadRequest(new { error = "containerImage is required." });

            var selected = new List<AgentProfile>();
            var missing = new List<string>();

            foreach (var name in request.ProfileNames.Distinct(StringComparer.Ordinal))
            {
                var profile = await store.GetAsync(name, ct);
                if (profile is null)
                {
                    missing.Add(name);
                    continue;
                }

                selected.Add(profile);
            }

            if (missing.Count > 0)
            {
                return Results.NotFound(new
                {
                    error = "One or more selected profiles were not found.",
                    missing
                });
            }

            var bundle = HostedAgentExportBundleBuilder.Create(request, selected);
            return Results.File(bundle.Content, "application/zip", bundle.FileName);
        })
        .WithName("ExportHostedAgents")
        .WithDescription("Exports selected agent profiles as an Azure hosted-agent BICEP bundle.");
    }
}


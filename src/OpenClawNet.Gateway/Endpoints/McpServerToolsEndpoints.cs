using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using OpenClawNet.Gateway.Services.Mcp;
using OpenClawNet.Mcp.Abstractions;

namespace OpenClawNet.Gateway.Endpoints;

public static class McpServerToolsEndpoints
{
    public static void MapMcpServerToolsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/mcp-servers").WithTags("MCP Servers");

        // GET /api/mcp-servers/{id}/tools — what tools does this MCP server expose?
        group.MapGet("/{id:guid}/tools", async (
            Guid id,
            [FromServices] McpServerCatalogService catalog,
            [FromServices] IMcpToolProvider toolProvider,
            CancellationToken ct) =>
        {
            var server = await catalog.GetAsync(id, ct);
            if (server is null)
                return Results.NotFound();

            if (!server.Enabled)
            {
                return Results.Ok(new McpServerToolsDto(
                    server.Id,
                    server.Name,
                    false,
                    "Server is disabled",
                    Array.Empty<McpToolDto>()
                ));
            }

            if (!server.IsRunning)
            {
                return Results.Ok(new McpServerToolsDto(
                    server.Id,
                    server.Name,
                    false,
                    "Server is not running",
                    Array.Empty<McpToolDto>()
                ));
            }

            try
            {
                var tools = await toolProvider.GetToolsForServerAsync(server.Id.ToString(), ct);
                
                var toolDtos = tools
                    .Select(t =>
                    {
                        var func = t as AIFunction;
                        var storage = t is IMcpAITool m ? m.StorageName : $"{Slug(server.Name)}.{func?.Name ?? string.Empty}";
                        return new McpToolDto(
                            storage,
                            func?.Description ?? "(no description)"
                        );
                    })
                    .OrderBy(t => t.Name, StringComparer.Ordinal)
                    .ToList();

                return Results.Ok(new McpServerToolsDto(
                    server.Id,
                    server.Name,
                    true,
                    null,
                    toolDtos
                ));
            }
            catch (Exception ex)
            {
                return Results.Ok(new McpServerToolsDto(
                    server.Id,
                    server.Name,
                    false,
                    $"Failed to load tools: {ex.Message}",
                    Array.Empty<McpToolDto>()
                ));
            }
        })
        .WithName("GetMcpServerTools")
        .WithDescription("List all tools exposed by a specific MCP server");
    }

    private static string Slug(string name)
    {
        var buf = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            buf[i] = char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_';
        }
        return new string(buf);
    }
}

public sealed record McpServerToolsDto(
    Guid ServerId,
    string ServerName,
    bool Success,
    string? Error,
    IReadOnlyCollection<McpToolDto> Tools
);

public sealed record McpToolDto(
    string Name,
    string Description
);

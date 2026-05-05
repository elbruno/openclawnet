using System.Text.Json;
using OpenClawNet.Storage;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Gateway.Endpoints;

public static class ToolEndpoints
{
    public static void MapToolEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/tools").WithTags("Tools");
        
        group.MapGet("/", (IToolRegistry registry) =>
        {
            var tools = registry.GetToolManifest();
            return Results.Ok(tools.Select(t => new ToolDto
            {
                Name = t.Name,
                Description = t.Description,
                Category = t.Category,
                RequiresApproval = t.RequiresApproval,
                Tags = t.Tags,
                ParameterSchema = JsonSerializer.Deserialize<JsonElement>(t.ParameterSchema.RootElement.GetRawText())
            }));
        })
        .WithName("ListTools");

        group.MapGet("/{name}", (
            string name,
            IToolRegistry registry,
            IToolTestRecordStore testStore) =>
        {
            var tools = registry.GetToolManifest();
            var tool = tools.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            
            if (tool is null)
                return Results.NotFound(new { error = $"Tool '{name}' not found." });

            // Get last test result if available
            var testRecord = testStore.GetAsync(name, CancellationToken.None).GetAwaiter().GetResult();

            return Results.Ok(new ToolDetailDto
            {
                Name = tool.Name,
                Description = tool.Description,
                Category = tool.Category,
                RequiresApproval = tool.RequiresApproval,
                Tags = tool.Tags,
                ParameterSchema = JsonSerializer.Deserialize<JsonElement>(tool.ParameterSchema.RootElement.GetRawText()),
                LastTestedAt = testRecord?.LastTestedAt,
                LastTestSucceeded = testRecord?.LastTestSucceeded,
                LastTestError = testRecord?.LastTestError,
                LastTestMode = testRecord?.LastTestMode
            });
        })
        .WithName("GetTool")
        .WithDescription("Returns a single tool's details including its schema and last test result.");
    }
}

public record ToolDto
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public bool RequiresApproval { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public JsonElement ParameterSchema { get; init; }
}

public sealed record ToolDetailDto : ToolDto
{
    public DateTime? LastTestedAt { get; init; }
    public bool? LastTestSucceeded { get; init; }
    public string? LastTestError { get; init; }
    public string? LastTestMode { get; init; }
}


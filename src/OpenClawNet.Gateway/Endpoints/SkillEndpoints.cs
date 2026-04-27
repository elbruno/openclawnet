using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using OpenClawNet.Skills;

namespace OpenClawNet.Gateway.Endpoints;

public static class SkillEndpoints
{
    private const string AwesomeCopilotApiUrl = "https://api.github.com/repos/github/awesome-copilot/contents/skills";
    private const string AwesomeCopilotRawBase = "https://raw.githubusercontent.com/github/awesome-copilot/main/skills";

    public static void MapSkillEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/skills").WithTags("Skills");

        group.MapGet("/", async (ISkillLoader loader) =>
        {
            var skills = await loader.ListSkillsAsync();
            return Results.Ok(skills);
        })
        .WithName("ListSkills");

        group.MapPost("/reload", async (ISkillLoader loader) =>
        {
            await loader.ReloadAsync();
            var skills = await loader.ListSkillsAsync();
            return Results.Ok(new { reloaded = true, count = skills.Count });
        })
        .WithName("ReloadSkills");

        group.MapPost("/{name}/enable", (string name, ISkillLoader loader) =>
        {
            loader.EnableSkill(name);
            return Results.Ok(new { name, enabled = true });
        })
        .WithName("EnableSkill");

        group.MapPost("/{name}/disable", (string name, ISkillLoader loader) =>
        {
            loader.DisableSkill(name);
            return Results.Ok(new { name, enabled = false });
        })
        .WithName("DisableSkill");

        // ── Import Endpoint ──────────────────────────────────────────────────────────────

        group.MapPost("/import", ImportSkillsAsync)
            .WithName("ImportSkills")
            .DisableAntiforgery();

        // ── Marketplace ──────────────────────────────────────────────────────────────

        group.MapGet("/marketplace", async (IHttpClientFactory httpClientFactory) =>
        {
            try
            {
                var client = httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("User-Agent", "OpenClawNet/1.0");

                var response = await client.GetAsync(AwesomeCopilotApiUrl);
                if (!response.IsSuccessStatusCode)
                    return Results.Problem($"GitHub API returned {response.StatusCode}", statusCode: (int)response.StatusCode);

                var json = await response.Content.ReadAsStringAsync();
                var entries = JsonSerializer.Deserialize<GitHubContentEntry[]>(json, JsonOptions);
                if (entries is null)
                    return Results.Ok(Array.Empty<MarketplaceSkill>());

                var skillDirs = entries.Where(e => e.Type == "dir").ToArray();
                return Results.Ok(skillDirs.Select(d => new MarketplaceSkill(
                    d.Name,
                    $"{AwesomeCopilotRawBase}/{d.Name}/SKILL.md",
                    $"https://github.com/github/awesome-copilot/tree/main/skills/{d.Name}"
                )));
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to fetch marketplace: {ex.Message}", statusCode: 500);
            }
        })
        .WithName("GetMarketplaceSkills");

        group.MapPost("/install", async (InstallSkillRequest request, ISkillLoader loader, IHttpClientFactory httpClientFactory) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.ContentUrl))
                return Results.BadRequest("Name and ContentUrl are required.");

            try
            {
                var client = httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("User-Agent", "OpenClawNet/1.0");

                var content = await client.GetStringAsync(request.ContentUrl);
                await loader.InstallSkillAsync(request.Name, content);

                return Results.Ok(new { name = request.Name, installed = true });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to install skill '{request.Name}': {ex.Message}", statusCode: 500);
            }
        })
        .WithName("InstallSkill");

        group.MapDelete("/installed/{name}", async (string name, ISkillLoader loader) =>
        {
            await loader.UninstallSkillAsync(name);
            return Results.Ok(new { name, uninstalled = true });
        })
        .WithName("UninstallSkill");
    }

    private static async Task<IResult> ImportSkillsAsync(
        HttpRequest request,
        ISkillLoader loader,
        OpenClawNet.Storage.Services.SkillImportService importService,
        ILogger<Program> logger)
    {
        if (!request.HasFormContentType)
            return Results.BadRequest(new { success = false, error = "Content-Type must be multipart/form-data" });

        var form = await request.ReadFormAsync();
        var file = form.Files.FirstOrDefault();
        if (file == null || file.Length == 0)
            return Results.BadRequest(new { success = false, error = "No file provided" });

        try
        {
            string skillName;

            if (file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                // Handle zip file (folder import)
                using var stream = file.OpenReadStream();
                skillName = await importService.ImportFolderAsync(stream);
            }
            else if (file.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                // Handle markdown file (single file import)
                using var stream = file.OpenReadStream();
                skillName = await importService.ImportSingleFileAsync(file.FileName, stream);
            }
            else
            {
                return Results.BadRequest(new { success = false, error = "Only .md and .zip files are supported" });
            }

            // Reload skills to make the newly imported skill discoverable
            await loader.ReloadAsync();

            return Results.Ok(new
            {
                success = true,
                skillName = skillName,
                message = "Skill imported successfully"
            });
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Skill import validation failed: {Message}", ex.Message);
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Skill import operation failed: {Message}", ex.Message);
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during skill import");
            return Results.Problem(
                detail: "An unexpected error occurred during skill import",
                statusCode: 500,
                instance: "/api/skills/import");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record GitHubContentEntry(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("html_url")] string HtmlUrl);

    private sealed record MarketplaceSkill(string Name, string ContentUrl, string GitHubUrl);

    private sealed record InstallSkillRequest(string Name, string ContentUrl);
}

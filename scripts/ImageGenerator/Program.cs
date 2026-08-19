using ElBruno.Text2Image.Foundry;
using ImageGenerator;
using Microsoft.Extensions.Configuration;

// ── Build configuration: user-secrets → env vars → appsettings.json ──
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables("IMGGEN_")
    .AddUserSecrets(typeof(ImagePrompts).Assembly)
    .Build();

// Helper to read from config (user-secrets/appsettings) or env vars
string? GetSetting(string key) =>
    config[key]
    ?? Environment.GetEnvironmentVariable($"IMGGEN_{key.ToUpperInvariant()}")
    // Keep backward compatibility with old FLUX2_ prefix
    ?? Environment.GetEnvironmentVariable($"FLUX2_{key.ToUpperInvariant()}");

// Output roots — plan repo and public repo
var planAssetsRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "design", "assets"));
var publicRepoRoot = @"D:\openclawnet\openclawnet\docs\design\assets";

// .NET brand logo for img2img reference
var dotnetLogoPath = ImagePrompts.DotNetLogoPath;

// ── CLI argument parsing ──────────────────────────────────────────────
// Usage:
//   dotnet run                       → list all prompts
//   dotnet run -- all                → generate ALL images
//   dotnet run -- 1A 2A 3B           → generate specific IDs
//   dotnet run -- category:slides    → generate all in a category
//   dotnet run -- --dry-run all      → show what would be generated
var ids = args.Where(a => !a.StartsWith("--")).ToList();
bool dryRun = args.Contains("--dry-run");
bool listOnly = ids.Count == 0 && !dryRun;

// Resolve which prompts to generate
var prompts = ResolvePrompts(ids);

if (listOnly)
{
    Console.WriteLine("╔═════════════════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║  OpenClawNet Image Generator — MAI-Image-2 / FLUX.2 via Foundry        ║");
    Console.WriteLine("╚═════════════════════════════════════════════════════════════════════════╝");
    Console.WriteLine();
    Console.WriteLine("Available prompts:");
    Console.WriteLine();
    foreach (var group in ImagePrompts.All.GroupBy(p => p.Category))
    {
        Console.WriteLine($"  📁 {group.Key}/");
        foreach (var p in group)
            Console.WriteLine($"     {p.Id,-6} {p.FileName,-32} {p.Width}×{p.Height}");
    }
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run                          List all prompts");
    Console.WriteLine("  dotnet run -- all                   Generate ALL images");
    Console.WriteLine("  dotnet run -- 1A 2A 3B              Generate specific IDs");
    Console.WriteLine("  dotnet run -- category:slides       Generate all in a category");
    Console.WriteLine("  dotnet run -- --dry-run all         Show what would be generated");
    Console.WriteLine();
    Console.WriteLine("Configuration (user-secrets recommended):");
    Console.WriteLine("  dotnet user-secrets set Endpoint  \"https://your-resource.services.ai.azure.com\"");
    Console.WriteLine("  dotnet user-secrets set ApiKey    \"your-api-key\"");
    Console.WriteLine("  dotnet user-secrets set Model     \"mai\"            (mai | flux2, default: mai)");
    Console.WriteLine("  dotnet user-secrets set ModelId   \"MAI-Image-2\"    (optional)");
    Console.WriteLine("  dotnet user-secrets set ModelName \"MAI-Image-2\"    (optional)");
    Console.WriteLine();
    Console.WriteLine("Also reads from env vars: IMGGEN_ENDPOINT, IMGGEN_APIKEY, IMGGEN_MODEL");
    Console.WriteLine("  (FLUX2_ prefix also accepted for backward compatibility)");
    return;
}

if (dryRun)
{
    Console.WriteLine("🔍 DRY RUN — would generate these images:");
    Console.WriteLine();
    foreach (var p in prompts)
    {
        var planPath = Path.Combine(planAssetsRoot, p.Category, p.FileName);
        var pubPath = Path.Combine(publicRepoRoot, p.Category, p.FileName);
        var refInfo = p.ReferenceImagePath is not null ? $" 🖼️ ref:{p.ReferenceImagePath}" : "";
        Console.WriteLine($"  [{p.Id}] {p.Category}/{p.FileName} ({p.Width}×{p.Height}){refInfo}");
        Console.WriteLine($"       → {planPath}");
        Console.WriteLine($"       → {pubPath}");
    }
    Console.WriteLine($"\n  Total: {prompts.Count} images");
    return;
}

// ── Configuration (only needed when generating) ──────────────────────
var endpoint = GetSetting("Endpoint")
    ?? throw new InvalidOperationException(
        "Endpoint not configured. Run: dotnet user-secrets set Endpoint \"https://your-resource.services.ai.azure.com\"");
var apiKey = GetSetting("ApiKey")
    ?? throw new InvalidOperationException(
        "ApiKey not configured. Run: dotnet user-secrets set ApiKey \"your-api-key\"");

var modelVariant = (GetSetting("Model") ?? "mai").ToLowerInvariant();

// ── Create generator ──────────────────────────────────────────────────
string generatorLabel;
Func<string, ElBruno.Text2Image.ImageGenerationOptions, string?, Task<ElBruno.Text2Image.ImageGenerationResult>> generateAsync;
IDisposable generatorDisposable;

// HttpClient is shared across all generation calls and disposed after the generator.
// Creating it here (not inside the if/else) follows the recommended single-instance
// pattern and avoids socket exhaustion on large batches.
using var httpClient = new HttpClient();

if (modelVariant == "mai")
{
    var modelId = GetSetting("ModelId") ?? "MAI-Image-2";
    var modelName = GetSetting("ModelName") ?? "MAI-Image-2";
    generatorLabel = $"{modelName} ({modelId})";
    var gen = new MaiImage2Generator(endpoint, apiKey, httpClient, modelName, modelId);
    generatorDisposable = gen;
    // MAI-Image-2 does not support img2img — reference images are silently ignored
    generateAsync = (prompt, opts, _) => gen.GenerateAsync(prompt, opts);
}
else
{
    var modelId = GetSetting("ModelId") ?? "FLUX.2-pro";
    var modelName = GetSetting("ModelName") ?? "FLUX.2 Pro";
    generatorLabel = $"{modelName} ({modelId})";
    var gen = new Flux2Generator(endpoint, apiKey, httpClient, modelName, modelId);
    generatorDisposable = gen;
    generateAsync = (prompt, opts, refPath) =>
        refPath is not null
            ? gen.GenerateAsync(prompt, refPath, opts)
            : gen.GenerateAsync(prompt, opts);
}

// ── Generate ──────────────────────────────────────────────────────────
Console.WriteLine($"🎨 Generating {prompts.Count} images with {generatorLabel}...");
Console.WriteLine($"   Endpoint:   {endpoint}");
Console.WriteLine($"   Model:      {generatorLabel}");
Console.WriteLine($"   Plan repo:  {planAssetsRoot}");
Console.WriteLine($"   Public repo:{publicRepoRoot}");
Console.WriteLine();

using (generatorDisposable)
{
    int success = 0, failed = 0;

    foreach (var spec in prompts)
    {
        var hasRef = spec.ReferenceImagePath is not null;
        var refInfo = hasRef
            ? (modelVariant == "mai" ? " + ref img (text-only for MAI)" : " + ref img")
            : "";
        Console.Write($"  [{spec.Id}] {spec.Category}/{spec.FileName} ({spec.Width}×{spec.Height}{refInfo}) ... ");

        try
        {
            var fullPrompt = $"{spec.Prompt.Trim()}\n\nAvoid: {ImagePrompts.NegativePrompt}";

            var options = new ElBruno.Text2Image.ImageGenerationOptions
            {
                Width = spec.Width,
                Height = spec.Height
            };

            var refPath = hasRef ? dotnetLogoPath : null;
            var result = await generateAsync(fullPrompt, options, refPath);

            // Save to both output directories
            foreach (var root in new[] { planAssetsRoot, publicRepoRoot })
            {
                var dir = Path.Combine(root, spec.Category);
                Directory.CreateDirectory(dir);
                var outputPath = Path.Combine(dir, spec.FileName);
                await result.SaveAsync(outputPath);
            }

            Console.WriteLine("✅");
            success++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ {ex.Message}");
            failed++;
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Done! ✅ {success} succeeded, ❌ {failed} failed.");
}

// ── Helpers ───────────────────────────────────────────────────────────
List<ImagePrompts.ImageSpec> ResolvePrompts(List<string> selectors)
{
    if (selectors.Count == 0 || selectors.Any(s => s.Equals("all", StringComparison.OrdinalIgnoreCase)))
        return [.. ImagePrompts.All];

    var resolved = new List<ImagePrompts.ImageSpec>();
    foreach (var sel in selectors)
    {
        if (sel.StartsWith("category:", StringComparison.OrdinalIgnoreCase))
        {
            var cat = sel["category:".Length..];
            resolved.AddRange(ImagePrompts.All.Where(p => p.Category.Equals(cat, StringComparison.OrdinalIgnoreCase)));
        }
        else
        {
            var match = ImagePrompts.All.FirstOrDefault(p => p.Id.Equals(sel, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                resolved.Add(match);
            else
                Console.WriteLine($"  ⚠️  Unknown ID: {sel} — skipping");
        }
    }
    return resolved;
}


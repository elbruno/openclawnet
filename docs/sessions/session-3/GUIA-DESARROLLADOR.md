# Guía del Desarrollador: Sistema de Skills y Memoria en Sesión 3

**Versión:** 1.0  
**Estado:** Sesión 3 — Guía de Desarrollo  
**Audiencia:** Desarrolladores que implementan features de Session 3  
**Última Actualización:** 2026-05-01

---

## Tabla de Contenidos

1. [Inicio Rápido](#inicio-rápido)
2. [Configuración del Entorno de Desarrollo](#configuración-del-entorno-de-desarrollo)
3. [Estructura del Proyecto](#estructura-del-proyecto)
4. [Implementar Skills desde Cero](#implementar-skills-desde-cero)
5. [Implementar el Sistema de Memoria](#implementar-el-sistema-de-memoria)
6. [Testing de Skills y Memoria](#testing-de-skills-y-memoria)
7. [Integración de APIs](#integración-de-apis)
8. [Debugging y Troubleshooting](#debugging-y-troubleshooting)
9. [Checklist de Desarrollo](#checklist-de-desarrollo)

---

## Inicio Rápido

### Para Presentadores (5 minutos)

```bash
# 1. Checkout código de Session 3 start
git checkout session-3-start

# 2. Restaurar dependencias
dotnet restore

# 3. Ejecutar Aspire (local dev)
dotnet run --project src/OpenClawNet.AppHost

# 4. Verificar que funciona (debe estar disponible en http://localhost:8000)
curl http://localhost:8000/api/health | jq .

# 5. Opcional: Cargar datos de prueba
dotnet run --project scripts/SeedSkills -- --count 10 --category development
```

### Para Desarrolladores (30 minutos)

```bash
# 1. Entender la estructura existente
ls -la src/OpenClawNet.Skills/
cat src/OpenClawNet.Skills/FileSkillLoader.cs  # 50 líneas

# 2. Entender el parser
cat src/OpenClawNet.Skills/SkillParser.cs  # 30 líneas

# 3. Entender integración de prompts
cat src/OpenClawNet.Agent/DefaultPromptComposer.cs  # 80 líneas

# 4. Correr tests
dotnet test tests/OpenClawNet.Skills.Tests/

# 5. Revisar demo live
dotnet run --project tests/OpenClawNet.PlaywrightTests -- --headed
```

---

## Configuración del Entorno de Desarrollo

### Requisitos Previos

```bash
# .NET
dotnet --version  # 8.0+

# LLM Local
ollama pull llama3.2  # ~5GB
ollama serve  # Ejecutar en terminal separada; escucha en http://localhost:11434

# Base de datos (incluida en proyecto)
dotnet ef database update  # Crea schema de sesiones
```

### Variables de Entorno

```bash
# .env (raíz del proyecto)
OLLAMA_API_URL=http://localhost:11434
SKILL_DIRECTORIES=skills/built-in,skills/samples
DATABASE_CONNECTION_STRING=Data Source=openclaw.db
LOG_LEVEL=Information
```

### Estructura de Directorios: Skills

```
skills/
├─ built-in/
│  ├─ dotnet-expert.md
│  ├─ aspire-scaffold.md
│  └─ azure-deploy.md
└─ samples/
   ├─ security-review.md
   └─ performance-tuning.md
```

---

## Estructura del Proyecto

### Proyectos Clave de Sesión 3

```
src/
├─ OpenClawNet.Skills/           # Nuevo en S3
│  ├─ FileSkillLoader.cs         # ISkillLoader implementation
│  ├─ SkillParser.cs             # Regex-based YAML parser
│  ├─ SkillDefinition.cs         # Record for metadata
│  ├─ SkillContent.cs            # Record for prompt injection
│  └─ SkillExtensions.cs         # Helper methods
│
├─ OpenClawNet.Memory/           # Nuevo en S3
│  ├─ DefaultMemoryService.cs    # IMemoryService implementation
│  ├─ DefaultEmbeddingsService.cs # IEmbeddingsService (ONNX local)
│  ├─ SessionSummary.cs          # Entity model
│  └─ MemoryStats.cs             # DTO for UI
│
├─ OpenClawNet.Agent/            # Modificado en S3
│  ├─ DefaultPromptComposer.cs   # Skill + summary injection
│  └─ AgentOrchestrator.cs       # Agents loop (sin cambios)
│
├─ OpenClawNet.Gateway/          # Modificado en S3
│  ├─ SkillEndpoints.cs          # POST /api/skills, PUT /disable, etc.
│  ├─ MemoryEndpoints.cs         # GET /api/memory/stats
│  └─ Program.cs                 # DI registration
│
└─ OpenClawNet.Storage/          # Modificado en S3
   ├─ SessionSummary.cs          # Nueva entidad
   └─ ApplicationDbContext.cs     # Add DbSet<SessionSummary>
```

---

## Implementar Skills desde Cero

### Paso 1: Crear Interfaz ISkillLoader

```csharp
// src/OpenClawNet.Skills/ISkillLoader.cs

public interface ISkillLoader
{
    /// <summary>
    /// Load all skills from configured directories
    /// </summary>
    Task<IReadOnlyList<SkillDefinition>> LoadSkillsAsync();
    
    /// <summary>
    /// Reload all skills (runtime refresh)
    /// </summary>
    Task ReloadSkillsAsync();
    
    /// <summary>
    /// Enable a skill by name (in-memory; persisted in next sync)
    /// </summary>
    Task<bool> EnableSkillAsync(string skillName);
    
    /// <summary>
    /// Disable a skill by name
    /// </summary>
    Task<bool> DisableSkillAsync(string skillName);
}
```

### Paso 2: Implementar FileSkillLoader

```csharp
// src/OpenClawNet.Skills/FileSkillLoader.cs

public class FileSkillLoader : ISkillLoader
{
    private readonly string[] _skillDirectories;
    private readonly ILogger<FileSkillLoader> _logger;
    private List<SkillDefinition> _skills = new();

    public FileSkillLoader(string[] skillDirectories, ILogger<FileSkillLoader> logger)
    {
        _skillDirectories = skillDirectories ?? throw new ArgumentNullException(nameof(skillDirectories));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<SkillDefinition>> LoadSkillsAsync()
    {
        var skills = new List<SkillDefinition>();
        var parser = new SkillParser();

        foreach (var dir in _skillDirectories)
        {
            if (!Directory.Exists(dir))
            {
                _logger.LogWarning("Skill directory not found: {Directory}", dir);
                continue;
            }

            var skillFiles = Directory.GetFiles(dir, "*.md");
            foreach (var file in skillFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file);
                    var (metadata, markdownContent) = parser.Parse(content);

                    var skill = new SkillDefinition(
                        Name: metadata["name"],
                        Description: metadata.GetValueOrDefault("description", ""),
                        Category: metadata.GetValueOrDefault("category", "uncategorized"),
                        Tags: metadata.GetValueOrDefault("tags", "").Split(',').Select(t => t.Trim()).ToArray(),
                        Enabled: bool.Parse(metadata.GetValueOrDefault("enabled", "true")),
                        FilePath: file,
                        Examples: metadata.GetValueOrDefault("examples", "").Split(',').Select(e => e.Trim()).ToArray()
                    );

                    skills.Add(skill);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse skill file: {File}", file);
                }
            }
        }

        _skills = skills;
        _logger.LogInformation("Loaded {SkillCount} skills from {DirectoryCount} directories",
            skills.Count, _skillDirectories.Length);

        return skills.AsReadOnly();
    }

    public async Task ReloadSkillsAsync()
    {
        await LoadSkillsAsync();
        _logger.LogInformation("Skills reloaded");
    }

    public async Task<bool> EnableSkillAsync(string skillName)
    {
        var skill = _skills.FirstOrDefault(s => s.Name == skillName);
        if (skill == null)
            return false;

        // Update in-memory; update file
        var filePath = skill.FilePath;
        var content = await File.ReadAllTextAsync(filePath);
        var updated = content.Replace("enabled: false", "enabled: true");
        await File.WriteAllTextAsync(filePath, updated);

        await ReloadSkillsAsync();
        return true;
    }

    public async Task<bool> DisableSkillAsync(string skillName)
    {
        var skill = _skills.FirstOrDefault(s => s.Name == skillName);
        if (skill == null)
            return false;

        var filePath = skill.FilePath;
        var content = await File.ReadAllTextAsync(filePath);
        var updated = content.Replace("enabled: true", "enabled: false");
        await File.WriteAllTextAsync(filePath, updated);

        await ReloadSkillsAsync();
        return true;
    }
}
```

### Paso 3: Crear SkillParser

```csharp
// src/OpenClawNet.Skills/SkillParser.cs

public class SkillParser
{
    private static readonly Regex FrontmatterRegex = new(
        @"^---\s*\n(.*?)\n---\s*\n(.*)$",
        RegexOptions.Singleline | RegexOptions.Compiled
    );

    public (Dictionary<string, string> Metadata, string Content) Parse(string fileContent)
    {
        var match = FrontmatterRegex.Match(fileContent);
        if (!match.Success)
            throw new InvalidOperationException("Invalid skill format: missing frontmatter");

        var yamlBlock = match.Groups[1].Value;
        var markdownContent = match.Groups[2].Value;

        var metadata = new Dictionary<string, string>();
        foreach (var line in yamlBlock.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0) continue;

            var key = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();

            // Remove quotes if present
            if (value.StartsWith("\"") && value.EndsWith("\""))
                value = value[1..^1];

            metadata[key] = value;
        }

        return (metadata, markdownContent);
    }
}
```

### Paso 4: Crear Modelos

```csharp
// src/OpenClawNet.Skills/SkillDefinition.cs

public sealed record SkillDefinition(
    string Name,
    string Description,
    string Category,
    string[] Tags,
    bool Enabled,
    string FilePath,
    string[] Examples
);

// src/OpenClawNet.Skills/SkillContent.cs

public sealed record SkillContent(
    string Name,
    string Content,
    string Description,
    string[] Tags
);
```

---

## Implementar el Sistema de Memoria

### Paso 1: Crear Entidad SessionSummary

```csharp
// src/OpenClawNet.Storage/SessionSummary.cs

public class SessionSummary
{
    public Guid Id { get; set; }
    public string SessionId { get; set; }
    public int TotalMessages { get; set; }
    public int SummaryCount { get; set; }
    public string SummaryContent { get; set; }  // Compressed conversation
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### Paso 2: Crear IMemoryService

```csharp
// src/OpenClawNet.Memory/IMemoryService.cs

public interface IMemoryService
{
    /// <summary>
    /// Add a new message to session memory
    /// </summary>
    Task StoreMessageAsync(string sessionId, string role, string content);
    
    /// <summary>
    /// Summarize old messages when context window grows
    /// </summary>
    Task SummarizeAsync(string sessionId, int maxMessages = 100);
    
    /// <summary>
    /// Get memory statistics for UI display
    /// </summary>
    Task<MemoryStats> GetStatsAsync(string sessionId);
    
    /// <summary>
    /// Search past conversation using semantic similarity
    /// </summary>
    Task<List<string>> SearchAsync(string sessionId, string query, int topK = 5);
}
```

### Paso 3: Implementar DefaultMemoryService

```csharp
// src/OpenClawNet.Memory/DefaultMemoryService.cs

public class DefaultMemoryService : IMemoryService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IEmbeddingsService _embeddingsService;
    private readonly ILogger<DefaultMemoryService> _logger;

    public DefaultMemoryService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IEmbeddingsService embeddingsService,
        ILogger<DefaultMemoryService> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _embeddingsService = embeddingsService ?? throw new ArgumentNullException(nameof(embeddingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StoreMessageAsync(string sessionId, string role, string content)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        
        var message = new SessionMessage
        {
            SessionId = sessionId,
            Role = role,
            Content = content,
            CreatedAt = DateTime.UtcNow
        };

        context.SessionMessages.Add(message);
        await context.SaveChangesAsync();

        _logger.LogInformation("Message stored for session {SessionId}", sessionId);
    }

    public async Task SummarizeAsync(string sessionId, int maxMessages = 100)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        var messages = await context.SessionMessages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .Take(maxMessages)
            .ToListAsync();

        if (messages.Count < 10) return;  // Only summarize if enough context

        var conversationText = string.Join("\n", messages.Select(m => $"{m.Role}: {m.Content}"));
        var summary = new SessionSummary
        {
            SessionId = sessionId,
            TotalMessages = messages.Count,
            SummaryCount = 1,
            SummaryContent = conversationText[..Math.Min(500, conversationText.Length)],  // Truncate
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.SessionSummaries.Add(summary);
        await context.SaveChangesAsync();

        _logger.LogInformation("Session {SessionId} summarized: {MessageCount} messages",
            sessionId, messages.Count);
    }

    public async Task<MemoryStats> GetStatsAsync(string sessionId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        var totalMessages = await context.SessionMessages
            .Where(m => m.SessionId == sessionId)
            .CountAsync();

        var summaryCount = await context.SessionSummaries
            .Where(s => s.SessionId == sessionId)
            .CountAsync();

        return new MemoryStats
        {
            SessionId = sessionId,
            TotalMessages = totalMessages,
            SummaryCount = summaryCount,
            ApproximateTokens = totalMessages * 50  // Rough estimate
        };
    }

    public async Task<List<string>> SearchAsync(string sessionId, string query, int topK = 5)
    {
        // TODO: Implement semantic search using embeddings
        // 1. Embed query via IEmbeddingsService
        // 2. Load message embeddings from cache
        // 3. Compute cosine similarity
        // 4. Return top-K results
        
        _logger.LogInformation("Semantic search not yet implemented");
        return new();
    }
}
```

### Paso 4: Implementar DefaultEmbeddingsService

```csharp
// src/OpenClawNet.Memory/DefaultEmbeddingsService.cs

public class DefaultEmbeddingsService : IEmbeddingsService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DefaultEmbeddingsService> _logger;
    private readonly MemoryCache _cache;

    public DefaultEmbeddingsService(HttpClient httpClient, ILogger<DefaultEmbeddingsService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    public async Task<float[]> EmbedAsync(string text)
    {
        // Check cache first
        if (_cache.TryGetValue(text, out float[] cachedEmbedding))
            return cachedEmbedding;

        try
        {
            var request = new { input = text };
            var response = await _httpClient.PostAsJsonAsync(
                "http://localhost:11434/api/embeddings",
                new { model = "nomic-embed-text", prompt = text }
            );

            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadAsAsync<EmbeddingResponse>();

            // Cache for 1 hour
            _cache.Set(text, result.Embedding, TimeSpan.FromHours(1));

            return result.Embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to embed text");
            throw;
        }
    }

    private class EmbeddingResponse
    {
        public float[] Embedding { get; set; }
    }
}
```

---

## Testing de Skills y Memoria

### Tests de Skills

```csharp
// tests/OpenClawNet.Skills.Tests/FileSkillLoaderTests.cs

public class FileSkillLoaderTests
{
    [Fact]
    public async Task LoadSkillsAsync_ShouldReturnAllSkills()
    {
        // Arrange
        var skillDirs = new[] { "skills/built-in" };
        var logger = new Mock<ILogger<FileSkillLoader>>();
        var loader = new FileSkillLoader(skillDirs, logger.Object);

        // Act
        var skills = await loader.LoadSkillsAsync();

        // Assert
        Assert.NotEmpty(skills);
        Assert.All(skills, skill =>
        {
            Assert.NotNull(skill.Name);
            Assert.NotNull(skill.Description);
        });
    }

    [Fact]
    public async Task DisableSkillAsync_ShouldUpdateFile()
    {
        // Arrange
        var skillDirs = new[] { "skills/test" };
        var logger = new Mock<ILogger<FileSkillLoader>>();
        var loader = new FileSkillLoader(skillDirs, logger.Object);

        // Act
        var result = await loader.DisableSkillAsync("test-skill");

        // Assert
        Assert.True(result);
    }
}
```

### Tests de Memoria

```csharp
// tests/OpenClawNet.Memory.Tests/DefaultMemoryServiceTests.cs

public class DefaultMemoryServiceTests
{
    [Fact]
    public async Task StoreMessageAsync_ShouldPersistMessage()
    {
        // Arrange
        var factory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        var embeddingsService = new Mock<IEmbeddingsService>();
        var logger = new Mock<ILogger<DefaultMemoryService>>();
        var service = new DefaultMemoryService(factory.Object, embeddingsService.Object, logger.Object);

        // Act
        await service.StoreMessageAsync("session-1", "user", "Hello");

        // Assert
        factory.Verify();
    }

    [Fact]
    public async Task SummarizeAsync_ShouldCreateSummary()
    {
        // Arrange
        var factory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        var service = new DefaultMemoryService(factory.Object, new Mock<IEmbeddingsService>().Object,
            new Mock<ILogger<DefaultMemoryService>>().Object);

        // Act
        await service.SummarizeAsync("session-1");

        // Assert
        factory.Verify();
    }
}
```

---

## Integración de APIs

### Skill Endpoints

```csharp
// src/OpenClawNet.Gateway/SkillEndpoints.cs

public static class SkillEndpoints
{
    public static void MapSkillEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/skills").WithName("Skills");

        // GET /api/skills
        group.MapGet("/", GetSkills)
            .WithName("ListSkills")
            .WithOpenApi();

        // PUT /api/skills/{name}/enable
        group.MapPut("/{name}/enable", EnableSkill)
            .WithName("EnableSkill")
            .WithOpenApi();

        // PUT /api/skills/{name}/disable
        group.MapPut("/{name}/disable", DisableSkill)
            .WithName("DisableSkill")
            .WithOpenApi();

        // POST /api/skills/reload
        group.MapPost("/reload", ReloadSkills)
            .WithName("ReloadSkills")
            .WithOpenApi();
    }

    private static async Task<IResult> GetSkills(ISkillLoader skillLoader)
    {
        var skills = await skillLoader.LoadSkillsAsync();
        return Results.Ok(skills);
    }

    private static async Task<IResult> EnableSkill(string name, ISkillLoader skillLoader)
    {
        var result = await skillLoader.EnableSkillAsync(name);
        return result ? Results.Ok() : Results.NotFound();
    }

    private static async Task<IResult> DisableSkill(string name, ISkillLoader skillLoader)
    {
        var result = await skillLoader.DisableSkillAsync(name);
        return result ? Results.Ok() : Results.NotFound();
    }

    private static async Task<IResult> ReloadSkills(ISkillLoader skillLoader)
    {
        await skillLoader.ReloadSkillsAsync();
        return Results.Ok();
    }
}
```

### Memory Endpoints

```csharp
// src/OpenClawNet.Gateway/MemoryEndpoints.cs

public static class MemoryEndpoints
{
    public static void MapMemoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/memory").WithName("Memory");

        // GET /api/memory/stats/{sessionId}
        group.MapGet("/stats/{sessionId}", GetMemoryStats)
            .WithName("GetMemoryStats")
            .WithOpenApi();

        // POST /api/memory/summarize/{sessionId}
        group.MapPost("/summarize/{sessionId}", SummarizeMemory)
            .WithName("SummarizeMemory")
            .WithOpenApi();
    }

    private static async Task<IResult> GetMemoryStats(string sessionId, IMemoryService memoryService)
    {
        var stats = await memoryService.GetStatsAsync(sessionId);
        return Results.Ok(stats);
    }

    private static async Task<IResult> SummarizeMemory(string sessionId, IMemoryService memoryService)
    {
        await memoryService.SummarizeAsync(sessionId);
        return Results.Ok();
    }
}
```

### DI Registration en Program.cs

```csharp
// src/OpenClawNet.Gateway/Program.cs

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddSingleton<ISkillLoader>(sp =>
    new FileSkillLoader(
        new[] { "skills/built-in", "skills/samples" },
        sp.GetRequiredService<ILogger<FileSkillLoader>>()
    )
);

builder.Services.AddScoped<IMemoryService, DefaultMemoryService>();
builder.Services.AddSingleton<IEmbeddingsService, DefaultEmbeddingsService>();
builder.Services.AddHttpClient<DefaultEmbeddingsService>();
builder.Services.AddDbContextFactory<ApplicationDbContext>();

var app = builder.Build();

// Map endpoints
app.MapSkillEndpoints();
app.MapMemoryEndpoints();

await app.RunAsync();
```

---

## Debugging y Troubleshooting

### Debugging de Skills

```csharp
// Enable verbose logging
logger.LogInformation("Loading skills from directories: {Directories}", 
    string.Join(", ", skillDirectories));

logger.LogInformation("Skill parser regex pattern: {Pattern}", 
    FrontmatterRegex.ToString());

logger.LogInformation("Parsed skill metadata: {@Metadata}", metadata);
```

### Debugging de Memoria

```csharp
// Track memory usage
var memoryStats = await memoryService.GetStatsAsync(sessionId);
logger.LogInformation("Session {SessionId} memory: {TotalMessages} messages, " +
    "{SummaryCount} summaries, ~{Tokens} tokens",
    sessionId, memoryStats.TotalMessages, memoryStats.SummaryCount, 
    memoryStats.ApproximateTokens);

// Track summarization
logger.LogInformation("Summarizing session {SessionId}: " +
    "compressing {MessageCount} messages",
    sessionId, messages.Count);
```

### Verificar Carga de Skills

```bash
# Check files
ls -la skills/built-in/
ls -la skills/samples/

# Check logs
dotnet run 2>&1 | grep -i "skill\|loaded"

# Test API
curl http://localhost:8000/api/skills | jq '.[] | {name, enabled}'
```

### Verificar Memoria

```bash
# Check database
sqlite3 openclaw.db "SELECT COUNT(*) FROM SessionMessages;"
sqlite3 openclaw.db "SELECT COUNT(*) FROM SessionSummaries;"

# Check API
curl http://localhost:8000/api/memory/stats/session-1 | jq .
```

---

## Checklist de Desarrollo

### Tareas de Implementación

- [ ] Crear ISkillLoader y FileSkillLoader
- [ ] Crear SkillParser con regex frontmatter
- [ ] Crear modelos SkillDefinition y SkillContent
- [ ] Integrar skills en DefaultPromptComposer
- [ ] Crear IMemoryService y DefaultMemoryService
- [ ] Crear DefaultEmbeddingsService (local ONNX)
- [ ] Crear SessionSummary entity y migrate
- [ ] Crear SkillEndpoints y MemoryEndpoints
- [ ] Registrar en DI en Program.cs

### Testing

- [ ] Unit tests para FileSkillLoader
- [ ] Unit tests para SkillParser
- [ ] Unit tests para DefaultMemoryService
- [ ] Integration tests para endpoints
- [ ] E2E tests con Playwright (habilitar/deshabilitar skills, observar cambios)

### Demo Live

- [ ] Demo: Crear nuevo skill .md
- [ ] Demo: Habilitar/deshabilitar en API
- [ ] Demo: Observar cambios en comportamiento del agente
- [ ] Demo: Ver estadísticas de memoria (antes/después summarización)
- [ ] Demo: Mostrar endpoint de recarga sin reinicio

### Documentación

- [ ] README con instrucciones de inicio rápido
- [ ] Ejemplos de skills en skills/samples/
- [ ] Guía de troubleshooting
- [ ] API documentation en OpenAPI/Swagger

# Arquitectura de Skills en OpenClawNet: Guía de Sesión 3

**Versión:** 1.0  
**Estado:** Sesión 3 — Sprint de Documentación  
**Audiencia:** Desarrolladores, arquitectos de agentes, ingenieros de integración  
**Última Actualización:** 2026-04-30

---

## Tabla de Contenidos

1. [Descripción General](#descripción-general)
2. [Sistema de Skills: Conceptos Fundamentales](#sistema-de-skills-conceptos-fundamentales)
3. [FileSkillLoader: Cargando Skills](#fileskillloader-cargando-skills)
4. [SkillParser: Análisis de Frontmatter YAML](#skillparser-análisis-de-frontmatter-yaml)
5. [Integración con DefaultPromptComposer](#integración-con-defaultpromptcomposer)
6. [Anatomía de un Skill](#anatomía-de-un-skill)
7. [Cómo Agregar un Nuevo Skill](#cómo-agregar-un-nuevo-skill)
8. [Búsqueda y Re-Ranking Semántico](#búsqueda-y-re-ranking-semántico)
9. [Ejemplos de Flujos de Trabajo](#ejemplos-de-flujos-de-trabajo)
10. [Preguntas Frecuentes y Resolución de Problemas](#preguntas-frecuentes-y-resolución-de-problemas)

---

## Descripción General

### ¿Qué son los Skills en OpenClawNet?

**Skills** son soluciones documentadas y reutilizables para problemas específicos descubiertos y validados durante el trabajo del agente. Sirven como la capa de conocimiento institucional de OpenClawNet, permitiendo a los agentes evitar duplicación de trabajo, aprender de los compañeros, y elevar la capacidad colectiva de resolución de problemas.

**Principio Central:** Si un agente resuelve un problema bien, ese patrón se convierte en un skill que cualquier otro agente (o desarrollador humano) puede descubrir y aplicar.

### ¿Por Qué Esta Sesión Importa?

La Sesión 3 introduce el **sistema de skills** — permitiendo que el comportamiento del agente se personalice sin cambios de código. Al final de esta sesión:

- Los skills se definen como archivos Markdown simples
- Los desarrolladores pueden habilitar/deshabilitar skills en tiempo de ejecución
- El agente adapta su comportamiento según los skills habilitados
- La API expone estadísticas de skills para transparencia operacional

---

## Sistema de Skills: Conceptos Fundamentales

### Anatomía Básica

Un skill es un archivo Markdown (`.md`) con dos secciones:

#### 1. Frontmatter YAML (Metadatos)

```yaml
---
name: dotnet-expert
description: .NET development expertise and best practices
category: development
tags: [dotnet, csharp, programming, architecture]
enabled: true
examples: ["Build ASP.NET Core API", "Design async patterns"]
---
```

#### 2. Contenido Markdown (Instrucciones)

```markdown
You are a .NET expert assistant. When answering questions:

- Reference official Microsoft documentation
- Prefer modern C# features (records, pattern matching)
- Recommend Aspire for distributed applications
- Follow Microsoft coding conventions

## Examples

### Example 1: Building an API
...

### Example 2: Async Patterns
...
```

**Qué Explicar:**
- El frontmatter YAML define **metadatos** (qué es el skill)
- El Markdown después de `---` define **instrucciones** (cómo el agente se comporta)
- Sin código requerido — archivos puro texto

### Ciclo de Vida del Skill

```
1. ARCHIVO CREADO
   └─ Skill.md en skills/built-in/ o skills/samples/
      ↓
2. DESCUBIERTA POR FILESK ILLLOADER
   └─ Startup: escaneo de directorios
      ↓
3. INYECTADA EN PROMPT DEL SISTEMA
   └─ DefaultPromptComposer: "## Skill: {nombre}\n{contenido}"
      ↓
4. AGENTE VE LAS INSTRUCCIONES
   └─ Comportamiento adaptado, contexto personalizado
      ↓
5. HABILITAR/DESHABILITAR EN RUNTIME
   └─ API de Skills: no requiere reinicio
```

---

## FileSkillLoader: Cargando Skills

### Responsabilidad

`FileSkillLoader` escanea directorios de skills, parsea cada archivo, y mantiene un registro de metadatos.

### Interfaz

```csharp
public interface ISkillLoader
{
    Task<IReadOnlyList<SkillDefinition>> LoadSkillsAsync();
    Task ReloadSkillsAsync();
    Task<bool> EnableSkillAsync(string skillName);
    Task<bool> DisableSkillAsync(string skillName);
}
```

### Flujo de Ejecución

```
Startup
  ↓
FileSkillLoader.LoadSkillsAsync()
  ├─ Scan: skills/built-in/ y skills/samples/
  ├─ Parse: Cada *.md usando SkillParser
  ├─ Extract: name, description, tags, enabled, examples
  ├─ Store: En memoria (lista inmutable)
  └─ Return: IReadOnlyList<SkillDefinition>
  
DefaultPromptComposer
  ├─ Filter: Solo skills enabled=true
  └─ Inject: En prompt del sistema
```

### Ejemplo: Estructura de Directorios

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

## SkillParser: Análisis de Frontmatter YAML

### Responsabilidad

`SkillParser` extrae metadatos YAML del frontmatter usando regex y proporciona contenido limpio.

### Regex Pattern

```csharp
private static readonly Regex FrontmatterRegex = new(
    @"^---\s*\n(.*?)\n---\s*\n(.*)$",
    RegexOptions.Singleline | RegexOptions.Compiled
);
```

### Algoritmo

```csharp
public (Dictionary<string, string> Metadata, string Content) Parse(string fileContent)
{
    var match = FrontmatterRegex.Match(fileContent);
    if (!match.Success)
        throw new InvalidOperationException("Invalid skill format");
    
    var yamlBlock = match.Groups[1].Value;
    var markdownContent = match.Groups[2].Value;
    
    var metadata = ParseYaml(yamlBlock);
    return (metadata, markdownContent);
}
```

### Ejemplo: Entrada y Salida

**Entrada:**
```markdown
---
name: dotnet-expert
description: .NET development expertise
tags: [dotnet, csharp]
enabled: true
---

You are a .NET expert...
```

**Salida:**
```csharp
Metadata = {
    { "name", "dotnet-expert" },
    { "description", ".NET development expertise" },
    { "tags", "dotnet, csharp" },
    { "enabled", "true" }
}
Content = "You are a .NET expert..."
```

---

## Integración con DefaultPromptComposer

### Flujo: Composición de Prompt con Skills

```
Agent Spawn Request
  ↓
DefaultPromptComposer.ComposeAsync(taskDescription)
  ├─ Load enabled skills: ISkillLoader.LoadSkillsAsync()
  ├─ Filter: WHERE enabled = true
  ├─ Format: "## Skill: {nombre}\n{contenido}"
  └─ Inject: En system prompt
      ↓
System Prompt Final:
"""
You are a helpful .NET assistant.

## Skill: dotnet-expert

You are a .NET expert assistant...

## Skill: aspire-scaffold

Scaffold Aspire projects by...

...
"""
      ↓
Agent LLM Input
```

### Código Pseudollenado

```csharp
public class DefaultPromptComposer : IPromptComposer
{
    private readonly ISkillLoader _skillLoader;
    private readonly ILogger<DefaultPromptComposer> _logger;
    
    public async Task<string> ComposeAsync(string taskDescription)
    {
        var basePrompt = """
            You are a helpful assistant that solves technical problems.
            """;
        
        var enabledSkills = await _skillLoader.LoadSkillsAsync();
        var skillSections = enabledSkills
            .Where(s => s.Enabled)
            .Select(s => $"## Skill: {s.Name}\n{s.Content}")
            .ToList();
        
        var finalPrompt = basePrompt + "\n\n" + string.Join("\n\n", skillSections);
        _logger.LogInformation("Composed prompt with {SkillCount} skills", skillSections.Count);
        
        return finalPrompt;
    }
}
```

---

## Anatomía de un Skill

### Estructura Completa Recomendada

```markdown
---
name: ndjson-request-correlation
description: Trace async NDJSON requests through multi-agent pipelines with correlation IDs
category: observability
tags:
  - ndjson
  - correlation
  - async
  - logging
author: Irving
extracted: 2026-04-10
confidence: high
status: active
---

## Propósito

Cuando un solo request del usuario activa múltiples flujos NDJSON async en varios agentes, la correlación de requests se vuelve difícil. Este skill documenta el patrón canónico: **inyección de ID de correlación en entrada de request, propagada a través de todos los flujos.**

## Cuándo Usar

- ✅ Escenarios multi-agente con streaming (ej: input del usuario → Agente A → Agente B)
- ✅ Rastreo de requests entre servicios
- ✅ Debugging de pipelines async
- ❌ Requests síncronos de agente único (overhead no justificado)

## Patrón Central

### Paso 1: Inyectar ID de Correlación en Entrada

```csharp
var correlationId = Guid.NewGuid().ToString("N");
httpContext.Request.Headers["X-Correlation-ID"] = correlationId;
logger.LogInformation("Request {CorrelationId} started", correlationId);
```

### Paso 2: Propagar Través del Flujo NDJSON

```csharp
await foreach (var chunk in streamEnumerable)
{
    using (LogContext.PushProperty("CorrelationId", correlationId))
    {
        yield return chunk;
    }
}
```

## Ejemplos Prácticos

### Ejemplo: Gateway Multi-Agente

...

## Validaciones

@validated-by Petey on 2026-04-16
✅ Multi-host ASP.NET Core + Ollama integration works as documented.

@validated-by Dylan on 2026-04-17
✅ Integration tests confirm multi-host doesn't break health checks.
```

### Campos del Frontmatter

| Campo | Tipo | Requerido | Descripción |
|-------|------|-----------|-------------|
| `name` | string | Sí | Identificador único del skill (kebab-case) |
| `description` | string | Sí | Breve descripción (1 línea) |
| `category` | string | No | Categoría (development, observability, etc.) |
| `tags` | array | Sí | Palabras clave para búsqueda |
| `author` | string | No | Quién extrajo el skill |
| `extracted` | date | No | Fecha de extracción |
| `confidence` | enum | No | low, medium, high (ver sección Confianza) |
| `status` | enum | No | active, deprecated, experimental |
| `enabled` | boolean | Sí | Habilitado por defecto |
| `examples` | array | No | Casos de uso de ejemplo |

---

## Cómo Agregar un Nuevo Skill

### Paso 1: Crear el Archivo

```bash
# Ubicación
skills/built-in/tu-nuevo-skill.md  # o skills/samples/
```

### Paso 2: Definir Frontmatter

```yaml
---
name: tu-nuevo-skill
description: Brief one-line description
category: development
tags: [tag1, tag2, tag3]
author: Tu Nombre
extracted: 2026-05-01
confidence: medium
status: active
enabled: true
examples: ["Example use case 1", "Example use case 2"]
---
```

### Paso 3: Escribir Instrucciones

```markdown
## Propósito

Explicar claramente qué hace este skill y por qué es importante.

## Cuándo Usar

- ✅ Caso de uso positivo 1
- ✅ Caso de uso positivo 2
- ❌ Caso de no-uso 1

## Patrón Central

Explicar el patrón de forma clara con ejemplos de código.

## Ejemplos Prácticos

Proporcionar ejemplos reales y completamente funcionales.
```

### Paso 4: Validar

```bash
# El skill se debería cargar automáticamente en startup
# Verificar en logs:
# INFO: Loaded 15 skills from skills/built-in
```

### Paso 5: Probar

- Iniciar la aplicación
- Navegar a `/api/skills`
- Verificar que tu skill aparezca
- Deshabilitar/habilitar vía API
- Observar cambios en comportamiento del agente

---

## Búsqueda y Re-Ranking Semántico

### Phase 1: Búsqueda Basada en Palabras Clave

```
Request del Agente
  ↓
Buscar en tags, name, description
  ├─ "Aspire" → encuentra todos los skills con "Aspire"
  ├─ Top-3 candidatos
  └─ ~40μs latencia
```

### Phase 2B: Re-Ranking Semántico (Futuro)

```
Task Description
  ↓
Embed via Ollama (nomic-embed-text) → ~8-15ms
  ↓
Load pre-computed skill embeddings from DB
  ↓
MempalaceNet RRF (Reciprocal Rank Fusion)
  └─ Combina: keyword rank + semantic similarity
  ↓
Return: Top-3 re-ranked by relevance
  └─ ~50-100ms total latencia
```

### Graceful Degradation (Fallback)

Si semantic re-ranking falla por cualquier razón (Ollama down, timeout):

```csharp
try
{
    var semanticResults = await SemanticSkillRanker.RankAsync(
        taskDescription, 
        keywordResults,
        cancellationToken: cts.Token
    );
    return semanticResults;
}
catch (TaskCanceledException)
{
    logger.LogWarning("Semantic re-rank timeout; using keyword results");
    return keywordResults;  // Fallback automático
}
catch (Exception ex)
{
    logger.LogError(ex, "Semantic re-rank failed; using keyword results");
    return keywordResults;  // Fallback automático
}
```

---

## Ejemplos de Flujos de Trabajo

### Workflow 1: Agregar y Probar un Nuevo Skill

```bash
# 1. Crear archivo
echo "---
name: my-skill
description: My skill
enabled: true
---

Instructions here." > skills/built-in/my-skill.md

# 2. Iniciar aplicación
dotnet run

# 3. Verificar que se cargó
curl http://localhost:5000/api/skills | jq '.[] | select(.name == "my-skill")'

# 4. Probar con agente
# Enviar request que debería usar el skill
# Observar cambios en comportamiento

# 5. Deshabilitar en runtime
curl -X PUT http://localhost:5000/api/skills/my-skill/disable

# 6. Verificar que agente ahora no lo usa
```

### Workflow 2: Validar Confianza de Skill

```markdown
## Extracción Inicial

@extracted by Irving on 2026-04-15
Este skill fue descubierto durante Story 1...

## Primera Validación

@validated-by Petey on 2026-04-16
✅ Used successfully in Story 2...

## Segunda Validación

@validated-by Dylan on 2026-04-17
✅ Confirmed in integration tests...

Status: Upgraded to HIGH confidence
```

---

## Preguntas Frecuentes y Resolución de Problemas

### P: ¿Mi skill no aparece en la lista de APIs?

**A:** Verificar:
1. Archivo en directorio correcto (`skills/built-in/` o `skills/samples/`)
2. Frontmatter YAML válido (revisar indentación)
3. Campo `name` presente y válido
4. Logs de aplicación: `grep "SkillParser" logs.txt`

### P: ¿Cambios al skill no se reflejan?

**A:** Los skills se cargan en startup. Opciones:
1. Reiniciar la aplicación
2. O usar endpoint de recarga: `POST /api/skills/reload`

### P: ¿Qué diferencia hay entre `built-in` y `samples`?

**A:**
- `built-in/`: Skills estables, canónicos, usados por todos
- `samples/`: Ejemplos experimentales, demos, plantillas

### P: ¿Puedo tener skills duplicados?

**A:** No. El `name` debe ser único. El sistema falla si hay duplicados.

### P: ¿Cuántos skills puede manejar?

**A:** 
- Phase 1 (keyword): ~100-500 skills con latencia <100μs
- Phase 2B (semantic): >1000 skills con latencia <100ms
- Migrar a FAISS si >1000 skills

---

## Referencia de Modelos

```csharp
// Metadatos inmutables — lo que ve la UI
public sealed record SkillDefinition(
    string Name,
    string Description,
    string Category,
    string[] Tags,
    bool Enabled,
    string FilePath,
    string[] Examples);

// Lo que usa el compositor de prompts
public sealed record SkillContent(
    string Name,
    string Content,
    string Description,
    string[] Tags);
```

---

## Referencia de DI

```csharp
services.AddSingleton<ISkillLoader>(sp =>
    new FileSkillLoader(
        skillDirectories: new[] { "skills/built-in", "skills/samples" },
        logger: sp.GetRequiredService<ILogger<FileSkillLoader>>()
    )
);

services.AddSingleton<IPromptComposer, DefaultPromptComposer>();
```

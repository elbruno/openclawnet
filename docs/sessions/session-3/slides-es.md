---
marp: true
title: "OpenClawNet — Sesión 3: Extracción Automática de Memoria para Agentes"
description: "Ventanas de contexto, resumen automático, búsqueda semántica y memoria a largo plazo"
theme: openclaw
paginate: true
size: 16:9
footer: "OpenClawNet · Sesión 3 · Extracción Automática de Memoria"
---

<!-- _class: lead -->

# OpenClawNet
## Sesión 3 — Extracción Automática de Memoria

**Serie Microsoft Reactor · ~75 min · .NET Intermedio**

> *De chatbot a colega: agentes que recuerdan qué pasó la última vez.*

<br>

<div class="speakers">

**Bruno Capuano** — Principal Cloud Advocate, Microsoft
[github.com/elbruno](https://github.com/elbruno) · [@elbruno](https://twitter.com/elbruno)

**Pablo Nunes Lopes** — Cloud Advocate, Microsoft
[linkedin.com/in/pablonuneslopes](https://www.linkedin.com/in/pablonuneslopes/)

</div>

<!--
NOTAS DEL ORADOR — diapositiva de título.
Bienvenidos de nuevo a la Sesión 3. En la Sesión 1 construimos una aplicación de chat en Aspire. En la Sesión 2 le dimos al agente manos—herramientas y la capacidad de actuar. Hoy abordamos uno de los problemas más difíciles en el diseño de agentes: la memoria. ¿Cómo permitimos que un agente aprenda de conversaciones pasadas sin almacenar gigabytes de tokens? ¿Cómo encontramos contexto relevante del historial de meses en milisegundos? Hoy construimos el sistema de memoria que los agentes OpenClawNet usan para recordar tus preferencias, tu conocimiento de dominio y qué pasó la última vez—sin quebrantar el presupuesto en tokens o latencia.
-->

---

## Dónde quedaron las Sesiones 1–2

- **Sesión 1** — Fundación Aspire, `IAgentProvider`, streaming NDJSON, SQLite
- **Sesión 2** — `ITool` + MCP, bucle de agente, puertas de aprobación, modelo de seguridad
- 5 herramientas en proceso, 5 servidores MCP incluidos
- 3 ataques bloqueados: traversal de ruta, inyección de comandos, SSRF
- `aspire start` → agente que usa herramientas en 30 segundos

> Hoy: **memoria a largo plazo sin romper ventanas de contexto.**

<!--
NOTAS DEL ORADOR — resumen.
Resumen rápido. La Sesión 1 nos dio un stack Aspire funcional con cinco proveedores de modelos detrás de una interfaz. La Sesión 2 nos dio el bucle de agente: llamadas a herramientas, políticas de aprobación e historia de seguridad. Ambas están en GitHub. Esta sesión asume que tienes la Sesión 2 en funcionamiento. Si no, la grabación y código están en github.com/elbruno/openclawnet.
-->

---

## El problema de la memoria

| Sin Memoria | **Con Sistema de Memoria** |
|---|---|
| Límite de token alcanzado → conversación truncada | Resumir mensajes antiguos → seguir hablando |
| "Cuéntame de nuevo sobre X" → sin idea | Búsqueda semántica → encontrar en segundos |
| Mismo contexto enviado cada solicitud | Enviar solo resúmenes relevantes |
| Costoso para conversaciones largas | 10x más barato. Recuperación de contexto 100x más rápida |
| Agente aprende nada | Agente aprende personalidad y conocimiento de dominio |

> **Idea clave:** Todo LLM tiene una ventana de contexto. El momento que una conversación la exceda, tienes dos opciones: truncar o comprimir.

<!--
NOTAS DEL ORADOR — problema.
Aquí está la brecha. Sin memoria, después de 50–100 mensajes se te acaban los tokens. O pierdes contexto o pagas exponencialmente más para mantenerlo. Con un sistema de memoria, comprimes mensajes antiguos en resúmenes y los almacenas. Cuando necesitas contexto, buscas por significado, no por palabras clave. Así una conversación que costaría 50x en Azure OpenAI cuesta 1x con compresión inteligente. Y el agente no solo recuerda hechos—aprende conocimiento de dominio y personalidad.
-->

---

## Objetivos de la Fase 3

1. **Resumen automático** — Mantener mensajes recientes, comprimir antiguos
2. **Almacenamiento persistente** — Entidades SessionSummary en SQLite
3. **Búsqueda semántica** — Encontrar conversaciones pasadas por significado, no palabras clave
4. **Incrustaciones locales** — Modelos ONNX, sin llamadas API, datos no salen de tu máquina
5. **UI transparente** — Los usuarios ven estadísticas de memoria (mensajes, cuenta de resumen, última actualización)
6. **Arquitectura sin reinicio** — Habilitar/deshabilitar habilidades sin reinicio; memoria persiste entre sesiones

**Alcance:** ~75 minutos | **Nivel:** .NET Intermedio | **Construye sobre:** Sesión 2

<!--
NOTAS DEL ORADOR — objetivos.
Seis objetivos para hoy. Primero, configuramos el disparador de resumen—después de N mensajes, comprimir. Segundo, persistimos esos resúmenes en la base de datos para que sobrevivan a reinicios. Tercero, convertimos conversaciones a vectores para poder encontrar significado, no solo palabras clave. Cuarto, lo hacemos localmente con modelos ONNX—sin llamadas API a Azure OpenAI. Quinto, lo hacemos visible al usuario. Sexto, configuramos la arquitectura para poder cargar habilidades en caliente y el sistema de memoria sigue funcionando.
-->

---

## Arquitectura de Extracción de Memoria

```
┌─────────────────────┐
│  Conversación Chat  │  (los mensajes fluyen)
└──────────┬──────────┘
           │
    [Disparador: N mensajes]
           │
           ▼
┌─────────────────────────────────────┐
│  DefaultMemoryService               │
│  ├─ Comprimir mensajes → resumen   │  (resumen basado en LLM)
│  ├─ Persistir en SessionSummary    │  (base de datos)
│  └─ Extraer hechos clave           │  (PNL)
└──────────┬──────────────────────────┘
           │
           ▼
┌─────────────────────────────────────┐
│  DefaultEmbeddingsService           │
│  ├─ Convertir a vectores (ONNX)    │  (inferencia local)
│  ├─ Búsqueda de similitud coseno   │  (coincidencia rápida)
│  └─ Almacenar incrustaciones       │
└──────────┬──────────────────────────┘
           │
           ▼
┌─────────────────────────────────────┐
│  Compositor de Prompt               │
│  ├─ Obtener resúmenes relevantes   │  (búsqueda semántica)
│  ├─ Inyectar en prompt del sistema │  (aumento de contexto)
│  └─ Enviar al modelo               │
└─────────────────────────────────────┘
```

<!--
NOTAS DEL ORADOR — arquitectura.
Aquí está el flujo. Llega conversación. Después de N mensajes, se activa el servicio de memoria: toma los mensajes antiguos, los envía al LLM con un prompt "resuma esto", obtiene un resumen comprimido, y lo almacena. Mientras tanto, el servicio de incrustaciones convierte tanto los mensajes originales como el resumen en vectores numéricos usando un modelo ONNX en tu CPU—sin llamada API, sin pico de latencia. Cuando necesitas contexto para la siguiente solicitud, el compositor de prompt consulta el servicio de incrustaciones para conversaciones similares, recupera los resúmenes, e inyectalos al inicio del prompt del sistema. El agente obtiene el contexto que necesita sin el costo de tokens.
-->

---

## Implementación: DefaultMemoryService

```csharp
public class DefaultMemoryService : IMemoryService
{
    private readonly IDbContextFactory<OpenClawDbContext> _dbContextFactory;
    private readonly IEmbeddingsService _embeddings;
    private readonly IAgentProvider _agentProvider;

    // Mantener N mensajes recientes literalmente; resumir los más antiguos
    private const int VERBATIM_THRESHOLD = 10;

    public async Task<string?> GetSessionSummaryAsync(Guid sessionId)
    {
        using var db = _dbContextFactory.CreateDbContext();
        return await db.SessionSummaries
            .Where(s => s.SessionId == sessionId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.Summary)
            .FirstOrDefaultAsync();
    }

    public async Task StoreSummaryAsync(
        Guid sessionId, 
        string summary, 
        int messageCount)
    {
        using var db = _dbContextFactory.CreateDbContext();
        db.SessionSummaries.Add(new SessionSummary
        {
            SessionId = sessionId,
            Summary = summary,
            CoveredMessageCount = messageCount
        });
        await db.SaveChangesAsync();
    }

    public async Task<MemoryStats> GetStatsAsync(Guid sessionId)
    {
        using var db = _dbContextFactory.CreateDbContext();
        var messages = await db.Messages
            .Where(m => m.SessionId == sessionId)
            .CountAsync();
            
        var summaries = await db.SessionSummaries
            .Where(s => s.SessionId == sessionId)
            .ToListAsync();

        return new MemoryStats(
            TotalMessages: messages,
            SummaryCount: summaries.Count,
            CoveredMessages: summaries.Sum(s => s.CoveredMessageCount),
            LastSummaryAt: summaries.MaxBy(s => s.CreatedAt)?.CreatedAt
        );
    }

    // Disparar resumen si es necesario después de VERBATIM_THRESHOLD mensajes
    public async Task SummarizeIfNeededAsync(Guid sessionId)
    {
        var stats = await GetStatsAsync(sessionId);
        if ((stats.TotalMessages - stats.CoveredMessages) > VERBATIM_THRESHOLD)
        {
            var recentMessages = await GetRecentMessagesAsync(
                sessionId, 
                VERBATIM_THRESHOLD);
            
            var summary = await SummarizeAsync(recentMessages);
            await StoreSummaryAsync(sessionId, summary, VERBATIM_THRESHOLD);
        }
    }
}
```

<!--
NOTAS DEL ORADOR — código del servicio de memoria.
Aquí está el patrón. Usamos `IDbContextFactory`—nunca un DbContext singleton en código asincrónico. VERBATIM_THRESHOLD es nuestro disparador: mantener los últimos 10 mensajes tal cual, comprimir todo lo más antiguo. `GetSessionSummaryAsync` es una consulta simple para el resumen más reciente. `StoreSummaryAsync` persiste una nueva entidad SessionSummary. `GetStatsAsync` le da a la UI transparencia total: cuántos mensajes totales, cuántos resúmenes existen, cuántos mensajes están cubiertos por resúmenes, y cuándo resumimos por última vez. El método crítico es `SummarizeIfNeededAsync`: verifica si excedimos el umbral, obtiene los mensajes antiguos, llama al prompt de resumen, y almacena el resultado. Ese es el disparador que evita que el agente se ahogue en tokens.
-->

---

## Implementación: DefaultEmbeddingsService

```csharp
public class DefaultEmbeddingsService : IEmbeddingsService
{
    // Respaldado por Elbruno.LocalEmbeddings (modelo ONNX, p.ej., MiniLM-L6-v2)
    private readonly IEmbeddingProvider _embeddingProvider;

    public async Task<float[]> EmbedAsync(string text)
    {
        // Devuelve un vector de 384 dimensiones (MiniLM) o similar
        return await _embeddingProvider.EmbedAsync(text);
    }

    public float CosineSimilarity(float[] v1, float[] v2)
    {
        // similitud = (v1 · v2) / (|v1| * |v2|)
        float dotProduct = 0;
        for (int i = 0; i < v1.Length; i++) dotProduct += v1[i] * v2[i];

        float mag1 = (float)Math.Sqrt(v1.Sum(x => x * x));
        float mag2 = (float)Math.Sqrt(v2.Sum(x => x * x));

        return mag1 == 0 || mag2 == 0 ? 0 : dotProduct / (mag1 * mag2);
    }

    public async Task<List<(string Text, float Similarity)>> SearchAsync(
        string query, 
        IEnumerable<string> corpus,
        int topK = 3)
    {
        var queryEmbedding = await EmbedAsync(query);
        var results = new List<(string, float)>();

        foreach (var text in corpus)
        {
            var textEmbedding = await EmbedAsync(text);
            var similarity = CosineSimilarity(queryEmbedding, textEmbedding);
            results.Add((text, similarity));
        }

        return results
            .OrderByDescending(r => r.Similarity)
            .Take(topK)
            .ToList();
    }
}
```

**¿Por qué incrustaciones locales?**
- Sin llamadas API = sin picos de latencia
- Sin datos que salgan de tu máquina = privacidad
- Los modelos ONNX se ejecutan en CPU en milisegundos
- Escalable gratis: incrustar millones de mensajes localmente

<!--
NOTAS DEL ORADOR — código de incrustaciones.
Las incrustaciones son vectores numéricos que capturan significado. Usamos un modelo ONNX como MiniLM—pequeño (22MB), rápido (milisegundos por texto), y de código abierto. El método clave es `CosineSimilarity`: mide qué tan "cerca" están dos vectores (0 = no relacionados, 1 = idénticos). `SearchAsync` toma una consulta, la incrustra, la compara con un corpus, y devuelve los top-K textos más similares. Así encontramos conversaciones relevantes. Y todo se ejecuta localmente. Sin llamada API a Azure, sin latencia, sin costo por consulta. Puedes incrustar millones de mensajes por el precio de descargar una vez un pequeño modelo ONNX.
-->

---

## Integración: Entidad SessionSummary

```csharp
public sealed class SessionSummary
{
    // ID único para este resumen
    public Guid Id { get; set; } = Guid.NewGuid();

    // Clave foránea a ChatSession
    public Guid SessionId { get; set; }

    // El texto del resumen comprimido
    public string Summary { get; set; } = string.Empty;

    // Cuántos mensajes fueron cubiertos por este resumen
    public int CoveredMessageCount { get; set; }

    // Cuándo se creó el resumen
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // El vector de incrustación para búsqueda semántica
    public float[]? EmbeddingVector { get; set; }

    // Propiedad de navegación
    public ChatSession Session { get; set; } = null!;
}
```

**Esquema de base de datos:**
```sql
CREATE TABLE session_summaries (
    id UUID PRIMARY KEY,
    session_id UUID NOT NULL REFERENCES chat_sessions(id) ON DELETE CASCADE,
    summary TEXT NOT NULL,
    covered_message_count INT NOT NULL,
    created_at TIMESTAMP NOT NULL,
    embedding_vector BYTEA,
    UNIQUE(session_id, created_at)
);

CREATE INDEX idx_session_summaries_session_id 
    ON session_summaries(session_id, created_at DESC);
```

<!--
NOTAS DEL ORADOR — entidad y esquema.
Una sesión puede tener muchos resúmenes. A medida que crece la conversación, creamos nuevos resúmenes cada N mensajes. `CoveredMessageCount` nos dice cuántos mensajes este resumen comprimió—útil para estadísticas. `EmbeddingVector` se almacena como binario—la incrustación ONNX serializada a bytes. Eliminamos en cascada en la eliminación de sesión para que los resúmenes huérfanos no se acumulen. El índice en `(session_id, created_at DESC)` hace la recuperación rápida—podemos obtener rápidamente el resumen más reciente o un rango de resúmenes por fecha.
-->

---

## Puntos Finales API: Recuperación de Memoria

```csharp
public static class MemoryEndpoints
{
    public static void MapMemoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/memory").WithTags("Memory");

        // Obtener el resumen más reciente
        group.MapGet("/{sessionId:guid}/summary", 
            async (Guid sessionId, IMemoryService memory) =>
                Results.Ok(new { 
                    sessionId, 
                    summary = await memory.GetSessionSummaryAsync(sessionId) 
                }));

        // Obtener todos los resúmenes de una sesión
        group.MapGet("/{sessionId:guid}/summaries", 
            async (Guid sessionId, IMemoryService memory) =>
                Results.Ok(await memory.GetAllSummariesAsync(sessionId)));

        // Obtener estadísticas de memoria (transparencia para UI)
        group.MapGet("/{sessionId:guid}/stats", 
            async (Guid sessionId, IMemoryService memory) =>
                Results.Ok(await memory.GetStatsAsync(sessionId)));

        // Búsqueda semántica en resúmenes
        group.MapPost("/{sessionId:guid}/search", 
            async (Guid sessionId, [FromBody] SearchRequest req, 
                   IMemoryService memory) =>
                Results.Ok(await memory.SearchSummariesAsync(
                    sessionId, 
                    req.Query, 
                    req.TopK ?? 3)));
    }
}

public record SearchRequest(string Query, int? TopK);
```

**Ejemplos de llamadas:**
```bash
# Obtener estadísticas (muestra cuenta de mensajes, resúmenes, cobertura)
curl http://localhost:5000/api/memory/{sessionId}/stats

# Buscar por significado (no por palabra clave)
curl -X POST http://localhost:5000/api/memory/{sessionId}/search \
  -H "Content-Type: application/json" \
  -d '{"query": "patrones de inyección de dependencias", "topK": 3}'
```

<!--
NOTAS DEL ORADOR — puntos finales.
Estos puntos finales son la superficie de API. `GET /summary` obtiene el resumen comprimido más reciente. `GET /summaries` obtiene todos para una sesión. `GET /stats` es la capa de transparencia—le dice a la UI exactamente cuántos mensajes existen, cuántos resúmenes, cuántos mensajes están comprimidos, y cuándo resumimos por última vez. `POST /search` es la recompensa: búsqueda semántica. Envías una pregunta, y devuelve los top-3 resúmenes más relevantes del historial de conversación completo. Así es cómo el agente encuentra contexto sin enviar 100KB de mensajes cada solicitud.
-->

---

## Vista Previa de Demostración: Extracción de Memoria en Acción

### 1. Línea Base: Sin Sistema de Memoria
```bash
# Enviar 50 mensajes en rápida sucesión
# Modelo recibe los 50 mensajes en contexto
# Costo: ~50 tokens solo por contexto
# A medida que crece la conversación, el costo de tokens explota
```

### 2. Con Sistema de Memoria Activo
```bash
# Enviar los mismos 50 mensajes
# Después del mensaje 10 (umbral), sistema dispara resumen
# Mensajes 1–9 → comprimidos a 1–2 oraciones
# Mensajes 10–50 → enviados literalmente (contexto reciente)
# Modelo recibe: resumen + mensajes recientes
# Costo: ~5 tokens para contexto (¡reducción del 90%!)
```

### 3. Demostración de Búsqueda Semántica
```bash
# Usuario: "Hablamos sobre caché la semana pasada. ¿Qué decidimos?"
# Sistema busca en TODOS los resúmenes pasados por significado
# Devuelve: Resumen de hace 3 días sobre caché de Redis
# Agente: "Decidiste que Redis era mejor que en memoria para este caso..."
# Resultado: ¡El agente recuerda un hecho de una conversación completamente diferente!
```

### 4. Interfaz de Usuario de Estadísticas de Memoria
- **Mensajes Totales:** 147
- **Resúmenes:** 4
- **Mensajes Cubiertos:** 140
- **Último Resumen:** hace 2 min
- **Proporción de Compresión:** 28:1

<!--
NOTAS DEL ORADOR — demostración.
Cuatro demostraciones. Primero, mostramos el problema de costo: 50 mensajes = muchos tokens. Segundo, mostramos la solución: los mismos 50 mensajes pero los primeros 40 se comprimen en un resumen, así el modelo ve una fracción de tokens. Tercero, mostramos búsqueda semántica: el agente encuentra información relevante de semanas atrás por significado, no por palabras clave. Cuarto, mostramos la UI—estadísticas de memoria completamente transparentes para que el usuario sepa qué está pasando. Esa es la recompensa: gestión de contexto que es rápida, barata, e invisible para el usuario.
-->

---

## 🤖 Momento Copilot: Agregar Filtrado por Rango de Fechas

**Objetivo:** Extender `MemoryEndpoints` con filtrado por rango de fechas.

```csharp
// Agregar a MemoryEndpoints:
group.MapGet("/{sessionId:guid}/summaries/range", 
    async (Guid sessionId, DateTime from, DateTime to, IMemoryService memory) =>
        Results.Ok(await memory.GetSummariesByDateAsync(sessionId, from, to)));

// Implementar en DefaultMemoryService:
public async Task<IEnumerable<SessionSummary>> GetSummariesByDateAsync(
    Guid sessionId, DateTime from, DateTime to)
{
    using var db = _dbContextFactory.CreateDbContext();
    return await db.SessionSummaries
        .Where(s => s.SessionId == sessionId 
            && s.CreatedAt >= from 
            && s.CreatedAt <= to)
        .OrderByDescending(s => s.CreatedAt)
        .ToListAsync();
}
```

**Pruébalo:**
```bash
curl "http://localhost:5000/api/memory/{sessionId}/summaries/range?from=2025-01-01&to=2025-01-31"
```

<!--
NOTAS DEL ORADOR — momento copilot.
Tu turno. Agrega un filtro de rango de fechas a los puntos finales de memoria. Esta es una característica real que los usuarios quieren: "Muéstrame todo lo que resumimos sobre este tema el mes pasado." La implementación es directa: agrega un punto final que acepte parámetros DateTime `from` y `to`, y filtra la consulta de resúmenes. Pruébalo con curl. Al final de este momento, habrás extendido la API tú mismo—es un pequeño cambio pero es tuyo.
-->

---

## Perspectivas Clave

> **"La memoria no es magia. Es compresión + recuperación."**

1. **Compresión vía LLM** — Deja que el modelo resuma. Es lo suyo.
2. **Búsqueda local** — No envíes todo a Azure. Usa incrustaciones locales.
3. **Estadísticas transparentes** — Los usuarios deben ver estadísticas de memoria, no una caja negra.
4. **Falla con gracia** — Si el resumen toma demasiado tiempo, omítelo. El agente aún funciona.
5. **Privacidad por defecto** — Todas las incrustaciones se calculan localmente. Sin datos a terceros.

<!--
NOTAS DEL ORADOR — perspectivas clave.
Cinco principios. Primero, la memoria es compresión y recuperación—no magia. Segundo, usa el LLM para lo que es bueno: resumen. Tercero, mantén la búsqueda local y rápida. Cuarto, muestra estadísticas para que los usuarios confíen en el sistema. Quinto, todo queda en tu máquina por defecto. Por eso importan las incrustaciones locales—puedes escalar memoria sin escalar tu factura de Azure.
-->

---

## Lo Que Construimos Hoy

✓ **Resumen de conversación** — Mantener mensajes recientes, comprimir antiguos  
✓ **Almacenamiento persistente** — Entidad SessionSummary + esquema de base de datos  
✓ **Incrustaciones locales** — Modelos ONNX para búsqueda semántica  
✓ **Servicio de memoria** — Lógica central para extracción y recuperación  
✓ **Puntos finales API** — Listar resúmenes, buscar, estadísticas, rangos de fechas  
✓ **Momento Copilot** — Agregaste filtrado por rango de fechas tú mismo  
✓ **Transparencia UI** — Estadísticas de memoria visibles para usuarios  

**Construye sobre:** Sesión 2 (fundación + herramientas)  
**Habilita:** Sesión 4 (despliegue en la nube + producción)

<!--
NOTAS DEL ORADOR — lista de verificación.
Siete cosas lanzadas hoy. Un sistema de memoria que mantiene conversaciones baratas y rápidas. Resúmenes persistentes que sobreviven reinicios. Búsqueda semántica local. Una API que hace la memoria consultable. Lo extendiste tú mismo. Y transparencia total en la UI. Esta es la fundación para personalización de agentes: el agente aprende tus preferencias y conocimiento de dominio sobre semanas y meses, pero no explota tu presupuesto de tokens.
-->

---

## Preguntas y Pasos Siguientes

### Repositorio de Hoy
- **Código:** `github.com/elbruno/openclawnet` — etiqueta Session 3
- **Diapositivas:** `docs/sessions/session-3/`
- **Demostraciones:** `docs/sessions/session-3/demos-resources/`

### Siguiente: Sesión 4 — Despliegue en la Nube y Producción
- Azure Foundry Agent Host
- Tuberías de CI/CD con GitHub Actions
- Configuración y monitoreo en producción
- De localhost a producción en una sesión

### Recursos
- [OpenClawNet GitHub](https://github.com/elbruno/openclawnet)
- [Microsoft Learn: RAG + Incrustaciones](https://learn.microsoft.com/es-es/azure/search/)
- [ONNX Runtime](https://onnxruntime.ai/)

**Construyamos agentes que recuerden. Juntos.**

<!--
NOTAS DEL ORADOR — cierre.
Esa es la Sesión 3. Llevamos al agente de sin memoria a memoria a largo plazo. La Sesión 4 lo llevaremos a producción. El código está en GitHub, grabado, y listo para aprender. ¿Preguntas? Envía un correo a bruno@microsoft.com o abre un problema. Gracias por acompañarnos.
-->

---

<!-- _class: lead -->

# Gracias

**Siguiente sesión:** Despliegue en la Nube y Producción

**Seguimiento:** oficina en Discord

**Código:** [github.com/elbruno/openclawnet](https://github.com/elbruno/openclawnet)

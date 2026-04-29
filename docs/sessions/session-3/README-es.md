# 🎭 Sesión 3: Skills + Memoria

![Duration](https://img.shields.io/badge/Duración-50%20min-blue)
![Level](https://img.shields.io/badge/Nivel-.NET%20Intermedio-purple)
![Session](https://img.shields.io/badge/Sesión-3%20de%204-green)

## Descripción General

El mismo agente, usuarios diferentes — y quieres que vean comportamiento diferente. Esta sesión añade **skills** (archivos Markdown con frontmatter YAML que moldean el comportamiento del agente sin cambios de código) y **memoria** (summarización automática para gestionar ventanas de contexto, más búsqueda semántica en conversaciones pasadas). Al final, el agente es personalizado, eficiente con contexto, y recuerda qué pasó la última vez.

> **"Los skills son solo markdown. La memoria es transparente."**

## Requisitos Previos

- ✅ Sesiones 1 & 2 completadas y funcionando
- ✅ LLM local ejecutándose (Ollama con `llama3.2` o Foundry Local)
- ✅ Comprensión de: async/await, E/S de archivos, conceptos básicos de EF Core
- ✅ Archivos de skills de muestra en `skills/built-in/` y `skills/samples/`

## Qué Aprenderás

### 🎭 Etapa 1: Sistema de Skills (12 min)
- Qué es un skill — Markdown + frontmatter YAML, sin código requerido
- `FileSkillLoader` — escanear directorios, parsear archivos, seguir estado habilitado/deshabilitado
- `SkillParser` — extracción de YAML basada en regex
- Cómo los skills se tejen en el prompt del sistema a través de `DefaultPromptComposer`

### 🧠 Etapa 2: Memoria y Summarización (15 min)
- El problema de la ventana de contexto — límites de tokens, costo, truncamiento
- Estrategia de summarización — mantener reciente verbatim, comprimir antiguo, buscar muy antiguo
- `DefaultMemoryService` — persistencia con EF Core usando `IDbContextFactory`
- `DefaultEmbeddingsService` — embeddings ONNX locales con similitud coseno

### ⚡ Etapa 3: Integración + UI (15 min)
- API de Skills — listar, recargar, habilitar/deshabilitar en tiempo de ejecución (sin reinicio)
- Estadísticas de Memoria — panel transparente con mensajes totales, cantidad de resúmenes
- Patrón antes/después — cambiar skill → observar cambio de comportamiento
- Enrutamiento de endpoint Gateway con Minimal API

## Proyectos Cubiertos

| Proyecto | LOC | Responsabilidad Clave |
|----------|-----|----------------------|
| OpenClawNet.Skills | 237 | Definiciones de skills, FileSkillLoader, SkillParser |
| OpenClawNet.Memory | 234 | DefaultMemoryService, DefaultEmbeddingsService, MemoryStats |
| OpenClawNet.Agent | — | DefaultPromptComposer (integración de skill + summary) |
| OpenClawNet.Storage | — | Entidad SessionSummary |
| OpenClawNet.Gateway | — | SkillEndpoints, MemoryEndpoints |

## Materiales de la Sesión

| Recurso | Enlace |
|---------|--------|
| 📖 Guía del Presentador | [session-3-guide.md](../session-3-guide.md) |
| 📖 Guía (Español) | [session-3-guide-es.md](../session-3-guide-es.md) |
| 🎤 Script del Presentador | [speaker-script.md](./speaker-script.md) |
| 🤖 Prompts de Copilot | [copilot-prompts.md](./copilot-prompts.md) |
| 🖥️ Diapositivas | _TBD_ |

## Checkpoints de Git

- **Tag de inicio:** `session-3-start` (alias: `session-2-complete`)
- **Tag final:** `session-3-complete`

## Referencia de Registro DI

```csharp
// Skills
services.AddSingleton<ISkillLoader>(sp =>
    new FileSkillLoader(skillDirectories, sp.GetRequiredService<ILogger<FileSkillLoader>>()));

// Memory
services.AddScoped<IMemoryService, DefaultMemoryService>();
services.AddSingleton<IEmbeddingsService, DefaultEmbeddingsService>();
```

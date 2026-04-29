# 🎭 Session 3: Skills + Memory

![Duration](https://img.shields.io/badge/Duration-50%20min-blue)
![Level](https://img.shields.io/badge/Level-Intermediate%20.NET-purple)
![Session](https://img.shields.io/badge/Session-3%20of%204-green)

## Overview

Same agent, different users — and you want them to see different behavior. This session adds **skills** (Markdown files with YAML frontmatter that shape agent behavior without code changes) and **memory** (automatic summarization to manage context windows, plus semantic search across past conversations). By the end, the agent is personalized, context-efficient, and remembers what happened last time.

> **"Skills are just markdown. Memory is transparent."**

## Prerequisites

- ✅ Sessions 1 & 2 complete and working
- ✅ Local LLM running (Ollama with `llama3.2` or Foundry Local)
- ✅ Understanding of: async/await, file I/O, EF Core basics
- ✅ Sample skill files in `skills/built-in/` and `skills/samples/`

## What You'll Learn

### 🎭 Stage 1: Skill System (12 min)
- What a skill is — Markdown + YAML frontmatter, no code required
- `FileSkillLoader` — scan directories, parse files, track enabled/disabled state
- `SkillParser` — regex-based YAML extraction
- How skills weave into the system prompt via `DefaultPromptComposer`

### 🧠 Stage 2: Memory & Summarization (15 min)
- The context window problem — token limits, cost, truncation
- Summarization strategy — keep recent verbatim, compress older, search very old
- `DefaultMemoryService` — EF Core persistence with `IDbContextFactory`
- `DefaultEmbeddingsService` — local ONNX embeddings with cosine similarity

### ⚡ Stage 3: Integration + UI (15 min)
- Skills API — list, reload, enable/disable at runtime (no restart)
- Memory stats — transparent dashboard with total messages, summary count
- Before/after pattern — toggle skill → observe behavior change
- Gateway endpoint wiring with Minimal API

## Projects Covered

| Project | LOC | Key Responsibility |
|---------|-----|-------------------|
| OpenClawNet.Skills | 237 | Skill definitions, FileSkillLoader, SkillParser |
| OpenClawNet.Memory | 234 | DefaultMemoryService, DefaultEmbeddingsService, MemoryStats |
| OpenClawNet.Agent | — | DefaultPromptComposer (skill + summary integration) |
| OpenClawNet.Storage | — | SessionSummary entity |
| OpenClawNet.Gateway | — | SkillEndpoints, MemoryEndpoints |

## Session Materials

| Resource | Link |
|----------|------|
| 📖 Presenter Guide | [session-3-guide.md](../session-3-guide.md) |
| 📖 Guía (Español) | [session-3-guide-es.md](../session-3-guide-es.md) |
| 🎤 Speaker Script | [speaker-script.md](./speaker-script.md) |
| 🤖 Copilot Prompts | [copilot-prompts.md](./copilot-prompts.md) |
| 🖥️ Slides | _TBD_ |

## Git Checkpoints

- **Starting tag:** `session-3-start` (alias: `session-2-complete`)
- **Ending tag:** `session-3-complete`

## DI Registration Reference

```csharp
// Skills
services.AddSingleton<ISkillLoader>(sp =>
    new FileSkillLoader(skillDirectories, sp.GetRequiredService<ILogger<FileSkillLoader>>()));

// Memory
services.AddScoped<IMemoryService, DefaultMemoryService>();
services.AddSingleton<IEmbeddingsService, DefaultEmbeddingsService>();
```

# Session 3 — Code Demos

Standalone C# console apps that illustrate the three pillars of Session 3:
**Skills**, **Storage**, and **Memory**. Each demo is a single-project
`dotnet run` console app, intentionally small (~100–200 lines) so it can be
read end-to-end in a few minutes.

## Main demos (used in the live talk)

| # | Pillar  | Folder                                     | What it shows                                                                                                  |
|---|---------|--------------------------------------------|----------------------------------------------------------------------------------------------------------------|
| 1 | Skills  | [`01-SkillOnOff/`](./01-SkillOnOff/)             | Same prompt sent to Ollama twice — once raw, once with a skill prepended. Side-by-side output proves the lift. |
| 2 | Storage | [`02-AgentProfileSwitcher/`](./02-AgentProfileSwitcher/) | SQLite-backed REPL with two seeded agent profiles; `:use <name>` switches the active persona at runtime.    |
| 3 | Memory  | [`03-MemoryStub/`](./03-MemoryStub/)             | Chat loop that persists every turn to SQLite and recalls relevant history (last N + LIKE-matched older turns).  |

## Bonus demos (extra material — not shown live)

| # | Pillar  | Folder                                       | What it shows                                                                          |
|---|---------|----------------------------------------------|----------------------------------------------------------------------------------------|
| 4 | Skills  | [`04-SkillPicker/`](./04-SkillPicker/)             | Scans a folder of `*.skill.md`, parses the frontmatter, and picks the best match for a query — no LLM. |
| 5 | Storage | [`05-ProviderCatalogCli/`](./05-ProviderCatalogCli/) | Mini CRUD over the `ModelProviderDefinition` schema in SQLite (`list / add / test / delete`).      |

## Prerequisites

- .NET 10 SDK
- For `SkillOnOff`, `AgentProfileSwitcher`, `MemoryStub`: a running local
  [Ollama](https://ollama.com/) with a small model pulled, e.g.
  `ollama pull llama3.2`
- The Storage demos use SQLite via `Microsoft.Data.Sqlite` — no external service

## Running a demo

```pwsh
cd <demo-folder>
dotnet run
```

Each folder has its own `README.md` with the full instructions, sample
output, and notes on what to watch for during the talk.

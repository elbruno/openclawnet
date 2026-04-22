# Demo 3 — Agent Personality Switching (Plan B)

An enhanced console app that demonstrates **agent personalities** by creating multiple `AgentProfile` instances with different instructions, then asking each one the same question.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Ollama](https://ollama.com/) running locally with a model pulled (e.g., `ollama pull gemma4:e2b`)

## Run

```bash
cd OpenClawNet.Demo3.Agents
dotnet run
```

## What It Shows

1. **AgentProfile** — named configurations with different personalities via `Instructions`
2. **Same provider, different agents** — one `OllamaAgentProvider`, three personalities
3. **Streaming responses** — each agent streams its answer token by token
4. **Personality-driven behavior** — same question, wildly different answers

## Agents

| Agent | Persona | Style |
|-------|---------|-------|
| 🏴‍☠️ Captain Claw | Pirate .NET developer | Nautical metaphors, "Arr!", technically accurate |
| 👨‍🍳 Chef Byte | Cooking-themed coder | Recipe metaphors, ingredients = dependencies |
| 🤖 RoboChat | Formal robot assistant | Precise, structured, efficiency-focused |

## Primary Demo (if using the running app)

The main Demo 3 uses the full running OpenClawNet app with workspace files:
- Swap `src/OpenClawNet.Agent/workspace/AGENTS.md` with personas from `sessions/session-1/demo-agents/`
- The running app picks up the new personality immediately

This Plan B console app serves as a self-contained fallback if the full app isn't available.

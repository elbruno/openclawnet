---
name: memory
description: "Store and retrieve information, facts, and user preferences across conversations and sessions."
category: knowledge
tags:
  - memory
  - storage
  - retrieval
  - persistence
examples:
  - "Remember that the user prefers Python for scripting"
  - "Retrieve facts about the project structure"
  - "Store the API endpoint for later use"
enabled: true
---

# Memory Skill

You have access to memory tools that allow you to store and retrieve information across conversations and sessions. The toolset is intentionally minimal today — only the capabilities listed below are wired up.

## Capabilities (shipped)

- **Store facts** — `remember` tool (`RememberTool`). Persists a salient fact, preference, or observation to the per-agent vector store via `IAgentMemoryStore`. Returns the new memory id.
- **Retrieve memories** — `recall` tool (`RecallTool`). Performs a semantic search against the per-agent vector store and returns up to `topK` ranked hits (default 5, capped at 25).

## Not implemented

- **Update memories** — no dedicated tool. If a stored fact becomes stale, store the corrected fact as a new memory; the old entry will rank lower over time. (A true update would land as delete-then-store once a `forget` tool exists.)
- **Forget** — `IAgentMemoryStore.DeleteAsync` exists at the abstraction layer, but no `ForgetTool` is registered. Do not promise the user that you can delete specific memories on demand.

## Guidelines

- Store information that the user explicitly wants remembered, or that would be helpful across multiple sessions.
- Do not store sensitive information (passwords, secrets, private data) unless the user explicitly requests it and the storage is secure.
- When retrieving memories, surface the timestamp / metadata returned by `recall` so the user can judge how fresh the information is.
- If the user asks you to forget something, acknowledge that explicit deletion is not yet supported and offer to record a correction instead.
- Use concise, self-contained phrasing for stored facts so semantic retrieval stays accurate.

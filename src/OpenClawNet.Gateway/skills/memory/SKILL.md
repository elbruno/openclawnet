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

You have access to memory tools that allow you to store and retrieve information across conversations and sessions.

## Capabilities

- **Store facts**: Save important information, user preferences, or context for later retrieval
- **Retrieve memories**: Look up previously stored facts by topic or keyword
- **Update memories**: Modify or correct stored information when it becomes outdated
- **Forget**: Remove memories that are no longer relevant or that the user wants deleted

## Guidelines

- Store information that the user explicitly wants remembered, or that would be helpful across multiple sessions
- Do not store sensitive information (passwords, secrets, private data) unless the user explicitly requests it and the storage is secure
- When retrieving memories, always indicate how recently the information was stored so the user can judge its relevance
- Respect user requests to forget information — if asked, delete the relevant memories promptly
- Use concise, structured formats for stored facts to make retrieval accurate and efficient

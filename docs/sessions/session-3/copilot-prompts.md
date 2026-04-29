# 🤖 Session 3: Copilot Prompts

Two live Copilot moments: creating a skill file from scratch and extending the memory API.

---

## Prompt 1: Create a Security Auditor Skill File

### When
**Stage 1** (~minute 10) — after walking through the skill system and SkillParser

### Context
- **File open:** `skills/samples/dotnet-expert.md` (as a reference for the format)
- **What just happened:** We explained YAML frontmatter, SkillDefinition, FileSkillLoader, and how skills get injected into the system prompt

### Mode
**Copilot Chat** (sidebar)

### Exact Prompt

```
Create a new skill file called security-auditor.md for the OpenClawNet skill system. It should follow the same YAML frontmatter format as dotnet-expert.md. The skill should instruct the agent to act as a security auditor — reviewing code for vulnerabilities, suggesting OWASP best practices, checking for common .NET security issues like SQL injection, XSS, path traversal, and insecure deserialization. Include tags for security, audit, owasp, and dotnet. Set enabled to true.
```

### Expected Result

A complete Markdown file with valid YAML frontmatter:

```markdown
---
name: security-auditor
description: Security auditing expertise for .NET applications
tags: [security, audit, owasp, dotnet]
enabled: true
---

You are a security auditor assistant. When reviewing code or answering questions:

- Identify common vulnerabilities (OWASP Top 10)
- Check for .NET-specific security issues:
  - SQL injection via string concatenation
  - XSS in Razor views and API responses
  - Path traversal in file operations
  - Insecure deserialization
- Recommend secure coding patterns and mitigations
- Reference OWASP guidelines and Microsoft security documentation
- Prioritize findings by severity (Critical, High, Medium, Low)
```

### Why It's Interesting

- **No C# required** — Copilot generates a complete skill using just Markdown
- **Format awareness** — it follows the YAML frontmatter convention from the existing skill
- **Immediately testable** — save the file, reload skills, and ask a security question
- **Demonstrates skill extensibility** — anyone (even non-developers) can create skills for the agent

### How to Test

```bash
# Save the file to skills/samples/security-auditor.md
# Then reload and verify:
curl -X POST http://localhost:5000/api/skills/reload
curl http://localhost:5000/api/skills | jq '.[] | select(.name == "security-auditor")'
```

---

## Prompt 2: Add Search-by-Date Filter to MemoryEndpoints

### When
**Stage 3** (~minute 40) — after walking through MemoryEndpoints and the memory stats panel

### Context
- **File open:** `src/OpenClawNet.Gateway/Endpoints/MemoryEndpoints.cs`
- **Cursor position:** Below the existing `GET /{sessionId:guid}/summaries` endpoint
- **What just happened:** We showed the memory stats panel and the three existing read-only endpoints

### Mode
**Copilot Chat** (sidebar recommended — multi-file generation)

### Exact Prompt

```
Add optional `from` and `to` query parameters to the summaries endpoint in MemoryEndpoints to filter summaries by CreatedAt date range. Use nullable DateTime parameters. If neither is provided, return all summaries (existing behavior). If `from` is provided, filter summaries where CreatedAt >= from. If `to` is provided, filter where CreatedAt <= to. Follow the existing minimal API pattern.
```

### Expected Result

Copilot modifies the existing summaries endpoint (or adds a new one) with date filtering:

```csharp
group.MapGet("/{sessionId:guid}/summaries",
    async (Guid sessionId, IMemoryService memoryService,
           DateTime? from, DateTime? to) =>
    {
        var summaries = await memoryService.GetAllSummariesAsync(sessionId);

        if (from.HasValue)
            summaries = summaries.Where(s => s.CreatedAt >= from.Value).ToList();
        if (to.HasValue)
            summaries = summaries.Where(s => s.CreatedAt <= to.Value).ToList();

        return Results.Ok(summaries);
    });
```

### Why It's Interesting

- **Matches existing patterns** — Copilot reads the surrounding Minimal API code and generates consistent endpoints
- **Real feature request** — date filtering is a common, practical extension
- **Query parameter binding** — shows ASP.NET Core's automatic query string binding with nullable types
- **Incremental extension** — builds on existing code without breaking backward compatibility (no params = all results)

### How to Test

```bash
# Get summaries within a date range
curl "http://localhost:5000/api/memory/{sessionId}/summaries?from=2025-01-01T00:00:00Z&to=2025-12-31T23:59:59Z"

# Get summaries from a specific date onward
curl "http://localhost:5000/api/memory/{sessionId}/summaries?from=2025-07-01T00:00:00Z"

# No params — existing behavior unchanged
curl "http://localhost:5000/api/memory/{sessionId}/summaries"
```

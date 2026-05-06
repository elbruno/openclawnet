# Storage & Database Architecture

## Overview

OpenClaw .NET uses **SQLite** with **Entity Framework Core** for persistent storage. The database is created and migrated at startup via `EnsureCreatedAsync()` and a custom `SchemaMigrator`.

**Key principle:** The `OpenClawDbContext` is the single source of truth. All data models go through EF Core. No raw SQL.

---

## Database Context

### `OpenClawDbContext`

Defined in `src/OpenClawNet.Storage/OpenClawDbContext.cs`:

```csharp
public class OpenClawDbContext : DbContext
{
    public DbSet<ChatSession> Sessions { get; }
    public DbSet<ChatMessageEntity> Messages { get; }
    public DbSet<SessionSummary> Summaries { get; }
    public DbSet<ToolCallRecord> ToolCalls { get; }
    public DbSet<ScheduledJob> Jobs { get; }
    public DbSet<JobRun> JobRuns { get; }
    public DbSet<ProviderSetting> ProviderSettings { get; }
    public DbSet<AgentProfileEntity> AgentProfiles { get; }
    public DbSet<ModelProviderDefinition> ModelProviders { get; }
}
```

---

## Core Entities

### `ChatSession`

Represents a single conversation session (user ↔ agent):

```csharp
public class ChatSession
{
    public string Id { get; set; }                          // Unique session ID
    public DateTime CreatedAt { get; set; }
    public DateTime? LastAccessAt { get; set; }
    public string? WorkspacePath { get; set; }              // Workspace for bootstrap files
    public string? AgentProfileName { get; set; }           // Named agent profile (optional)
    public string? Channel { get; set; }                    // Source channel (e.g., "teams", "webchat")
    public string? ChannelUserId { get; set; }              // User ID in channel
    public List<ChatMessageEntity> Messages { get; set; }   // Foreign key: conversation
    public List<SessionSummary> Summaries { get; set; }     // Foreign key: context compaction
    public bool IsIsolated { get; set; }                    // Isolated session flag (for jobs/webhooks)
}
```

**Foreign Keys:**
- `Messages` (one-to-many): All messages in this session
- `Summaries` (one-to-many): All context compaction summaries for this session

**Cascade behavior:** Deleting a session deletes all its messages and summaries.

---

### `ChatMessageEntity`

Represents a single message (user or assistant) in a conversation:

```csharp
public class ChatMessageEntity
{
    public string Id { get; set; }
    public string SessionId { get; set; }                   // Foreign key: ChatSession
    public ChatSession Session { get; set; }                // Navigation property
    public string Role { get; set; }                        // "user", "assistant", "system"
    public string Content { get; set; }                     // Message text or tool result
    public int OrderIndex { get; set; }                     // Position in conversation (0-indexed)
    public DateTime CreatedAt { get; set; }
    public string? ToolCallJson { get; set; }               // Serialized tool calls (if any)
    public string? Metadata { get; set; }                   // Additional JSON metadata
}
```

**Indexes:**
- Composite index on `(SessionId, OrderIndex)` for efficient history retrieval

**Cascade behavior:** Deleting a session cascades to all its messages.

---

### `SessionSummary`

Stores compressed conversation summaries for context compaction:

```csharp
public class SessionSummary
{
    public string Id { get; set; }
    public string SessionId { get; set; }                   // Foreign key: ChatSession
    public ChatSession Session { get; set; }                // Navigation property
    public int SummaryIndex { get; set; }                   // Order of summaries (0, 1, 2...)
    public int FirstMessageIndex { get; set; }              // Which messages were summarized (start)
    public int LastMessageIndex { get; set; }               // Which messages were summarized (end)
    public string SummaryText { get; set; }                 // Compressed summary
    public DateTime CreatedAt { get; set; }
}
```

**Purpose:** When a session has >20 messages, the oldest 10 are summarized. The summary is stored here and included in future prompt composition.

**Example:**
- Messages 0–9 summarized → SessionSummary { SummaryIndex: 0, FirstMessageIndex: 0, LastMessageIndex: 9, SummaryText: "User asked about..." }
- Messages 10–19 summarized → SessionSummary { SummaryIndex: 1, FirstMessageIndex: 10, LastMessageIndex: 19, SummaryText: "User discussed..." }
- Messages 20+ kept at full fidelity

---

### `ToolCallRecord`

Audit trail of tool executions:

```csharp
public class ToolCallRecord
{
    public string Id { get; set; }
    public string SessionId { get; set; }                   // Foreign key: ChatSession
    public string ToolName { get; set; }                    // e.g., "FileSystem", "Shell"
    public string Input { get; set; }                       // Serialized tool input JSON
    public string? Output { get; set; }                     // Serialized tool output JSON
    public DateTime ExecutedAt { get; set; }
    public bool Success { get; set; }                       // true if tool succeeded
    public string? ErrorMessage { get; set; }               // Error details if Success=false
}
```

**Index:** On `SessionId` for session-scoped tool history queries.

---

## Job Scheduling Entities

### `ScheduledJob`

A recurring or one-time automation job:

```csharp
public class ScheduledJob
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public JobStatus Status { get; set; }                   // Draft, Active, Paused, Completed, Failed
    public string CronExpression { get; set; }              // e.g., "0 9 * * MON-FRI" (9 AM weekdays)
    public DateTime? NextRunAt { get; set; }                // When job will next run
    public DateTime? LastRunAt { get; set; }
    public string? Prompt { get; set; }                     // The task for the agent
    public string? WorkspacePath { get; set; }              // Workspace for isolated session
    public string? AgentProfileName { get; set; }           // Named agent profile to use
    public string? ChannelId { get; set; }                  // Send result to this channel (optional)
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<JobRun> Runs { get; set; }                  // Foreign key: execution history
}
```

**JobStatus enum:**
- `Draft` — Not yet scheduled
- `Active` — Running on schedule
- `Paused` — Temporarily disabled
- `Completed` — One-time job finished
- `Failed` — Last run failed; manual intervention needed

**Cascade behavior:** Deleting a job cascades to all its runs.

---

### `JobRun`

A single execution of a scheduled job:

```csharp
public class JobRun
{
    public string Id { get; set; }
    public string JobId { get; set; }                       // Foreign key: ScheduledJob
    public ScheduledJob Job { get; set; }                   // Navigation property
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; }                      // "running", "completed", "failed"
    public string? Output { get; set; }                     // Agent response or error message
    public string? SessionId { get; set; }                  // Isolated session created for this run
    public string? ErrorLog { get; set; }                   // Full error details if failed
}
```

**Example timeline:**
1. Job scheduled for 9 AM Monday
2. JobSchedulerService wakes up and sees NextRunAt = now
3. Creates JobRun { Status: "running", StartedAt: now }
4. Spawns isolated session, runs agent with job prompt
5. Updates JobRun { Status: "completed", Output: "...", CompletedAt: now }
6. Updates ScheduledJob { NextRunAt: next Monday 9 AM, LastRunAt: now }

---

## Configuration & Settings Entities

### `ProviderSetting`

Key-value store for provider runtime configuration:

```csharp
public class ProviderSetting
{
    public string Key { get; set; }                         // Primary key (e.g., "current_provider")
    public string Value { get; set; }                       // JSON value
}
```

**Examples:**
- `current_provider` → `"ollama"`
- `active_model` → `"gemma4:e2b"`
- `fallback_chain` → `["ollama", "foundrylocal", "azure-openai"]`

---

### `ModelProviderDefinition`

Named provider configuration (stored in DB, not just DI):

```csharp
public class ModelProviderDefinition
{
    public string Name { get; set; }                        // Primary key; e.g., "ollama-local", "azure-gpt4o"
    public string ProviderType { get; set; }                // "ollama", "azure-openai", "foundry", "foundrylocal", "github-copilot"
    public string Endpoint { get; set; }                    // e.g., "http://localhost:11434"
    public string Model { get; set; }                       // e.g., "llama3.2", "gpt-4o"
    public string? ApiKey { get; set; }                     // Encrypted or secret-referenced
    public string? DeploymentName { get; set; }             // Azure-specific
    public string? AuthMode { get; set; }                   // "api-key", "integrated" (Azure)
    public bool IsDefault { get; set; }                     // Use this as default if no profile specified
}
```

**Storage:** All instances of all providers can be configured here. Multiple Ollama endpoints, multiple Azure deployments, etc.

**Example configurations:**

```
Name: "ollama-local"
  ProviderType: "ollama"
  Endpoint: "http://localhost:11434"
  Model: "gemma4:e2b"

Name: "ollama-remote"
  ProviderType: "ollama"
  Endpoint: "http://gpu-server:11434"
  Model: "llama3.2"

Name: "azure-gpt4o"
  ProviderType: "azure-openai"
  Endpoint: "https://resource.openai.azure.com"
  DeploymentName: "gpt-4o"
  Model: "gpt-4o"
  ApiKey: "sk-..."
  AuthMode: "api-key"

Name: "foundry-claude"
  ProviderType: "foundry"
  Endpoint: "https://foundry.azure.ai"
  Model: "claude-3-sonnet"
  ApiKey: "..."
```

---

### `AgentProfileEntity`

Named agent definition (combines provider + instructions + tool filter):

```csharp
public class AgentProfileEntity
{
    public string Name { get; set; }                        // Primary key; e.g., "code-assistant", "summarizer"
    public string? ModelProviderDefinitionName { get; set; } // Reference to ModelProviderDefinition
    public string? Instructions { get; set; }               // Additional system prompt
    public string? ToolFilter { get; set; }                 // Comma-separated allowed tool names
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
```

**Example:**

```
Name: "code-assistant"
  ModelProviderDefinitionName: "ollama-local"
  Instructions: "You are a .NET expert. Always provide runnable code examples."
  ToolFilter: "FileSystem,Web,Shell"

Name: "summarizer"
  ModelProviderDefinitionName: "foundry-claude"
  Instructions: "Summarize text concisely in bullet points."
  ToolFilter: "none"
```

---

## Entity Relationship Diagram (Conceptual)

```
ChatSession (PK: Id)
    ├── 1:N → ChatMessageEntity (FK: SessionId)
    │         └── OrderIndex (composite index with SessionId)
    │
    ├── 1:N → SessionSummary (FK: SessionId)
    │
    └── 1:N → ToolCallRecord (FK: SessionId)

ScheduledJob (PK: Id)
    └── 1:N → JobRun (FK: JobId)

ModelProviderDefinition (PK: Name)
    └── Referenced by AgentProfileEntity.ModelProviderDefinitionName

AgentProfileEntity (PK: Name)
    └── Optional reference to ModelProviderDefinition

ProviderSetting (PK: Key)
    └── Runtime configuration (current_provider, etc.)
```

---

## Initialization & Migration

### Startup Sequence

In `Gateway/Program.cs` (lines 150–165):

```csharp
// Ensure database is created and schema is up to date
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider
        .GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<OpenClawDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    
    // Step 1: Create tables (idempotent)
    await db.Database.EnsureCreatedAsync();
    
    // Step 2: Run custom migrations
    await OpenClawNet.Storage.SchemaMigrator.MigrateAsync(db);
    
    // Step 3: Seed defaults
    var providerStore = scope.ServiceProvider
        .GetRequiredService<IModelProviderDefinitionStore>();
    await providerStore.SeedDefaultsAsync();
    
    var profileStore = scope.ServiceProvider
        .GetRequiredService<IAgentProfileStore>();
    await profileStore.GetDefaultAsync();
}
```

### `SchemaMigrator` Pattern

Custom `SchemaMigrator` class handles schema changes that EF Core migrations wouldn't catch. Example:

```csharp
public static class SchemaMigrator
{
    public static async Task MigrateAsync(OpenClawDbContext db)
    {
        // Check schema version
        var schemaVersion = await GetSchemaVersionAsync(db);
        
        // V1 → V2: Add new column to ChatSession
        if (schemaVersion < 2)
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ChatSessions ADD COLUMN IsIsolated BOOLEAN DEFAULT 0");
            await SetSchemaVersionAsync(db, 2);
        }
        
        // V2 → V3: Add index
        if (schemaVersion < 3)
        {
            await db.Database.ExecuteSqlRawAsync(
                @"CREATE INDEX idx_session_created ON ChatSessions(CreatedAt)");
            await SetSchemaVersionAsync(db, 3);
        }
        
        await db.SaveChangesAsync();
    }
}
```

**Why separate from EF Core migrations?**
- Keeps schema in sync at startup (no manual migration commands)
- Supports both SQLite (file-based) and future cloud databases
- Graceful schema evolution without downtime

---

## Query Patterns

### Load Session with History

```csharp
var session = await db.Sessions
    .Include(s => s.Messages.OrderBy(m => m.OrderIndex))
    .FirstOrDefaultAsync(s => s.Id == sessionId);
```

### Get Recent Messages

```csharp
var recentMessages = await db.Messages
    .Where(m => m.SessionId == sessionId)
    .OrderByDescending(m => m.OrderIndex)
    .Take(20)
    .OrderBy(m => m.OrderIndex)  // Re-order for composition
    .ToListAsync();
```

### Find Summaries for a Session

```csharp
var summaries = await db.Summaries
    .Where(s => s.SessionId == sessionId)
    .OrderBy(s => s.SummaryIndex)
    .ToListAsync();
```

### Get Scheduled Jobs Due Now

```csharp
var dueJobs = await db.Jobs
    .Where(j => j.Status == JobStatus.Active && j.NextRunAt <= DateTime.UtcNow)
    .ToListAsync();
```

---

## Storage Considerations

### SQLite Advantages
- ✅ No separate server — file-based, local-first
- ✅ Zero configuration
- ✅ Full ACID compliance
- ✅ Suitable for medium-scale workloads (10K–100K sessions)

### Limitations & Workarounds
- ❌ No concurrent write transactions (one writer at a time)
  - **Mitigation:** Job scheduler polls every 30 seconds (not continuous)
- ❌ Limited to single machine (no distributed read replicas)
  - **Future:** Migrate to PostgreSQL or Azure SQL for cloud deployment

### Backup Strategy

```powershell
# Backup SQLite database file
cp openclawnet.db openclawnet.db.backup-$(date +%Y%m%d)

# To restore
cp openclawnet.db.backup-YYYYMMDD openclawnet.db
```

SQLite database is a single binary file, making backups simple.

---

## Performance Notes

### Indexes

Current indexes:
- `(ChatMessageEntity.SessionId, ChatMessageEntity.OrderIndex)` — Fast history retrieval
- `(ToolCallRecord.SessionId)` — Session-scoped audit trail

**Future indexes to consider:**
- `CreatedAt` on ChatSession for session listing/filtering
- `NextRunAt` on ScheduledJob for job scheduling queries

### Query Optimization

- Always use `Include()` to avoid N+1 queries
- Use `OrderBy()` + `Take()` for pagination
- Filter in LINQ before `.ToListAsync()` (push filtering to DB)

---

## Integration Points

### Gateway → Storage

`Gateway/Program.cs` registers storage:
```csharp
builder.AddSqliteConnection("openclawnet-db");
builder.Services.AddOpenClawStorage();
```

### Agent Runtime → Storage

`DefaultAgentRuntime` injects `IConversationStore`:
```csharp
var messages = await _conversationStore.GetSessionMessagesAsync(sessionId);
```

### Job Scheduler → Storage

`JobSchedulerService` injects `IJobStore`:
```csharp
var dueJobs = await _jobStore.GetDueJobsAsync();
```

---

## References

- **Context definition:** `src/OpenClawNet.Storage/OpenClawDbContext.cs`
- **Entities:** `src/OpenClawNet.Storage/Entities/`
- **Migrator:** `src/OpenClawNet.Storage/SchemaMigrator.cs`
- **Stores (DAL):** `src/OpenClawNet.Storage/` (ConversationStore, JobStore, etc.)
- **Startup:** `src/OpenClawNet.Gateway/Program.cs` lines 44–46, 150–165

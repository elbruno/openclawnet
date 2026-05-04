using Microsoft.EntityFrameworkCore;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Storage;

/// <summary>
/// Adds missing columns to existing SQLite tables.
/// EnsureCreatedAsync only creates new tables — it never alters existing ones.
/// This migrator bridges the gap for development databases that predate model changes.
/// </summary>
public static class SchemaMigrator
{
    public static async Task MigrateAsync(OpenClawDbContext db)
    {
        // InMemory provider always has the latest schema from the EF model —
        // raw SQL (PRAGMA, ALTER TABLE) is not supported and not needed.
        // The PR-E destructive seed (SeedDefaultMcpServersAsync) is pure EF and runs
        // for every provider — see the bottom of this method.
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            await SeedDefaultMcpServersAsync(db).ConfigureAwait(false);
            return;
        }

        // Jobs table: columns added for smart-scheduler support
        await AddColumnIfMissingAsync(db, "Jobs", "StartAt", "TEXT");
        await AddColumnIfMissingAsync(db, "Jobs", "EndAt", "TEXT");
        await AddColumnIfMissingAsync(db, "Jobs", "TimeZone", "TEXT");
        await AddColumnIfMissingAsync(db, "Jobs", "NaturalLanguageSchedule", "TEXT");

        // Jobs table: concurrency control (default false = skip overlapping runs)
        await AddColumnIfMissingAsync(db, "Jobs", "AllowConcurrentRuns", "INTEGER DEFAULT 0");

        // Migrate old string status values to the new JobStatus enum names
        await MigrateJobStatusValuesAsync(db);

        // Agent profile selection
        await AddColumnIfMissingAsync(db, "Sessions", "AgentProfileName", "TEXT");
        await AddColumnIfMissingAsync(db, "Jobs", "AgentProfileName", "TEXT");

        // Jobs table: PR #6 additions (parameterized prompts + trigger metadata)
        await AddColumnIfMissingAsync(db, "Jobs", "InputParametersJson", "TEXT");
        await AddColumnIfMissingAsync(db, "Jobs", "LastOutputJson", "TEXT");
        await AddColumnIfMissingAsync(db, "Jobs", "TriggerType", "TEXT DEFAULT 'manual'");
        await AddColumnIfMissingAsync(db, "Jobs", "WebhookEndpoint", "TEXT");

        // Jobs table: traceability link from a job back to the template/demo it was
        // instantiated from. Free-form text; null for hand-rolled jobs. Allows multi-
        // instance demo jobs to keep their lineage without coupling to template state.
        await AddColumnIfMissingAsync(db, "Jobs", "SourceTemplateName", "TEXT");

        // JobRuns table: PR #6 additions (execution audit trail)
        await AddColumnIfMissingAsync(db, "JobRuns", "InputSnapshotJson", "TEXT");
        await AddColumnIfMissingAsync(db, "JobRuns", "TokensUsed", "INTEGER");
        await AddColumnIfMissingAsync(db, "JobRuns", "ExecutedByAgentProfile", "TEXT");

        // ModelProviders table
        await CreateTableIfMissingAsync(db, "ModelProviders",
            """
            CREATE TABLE ModelProviders (
                Name TEXT NOT NULL PRIMARY KEY,
                ProviderType TEXT NOT NULL,
                DisplayName TEXT,
                Endpoint TEXT,
                Model TEXT,
                ApiKey TEXT,
                DeploymentName TEXT,
                AuthMode TEXT,
                IsSupported INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )
            """);

        // ModelProviders table: test result tracking columns
        await AddColumnIfMissingAsync(db, "ModelProviders", "LastTestedAt", "TEXT");
        await AddColumnIfMissingAsync(db, "ModelProviders", "LastTestSucceeded", "INTEGER");
        await AddColumnIfMissingAsync(db, "ModelProviders", "LastTestError", "TEXT");

        // AgentProfiles table — add columns for existing databases that predate them
        await AddColumnIfMissingAsync(db, "AgentProfiles", "DisplayName", "TEXT");
        await AddColumnIfMissingAsync(db, "AgentProfiles", "Provider", "TEXT");
        await AddColumnIfMissingAsync(db, "AgentProfiles", "Endpoint", "TEXT");
        await AddColumnIfMissingAsync(db, "AgentProfiles", "ApiKey", "TEXT");
        await AddColumnIfMissingAsync(db, "AgentProfiles", "DeploymentName", "TEXT");
        await AddColumnIfMissingAsync(db, "AgentProfiles", "AuthMode", "TEXT");
        await AddColumnIfMissingAsync(db, "AgentProfiles", "Instructions", "TEXT");
        await AddColumnIfMissingAsync(db, "AgentProfiles", "EnabledTools", "TEXT");
        await AddColumnIfMissingAsync(db, "AgentProfiles", "Temperature", "REAL");
        await AddColumnIfMissingAsync(db, "AgentProfiles", "MaxTokens", "INTEGER");
        await AddColumnIfMissingAsync(db, "AgentProfiles", "IsDefault", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfMissingAsync(db, "AgentProfiles", "CreatedAt", "TEXT");
        await AddColumnIfMissingAsync(db, "AgentProfiles", "UpdatedAt", "TEXT");

        // AgentProfiles table: Wave 4 PR-1 — tool approval gate (safe-by-default = 1)
        await AddColumnIfMissingAsync(db, "AgentProfiles", "RequireToolApproval", "INTEGER NOT NULL DEFAULT 1");

        // AgentProfiles table: test result tracking columns
        await AddColumnIfMissingAsync(db, "AgentProfiles", "LastTestedAt", "TEXT");
        await AddColumnIfMissingAsync(db, "AgentProfiles", "LastTestSucceeded", "INTEGER");
        await AddColumnIfMissingAsync(db, "AgentProfiles", "LastTestError", "TEXT");

        // AgentProfiles table: IsEnabled flag (defaults to 1 = enabled)
        await AddColumnIfMissingAsync(db, "AgentProfiles", "IsEnabled", "INTEGER NOT NULL DEFAULT 1");

        // AgentProfiles table: Kind discriminator (Standard | System | ToolTester)
        await AddColumnIfMissingAsync(db, "AgentProfiles", "Kind", "TEXT NOT NULL DEFAULT 'Standard'");

        // Messages table: Tool approval bubbles — Phase A
        await AddColumnIfMissingAsync(db, "Messages", "MessageType", "TEXT DEFAULT 'Chat'");
        await AddColumnIfMissingAsync(db, "Messages", "ToolName", "TEXT");
        await AddColumnIfMissingAsync(db, "Messages", "ToolArgsJson", "TEXT");
        await AddColumnIfMissingAsync(db, "Messages", "ToolDecision", "TEXT");
        await AddColumnIfMissingAsync(db, "Messages", "ToolDecidedBy", "TEXT");
        await AddColumnIfMissingAsync(db, "Messages", "ToolDecidedAt", "TEXT");

        // AgentProfiles table
        await CreateTableIfMissingAsync(db, "AgentProfiles",
            """
            CREATE TABLE AgentProfiles (
                Name TEXT NOT NULL PRIMARY KEY,
                DisplayName TEXT,
                Provider TEXT,
                Instructions TEXT,
                EnabledTools TEXT,
                Temperature REAL,
                MaxTokens INTEGER,
                IsDefault INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )
            """);

        // PR-A: MCP server registry. Encrypted columns (EnvJson, HeadersJson) hold
        // ciphertext produced by ISecretStore — never raw secrets.
        await CreateTableIfMissingAsync(db, "McpServerDefinitions",
            """
            CREATE TABLE McpServerDefinitions (
                Id TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                Transport TEXT NOT NULL,
                Command TEXT,
                ArgsJson TEXT NOT NULL DEFAULT '[]',
                EnvJson TEXT,
                Url TEXT,
                HeadersJson TEXT,
                Enabled INTEGER NOT NULL DEFAULT 1,
                IsBuiltIn INTEGER NOT NULL DEFAULT 0,
                LastError TEXT,
                LastSeenUtc TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )
            """);
        await CreateIndexIfMissingAsync(db, "IX_McpServerDefinitions_Name",
            "CREATE UNIQUE INDEX IX_McpServerDefinitions_Name ON McpServerDefinitions(Name)");

        await CreateTableIfMissingAsync(db, "McpToolOverrides",
            """
            CREATE TABLE McpToolOverrides (
                ServerId TEXT NOT NULL,
                ToolName TEXT NOT NULL,
                RequireApproval INTEGER,
                Disabled INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT NOT NULL,
                PRIMARY KEY (ServerId, ToolName)
            )
            """);

        // PR-E: idempotency marker table. EnsureCreated handles it for fresh DBs;
        // CreateTableIfMissingAsync handles existing SQLite DBs that predate it.
        await CreateTableIfMissingAsync(db, "SchemaVersions",
            """
            CREATE TABLE SchemaVersions (
                Key TEXT NOT NULL PRIMARY KEY,
                Value TEXT NOT NULL,
                AppliedAt TEXT NOT NULL
            )
            """);

        // ToolTestRecords table — last test outcome per tool (Direct Invoke / Agent Probe).
        await CreateTableIfMissingAsync(db, "ToolTestRecords",
            """
            CREATE TABLE ToolTestRecords (
                Name TEXT NOT NULL PRIMARY KEY,
                LastTestedAt TEXT,
                LastTestSucceeded INTEGER,
                LastTestError TEXT,
                LastTestMode TEXT
            )
            """);

        // Secrets table — DataProtection-encrypted key/value pairs used by tools
        // (e.g. GITHUB_TOKEN, TEXT2IMAGE_FOUNDRY_APIKEY).
        await CreateTableIfMissingAsync(db, "Secrets",
            """
            CREATE TABLE Secrets (
                Name TEXT NOT NULL PRIMARY KEY,
                EncryptedValue TEXT NOT NULL,
                Description TEXT,
                UpdatedAt TEXT NOT NULL
            )
            """);

        // JobRunEvents table — append-only timeline of what happened during a JobRun.
        // Survives app restart (unlike OTEL traces). Cascade-deletes with the parent run.
        await CreateTableIfMissingAsync(db, "JobRunEvents",
            """
            CREATE TABLE JobRunEvents (
                Id TEXT NOT NULL PRIMARY KEY,
                JobRunId TEXT NOT NULL,
                Sequence INTEGER NOT NULL,
                Timestamp TEXT NOT NULL,
                Kind TEXT NOT NULL,
                ToolName TEXT,
                ArgumentsJson TEXT,
                ResultJson TEXT,
                Message TEXT,
                DurationMs INTEGER,
                TokensUsed INTEGER,
                FOREIGN KEY (JobRunId) REFERENCES JobRuns(Id) ON DELETE CASCADE
            )
            """);
        await CreateIndexIfMissingAsync(db,
            "IX_JobRunEvents_JobRunId_Sequence",
            "CREATE INDEX IX_JobRunEvents_JobRunId_Sequence ON JobRunEvents(JobRunId, Sequence)");

        // JobRunArtifacts table — Phase 1 job output dashboard
        await CreateTableIfMissingAsync(db, "JobRunArtifacts",
            """
            CREATE TABLE JobRunArtifacts (
                Id TEXT NOT NULL PRIMARY KEY,
                JobRunId TEXT NOT NULL,
                JobId TEXT NOT NULL,
                Sequence INTEGER NOT NULL DEFAULT 0,
                ArtifactType TEXT NOT NULL DEFAULT 'text',
                Title TEXT,
                ContentInline TEXT,
                ContentPath TEXT,
                ContentSizeBytes INTEGER NOT NULL DEFAULT 0,
                MimeType TEXT,
                CreatedAt TEXT NOT NULL,
                Metadata TEXT,
                FOREIGN KEY (JobRunId) REFERENCES JobRuns(Id) ON DELETE CASCADE
            )
            """);
        await CreateIndexIfMissingAsync(db, "IX_JobRunArtifacts_JobId_CreatedAt",
            "CREATE INDEX IX_JobRunArtifacts_JobId_CreatedAt ON JobRunArtifacts(JobId, CreatedAt DESC)");
        await CreateIndexIfMissingAsync(db, "IX_JobRunArtifacts_JobRunId_Sequence",
            "CREATE INDEX IX_JobRunArtifacts_JobRunId_Sequence ON JobRunArtifacts(JobRunId, Sequence)");

        // ── Concept-review (April 2026) §4a/4b/4c — new audit + telemetry tables ──

        // §4b: server-default approval (nullable; null = inherit from agent profile).
        await AddColumnIfMissingAsync(db, "McpServerDefinitions", "DefaultRequireApproval", "INTEGER");

        // §4b: per-job state-change audit log.
        await CreateTableIfMissingAsync(db, "JobStateChanges",
            """
            CREATE TABLE JobStateChanges (
                Id TEXT NOT NULL PRIMARY KEY,
                JobId TEXT NOT NULL,
                FromStatus TEXT NOT NULL,
                ToStatus TEXT NOT NULL,
                Reason TEXT,
                ChangedBy TEXT,
                ChangedAt TEXT NOT NULL,
                FOREIGN KEY (JobId) REFERENCES Jobs(Id) ON DELETE CASCADE
            )
            """);
        await CreateIndexIfMissingAsync(db, "IX_JobStateChanges_JobId_ChangedAt",
            "CREATE INDEX IX_JobStateChanges_JobId_ChangedAt ON JobStateChanges(JobId, ChangedAt DESC)");

        // §4a: tool-approval decision audit log.
        await CreateTableIfMissingAsync(db, "ToolApprovalLogs",
            """
            CREATE TABLE ToolApprovalLogs (
                Id TEXT NOT NULL PRIMARY KEY,
                RequestId TEXT NOT NULL,
                SessionId TEXT NOT NULL,
                ToolName TEXT NOT NULL,
                AgentProfileName TEXT,
                Approved INTEGER NOT NULL,
                RememberForSession INTEGER NOT NULL,
                Source TEXT NOT NULL,
                DecidedAt TEXT NOT NULL
            )
            """);
        await CreateIndexIfMissingAsync(db, "IX_ToolApprovalLogs_SessionId",
            "CREATE INDEX IX_ToolApprovalLogs_SessionId ON ToolApprovalLogs(SessionId)");
        await CreateIndexIfMissingAsync(db, "IX_ToolApprovalLogs_RequestId",
            "CREATE INDEX IX_ToolApprovalLogs_RequestId ON ToolApprovalLogs(RequestId)");
        await CreateIndexIfMissingAsync(db, "IX_ToolApprovalLogs_DecidedAt",
            "CREATE INDEX IX_ToolApprovalLogs_DecidedAt ON ToolApprovalLogs(DecidedAt)");

        // §4c: shared telemetry across chat + job runs (sibling-model, Option B).
        await CreateTableIfMissingAsync(db, "AgentInvocationLogs",
            """
            CREATE TABLE AgentInvocationLogs (
                Id TEXT NOT NULL PRIMARY KEY,
                Kind TEXT NOT NULL,
                SourceId TEXT NOT NULL,
                AgentProfileName TEXT,
                Provider TEXT,
                Model TEXT,
                TokensIn INTEGER,
                TokensOut INTEGER,
                LatencyMs INTEGER,
                StartedAt TEXT NOT NULL,
                CompletedAt TEXT,
                Error TEXT
            )
            """);
        await CreateIndexIfMissingAsync(db, "IX_AgentInvocationLogs_Kind_SourceId",
            "CREATE INDEX IX_AgentInvocationLogs_Kind_SourceId ON AgentInvocationLogs(Kind, SourceId)");
        await CreateIndexIfMissingAsync(db, "IX_AgentInvocationLogs_StartedAt",
            "CREATE INDEX IX_AgentInvocationLogs_StartedAt ON AgentInvocationLogs(StartedAt)");

        // §4c: optional chat-side artifact stream — channels can include these via feature flag.
        await CreateTableIfMissingAsync(db, "ChatSessionArtifacts",
            """
            CREATE TABLE ChatSessionArtifacts (
                Id TEXT NOT NULL PRIMARY KEY,
                SessionId TEXT NOT NULL,
                Sequence INTEGER NOT NULL DEFAULT 0,
                ArtifactType TEXT NOT NULL DEFAULT 'text',
                Title TEXT,
                ContentInline TEXT,
                ContentPath TEXT,
                ContentSizeBytes INTEGER NOT NULL DEFAULT 0,
                MimeType TEXT,
                CreatedAt TEXT NOT NULL,
                Metadata TEXT,
                FOREIGN KEY (SessionId) REFERENCES Sessions(Id) ON DELETE CASCADE
            )
            """);
        await CreateIndexIfMissingAsync(db, "IX_ChatSessionArtifacts_SessionId_Sequence",
            "CREATE INDEX IX_ChatSessionArtifacts_SessionId_Sequence ON ChatSessionArtifacts(SessionId, Sequence)");
        await CreateIndexIfMissingAsync(db, "IX_ChatSessionArtifacts_CreatedAt",
            "CREATE INDEX IX_ChatSessionArtifacts_CreatedAt ON ChatSessionArtifacts(CreatedAt)");

        // Phase 2 Feature 1: Job-to-Channel Routing Model (Story 3)
        // Stores the channel routing configuration for jobs (which channels receive notifications).
        await CreateTableIfMissingAsync(db, "JobChannelConfigurations",
            """
            CREATE TABLE JobChannelConfigurations (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                JobId TEXT NOT NULL,
                ChannelType TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 0,
                ChannelConfig TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (JobId) REFERENCES Jobs(Id) ON DELETE CASCADE
            )
            """);
        await CreateIndexIfMissingAsync(db, "IX_JobChannelConfigurations_JobId",
            "CREATE INDEX IX_JobChannelConfigurations_JobId ON JobChannelConfigurations(JobId)");
        await CreateIndexIfMissingAsync(db, "IX_JobChannelConfigurations_JobId_ChannelType",
            "CREATE UNIQUE INDEX IX_JobChannelConfigurations_JobId_ChannelType ON JobChannelConfigurations(JobId, ChannelType)");

        // Phase 2 Feature 1: Delivery Audit Log (Story 4)
        // Tracks success/failure of each channel delivery attempt for post-mortem analysis.
        await CreateTableIfMissingAsync(db, "AdapterDeliveryLogs",
            """
            CREATE TABLE AdapterDeliveryLogs (
                Id TEXT NOT NULL PRIMARY KEY,
                JobId TEXT NOT NULL,
                ChannelType TEXT NOT NULL,
                ChannelConfig TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'pending',
                DeliveredAt TEXT,
                ErrorMessage TEXT,
                ResponseCode INTEGER,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (JobId) REFERENCES Jobs(Id) ON DELETE CASCADE
            )
            """);
        await CreateIndexIfMissingAsync(db, "IX_AdapterDeliveryLogs_JobId",
            "CREATE INDEX IX_AdapterDeliveryLogs_JobId ON AdapterDeliveryLogs(JobId)");
        await CreateIndexIfMissingAsync(db, "IX_AdapterDeliveryLogs_CreatedAt",
            "CREATE INDEX IX_AdapterDeliveryLogs_CreatedAt ON AdapterDeliveryLogs(CreatedAt)");

        // PR-F: drop the legacy AgentProfiles.Model column.The agent now references a
        // ModelProviderDefinition whose own Model field is authoritative — see Bruno's
        // directive on the agent-UI redesign. Idempotent via the SchemaVersions marker.
        await DropAgentProfileModelColumnAsync(db);

        // PR-E: destructive seed of the 4 bundled MCP servers + remap of legacy
        // EnabledTools entries. Gated by the SchemaVersion marker so it runs once.
        await SeedDefaultMcpServersAsync(db).ConfigureAwait(false);
    }

    // ── PR-E: destructive seed + EnabledTools remap (single, idempotent step) ──

    /// <summary>Marker key written to <c>SchemaVersions</c> after the seed completes.</summary>
    internal const string McpDefaultsMarker = "mcp.defaults.v1";

    /// <summary>Marker key written to <c>SchemaVersions</c> after the AgentProfiles.Model column is dropped (PR-F).</summary>
    internal const string AgentProfileDropModelMarker = "agentprofile.drop-model.v1";

    /// <summary>
    /// Stable Ids for the 4 bundled MCP server rows. Kept in sync with the
    /// <c>ServerId</c> constants in <c>OpenClawNet.Mcp.Web/Shell/Browser/FileSystem</c> —
    /// changing one without the other would orphan the runtime registration.
    /// </summary>
    internal static readonly Guid WebServerId        = new("8f7d1c80-1111-4a11-8001-77627e620001");
    internal static readonly Guid ShellServerId      = new("8f7d1c80-1111-4a11-8001-77627e620002");
    internal static readonly Guid BrowserServerId    = new("8f7d1c80-1111-4a11-8001-77627e620003");
    internal static readonly Guid FileSystemServerId = new("8f7d1c80-1111-4a11-8001-77627e620004");

    /// <summary>
    /// Bruno's directive (D4): clear any previously persisted MCP server rows and re-seed
    /// the canonical 4 built-ins. Also remaps legacy bare tool names in
    /// <c>AgentProfile.EnabledTools</c> to the new <c>&lt;server&gt;.&lt;tool&gt;</c> form.
    /// Runs exactly once per database via the <see cref="McpDefaultsMarker"/> row.
    /// </summary>
    public static async Task SeedDefaultMcpServersAsync(OpenClawDbContext db)
    {
        var marker = await db.SchemaVersions
            .FirstOrDefaultAsync(v => v.Key == McpDefaultsMarker)
            .ConfigureAwait(false);
        if (marker is not null) return;

        // 1. Wipe every existing MCP row. Tool overrides go first to keep the FK story
        //    obvious even though the schema has no FK declared today.
        if (await db.McpToolOverrides.AnyAsync().ConfigureAwait(false))
        {
            db.McpToolOverrides.RemoveRange(db.McpToolOverrides);
            await db.SaveChangesAsync().ConfigureAwait(false);
        }
        if (await db.McpServerDefinitions.AnyAsync().ConfigureAwait(false))
        {
            db.McpServerDefinitions.RemoveRange(db.McpServerDefinitions);
            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        // 2. Seed the 4 canonical built-ins.
        var now = DateTime.UtcNow;
        db.McpServerDefinitions.AddRange(
            new McpServerDefinitionEntity
            {
                Id = WebServerId,
                Name = "web",
                Transport = "InProcess",
                ArgsJson = "[]",
                Enabled = true,
                IsBuiltIn = true,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new McpServerDefinitionEntity
            {
                Id = ShellServerId,
                Name = "shell",
                Transport = "InProcess",
                ArgsJson = "[]",
                Enabled = true,
                IsBuiltIn = true,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new McpServerDefinitionEntity
            {
                Id = BrowserServerId,
                Name = "browser",
                Transport = "InProcess",
                ArgsJson = "[]",
                Enabled = true,
                IsBuiltIn = true,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new McpServerDefinitionEntity
            {
                Id = FileSystemServerId,
                Name = "filesystem",
                Transport = "InProcess",
                ArgsJson = "[]",
                Enabled = true,
                IsBuiltIn = true,
                CreatedAt = now,
                UpdatedAt = now,
            });

        // 3. Migrate AgentProfile.EnabledTools — bare legacy names → <server>.<tool>.
        await RemapAgentProfileEnabledToolsAsync(db).ConfigureAwait(false);

        // 4. Plant the marker so this step never repeats.
        db.SchemaVersions.Add(new SchemaVersionEntity
        {
            Key = McpDefaultsMarker,
            Value = "1",
            AppliedAt = now,
        });

        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Mapping from legacy bare <see cref="Tools.Abstractions.ITool.Name"/> values to the
    /// <c>&lt;server&gt;.&lt;tool&gt;</c> storage form introduced by PR-D. Multi-action
    /// legacy tools (filesystem, browser) expand to every concrete sub-tool — preserves
    /// the user's intent of "this whole tool is allowed".
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string[]> LegacyToolNameMap =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["web_fetch"]   = new[] { "web.fetch" },
            ["fetch"]       = new[] { "web.fetch" },
            ["shell"]       = new[] { "shell.exec" },
            ["run"]         = new[] { "shell.exec" },
            ["file_system"] = new[]
            {
                "filesystem.read",
                "filesystem.write",
                "filesystem.list",
                "filesystem.find_projects",
            },
            ["read_file"]   = new[] { "filesystem.read" },
            ["write_file"]  = new[] { "filesystem.write" },
            ["list_dir"]    = new[] { "filesystem.list" },
            ["browser"]     = new[]
            {
                "browser.navigate",
                "browser.extract_text",
                "browser.screenshot",
                "browser.click",
                "browser.fill",
            },
            ["navigate"]    = new[] { "browser.navigate" },
            ["schedule"]    = new[] { "scheduler.schedule" },
            ["add_task"]    = new[] { "scheduler.schedule" },
            ["list_tasks"]  = new[] { "scheduler.schedule" },
        };

    private static async Task RemapAgentProfileEnabledToolsAsync(OpenClawDbContext db)
    {
        var profiles = await db.AgentProfiles
            .Where(p => p.EnabledTools != null && p.EnabledTools != "")
            .ToListAsync()
            .ConfigureAwait(false);

        var unmappedSeen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var profile in profiles)
        {
            var remapped = RemapEnabledToolsCsv(profile.EnabledTools, unmappedSeen, out var changed);
            if (!changed) continue;
            profile.EnabledTools = remapped;
            profile.UpdatedAt = DateTime.UtcNow;
        }

        if (unmappedSeen.Count > 0)
        {
            // No ILogger here — write a one-shot console line (sufficient for a silent
            // migration; the WARN was the requirement, not structured logging).
            Console.Error.WriteLine(
                "[SchemaMigrator] WARN: AgentProfile.EnabledTools contained unmapped tool names "
                + $"that were preserved as-is: {string.Join(", ", unmappedSeen.OrderBy(n => n))}");
        }
    }

    /// <summary>
    /// Splits a CSV EnabledTools value, applies <see cref="LegacyToolNameMap"/>, and rejoins
    /// the result. Names already in <c>server.tool</c> form (containing a dot) pass through
    /// untouched. Names with no mapping are preserved and added to <paramref name="unmapped"/>.
    /// </summary>
    internal static string? RemapEnabledToolsCsv(string? csv, ISet<string> unmapped, out bool changed)
    {
        changed = false;
        if (string.IsNullOrWhiteSpace(csv)) return csv;

        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<string>(parts.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in parts)
        {
            if (raw.Contains('.', StringComparison.Ordinal))
            {
                if (seen.Add(raw)) result.Add(raw);
                continue;
            }

            if (LegacyToolNameMap.TryGetValue(raw, out var mapped))
            {
                changed = true;
                foreach (var m in mapped)
                    if (seen.Add(m)) result.Add(m);
            }
            else
            {
                unmapped.Add(raw);
                if (seen.Add(raw)) result.Add(raw);
            }
        }

        return string.Join(", ", result);
    }

    /// <summary>
    /// Converts legacy status strings to the new enum-compatible values.
    /// "pending" → "draft", "queued"/"failed" → "completed".
    /// </summary>
    private static async Task MigrateJobStatusValuesAsync(OpenClawDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        var updates = new (string Old, string New)[]
        {
            ("pending", "draft"),
            ("queued", "completed"),
            ("failed", "completed"),
        };

        foreach (var (old, @new) in updates)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"UPDATE Jobs SET Status = '{@new}' WHERE Status = '{old}'";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// PR-F: drops the legacy <c>AgentProfiles.Model</c> column. SQLite has supported
    /// <c>ALTER TABLE … DROP COLUMN</c> since 3.35 (Apr 2021); the bundled
    /// <c>Microsoft.Data.Sqlite</c> provider ships a newer SQLite than that. Idempotent
    /// via a row in <c>SchemaVersions</c> so the destructive step runs at most once per
    /// database. No-op when the column is already absent (fresh DBs from the updated
    /// CREATE TABLE) or when the marker is already present.
    /// </summary>
    private static async Task DropAgentProfileModelColumnAsync(OpenClawDbContext db)
    {
        var marker = await db.SchemaVersions
            .FirstOrDefaultAsync(v => v.Key == AgentProfileDropModelMarker)
            .ConfigureAwait(false);
        if (marker is not null) return;

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync().ConfigureAwait(false);

        bool hasModelColumn = false;
        using (var info = conn.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(AgentProfiles)";
            using var reader = await info.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                if (string.Equals(reader["name"]?.ToString(), "Model", StringComparison.OrdinalIgnoreCase))
                {
                    hasModelColumn = true;
                    break;
                }
            }
        }

        if (hasModelColumn)
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE AgentProfiles DROP COLUMN Model";
            await alter.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        db.SchemaVersions.Add(new SchemaVersionEntity
        {
            Key = AgentProfileDropModelMarker,
            Value = "1",
            AppliedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private static async Task AddColumnIfMissingAsync(
        OpenClawDbContext db, string table, string column, string type)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            if (string.Equals(reader["name"]?.ToString(), column, StringComparison.OrdinalIgnoreCase))
                return; // Column already exists
        }

        await reader.CloseAsync();

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
        await alter.ExecuteNonQueryAsync();
    }

    private static async Task CreateTableIfMissingAsync(
        OpenClawDbContext db, string table, string createSql)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        using var check = conn.CreateCommand();
        check.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{table}'";
        var result = await check.ExecuteScalarAsync();
        if (result is not null) return;

        using var create = conn.CreateCommand();
        create.CommandText = createSql;
        await create.ExecuteNonQueryAsync();
    }

    private static async Task CreateIndexIfMissingAsync(
        OpenClawDbContext db, string indexName, string createSql)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        using var check = conn.CreateCommand();
        check.CommandText = $"SELECT name FROM sqlite_master WHERE type='index' AND name='{indexName}'";
        var result = await check.ExecuteScalarAsync();
        if (result is not null) return;

        using var create = conn.CreateCommand();
        create.CommandText = createSql;
        await create.ExecuteNonQueryAsync();
    }
}

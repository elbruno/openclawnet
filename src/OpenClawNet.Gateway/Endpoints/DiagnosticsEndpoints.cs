using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenClawNet.Storage;
using System.Diagnostics;
using System.Reflection;

namespace OpenClawNet.Gateway.Endpoints;

public static class DiagnosticsEndpoints
{
    public static void MapDiagnosticsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/diagnostics").WithTags("Diagnostics");

        // GET /api/diagnostics/db — db file path, size, last-write
        group.MapGet("/db", async (IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            
            string? dbPath = null;
            long? sizeBytes = null;
            DateTime? lastWriteTime = null;
            string? error = null;

            // GetConnectionString is relational-only; handle non-relational providers
            if (db.Database.IsRelational())
            {
                try
                {
                    var connectionString = db.Database.GetConnectionString();
                    // Parse the connection string to get the database file path
                    var builder = new SqliteConnectionStringBuilder(connectionString);
                    dbPath = builder.DataSource;

                    if (!string.IsNullOrEmpty(dbPath) && dbPath != ":memory:" && File.Exists(dbPath))
                    {
                        var fileInfo = new FileInfo(dbPath);
                        sizeBytes = fileInfo.Length;
                        lastWriteTime = fileInfo.LastWriteTimeUtc;
                    }
                    else if (dbPath == ":memory:")
                    {
                        error = "In-memory database (no file)";
                    }
                    else
                    {
                        error = "Database file not found";
                    }
                }
                catch (Exception ex)
                {
                    error = $"Failed to read database info: {ex.Message}";
                }
            }
            else
            {
                // Non-relational provider (e.g., InMemory for tests)
                dbPath = db.Database.ProviderName;
                error = "Non-relational database provider";
            }

            // Get table counts
            var jobCount = await db.Jobs.CountAsync();
            var runCount = await db.JobRuns.CountAsync();
            var sessionCount = await db.Set<OpenClawNet.Storage.Entities.ChatSession>().CountAsync();

            return Results.Ok(new DatabaseDiagnosticsDto(
                dbPath,
                sizeBytes,
                lastWriteTime,
                error,
                jobCount,
                runCount,
                sessionCount
            ));
        })
        .WithName("GetDatabaseDiagnostics")
        .WithDescription("Get database file information (path, size, last write time, entity counts)");

        // GET /api/diagnostics/info — build sha, version, started-at, uptime
        group.MapGet("/info", (IHostEnvironment env) =>
        {
            var assembly = Assembly.GetEntryAssembly();
            var version = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly?.GetName().Version?.ToString()
                ?? "unknown";

            var buildDate = assembly?.GetCustomAttribute<AssemblyMetadataAttribute>()
                ?.Value ?? "unknown";

            var startTime = Process.GetCurrentProcess().StartTime.ToUniversalTime();
            var uptime = DateTime.UtcNow - startTime;

            return Results.Ok(new SystemInfoDto(
                "OpenClawNet",
                version,
                buildDate,
                env.EnvironmentName,
                startTime,
                uptime.TotalSeconds
            ));
        })
        .WithName("GetSystemInfo")
        .WithDescription("Get system information (version, environment, uptime)");
    }
}

public sealed record DatabaseDiagnosticsDto(
    string? DatabasePath,
    long? SizeBytes,
    DateTime? LastWriteTime,
    string? Error,
    int JobCount,
    int RunCount,
    int SessionCount
);

public sealed record SystemInfoDto(
    string Name,
    string Version,
    string BuildDate,
    string Environment,
    DateTime StartedAt,
    double UptimeSeconds
);

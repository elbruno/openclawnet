using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenClawNet.Storage;

namespace OpenClawNet.UnitTests.Storage;

/// <summary>
/// Covers <see cref="SchemaMigrator"/> behavior for <c>AgentProfiles.Model</c>
/// on a real in-memory SQLite connection. The column is part of the current model;
/// these tests guard against duplicate-column migration failures and accidental
/// reactivation of the old destructive drop migration.
/// </summary>
public class SchemaMigratorDropAgentProfileModelTests
{
    [Fact]
    public async Task Migrate_PreservesModelColumn_WhenPresent()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        try
        {
            await using var db = new OpenClawDbContext(
                new DbContextOptionsBuilder<OpenClawDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();

            using (var seed = connection.CreateCommand())
            {
                seed.CommandText =
                    """
                    INSERT INTO AgentProfiles (Name, Model, IsDefault, RequireToolApproval, IsEnabled, Kind, CreatedAt, UpdatedAt)
                    VALUES ('current', 'gpt-4o', 0, 0, 1, 'Standard', '2025-01-01T00:00:00', '2025-01-01T00:00:00');
                    """;
                await seed.ExecuteNonQueryAsync();
            }

            await SchemaMigrator.MigrateAsync(db);

            var hasModel = await ColumnExistsAsync(connection, "AgentProfiles", "Model");
            hasModel.Should().BeTrue("AgentProfiles.Model is required for per-profile model selection");

            await using var freshDb = new OpenClawDbContext(
                new DbContextOptionsBuilder<OpenClawDbContext>().UseSqlite(connection).Options);
            var profile = await freshDb.AgentProfiles.FirstOrDefaultAsync(p => p.Name == "current");
            profile.Should().NotBeNull();
            profile!.Model.Should().Be("gpt-4o");

            var marker = await freshDb.SchemaVersions
                .FirstOrDefaultAsync(v => v.Key == SchemaMigrator.AgentProfileDropModelMarker);
            marker.Should().BeNull("the old destructive drop migration must remain disabled");
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    [Fact]
    public async Task Migrate_IsNoOp_WhenModelColumnAlreadyPresent()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        try
        {
            await using var db = new OpenClawDbContext(
                new DbContextOptionsBuilder<OpenClawDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();

            await SchemaMigrator.MigrateAsync(db);
            await SchemaMigrator.MigrateAsync(db);

            var hasModel = await ColumnExistsAsync(connection, "AgentProfiles", "Model");
            hasModel.Should().BeTrue();

            await using var freshDb = new OpenClawDbContext(
                new DbContextOptionsBuilder<OpenClawDbContext>().UseSqlite(connection).Options);
            var markers = await freshDb.SchemaVersions
                .Where(v => v.Key == SchemaMigrator.AgentProfileDropModelMarker)
                .CountAsync();
             markers.Should().Be(0, "the disabled drop migration must not write markers");
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader["name"]?.ToString(), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

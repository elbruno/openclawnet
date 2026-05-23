using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Storage;

public static class SecretAccessAuditHashChain
{
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    public static string ComputeRowHash(
        string? previousRowHash,
        DateTime accessedAt,
        string callerType,
        string callerId,
        string? sessionId,
        string secretName,
        bool success)
    {
        var canonical = string.Join('|',
            NormalizeHash(previousRowHash),
            NormalizeUtc(accessedAt).ToString("O", CultureInfo.InvariantCulture),
            Normalize(callerType),
            Normalize(callerId),
            Normalize(sessionId),
            Normalize(secretName),
            success ? "success" : "failure");

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    internal static async Task BootstrapMissingHashesAsync(OpenClawDbContext db, CancellationToken ct = default)
    {
        if (!await db.SecretAccessAudit.AnyAsync(a => a.RowHash == null || a.PreviousRowHash == null, ct).ConfigureAwait(false))
            return;

        var previous = GenesisHash;
        var rows = await db.SecretAccessAudit
            .OrderBy(a => a.Sequence ?? 0)
            .ThenBy(a => a.AccessedAt)
            .ThenBy(a => a.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            row.PreviousRowHash = previous;
            row.RowHash = ComputeRowHash(
                previous,
                row.AccessedAt,
                row.CallerType,
                row.CallerId,
                row.SessionId,
                row.SecretName,
                row.Success);
            previous = row.RowHash!;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public static async Task<bool> VerifyAsync(OpenClawDbContext db, CancellationToken ct = default)
    {
        var previous = GenesisHash;
        var rows = await db.SecretAccessAudit
            .AsNoTracking()
            .OrderBy(a => a.Sequence ?? 0)
            .ThenBy(a => a.AccessedAt)
            .ThenBy(a => a.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            if (!string.Equals(row.PreviousRowHash, previous, StringComparison.Ordinal))
                return false;

            var expected = ComputeRowHash(
                previous,
                row.AccessedAt,
                row.CallerType,
                row.CallerId,
                row.SessionId,
                row.SecretName,
                row.Success);
            if (!string.Equals(row.RowHash, expected, StringComparison.Ordinal))
                return false;

            previous = row.RowHash;
        }

        return true;
    }

    private static string Normalize(string? value) => value ?? string.Empty;

    private static string NormalizeHash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? GenesisHash : value.Trim().ToLowerInvariant();

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}

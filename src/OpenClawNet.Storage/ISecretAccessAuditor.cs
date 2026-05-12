using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Storage;

public interface ISecretAccessAuditor
{
    Task RecordAsync(string secretName, VaultCallerContext ctx, bool success, CancellationToken ct = default);
}

public sealed class SecretAccessAuditor : ISecretAccessAuditor
{
    private static readonly SemaphoreSlim ChainLock = new(1, 1);
    private readonly IDbContextFactory<OpenClawDbContext> _dbFactory;
    private readonly ILogger<SecretAccessAuditor> _logger;

    public SecretAccessAuditor(IDbContextFactory<OpenClawDbContext> dbFactory, ILogger<SecretAccessAuditor> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task RecordAsync(string secretName, VaultCallerContext ctx, bool success, CancellationToken ct = default)
    {
        await ChainLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

            // Get the last sequence number to compute the next one
            long nextSequence = await db.Set<SecretAccessAuditEntity>()
                .AsNoTracking()
                .Select(a => (long?)a.Sequence)
                .MaxAsync(ct)
                .ConfigureAwait(false) ?? 0;
            nextSequence++;

            string previous = await db.Set<SecretAccessAuditEntity>()
                .AsNoTracking()
                .OrderByDescending(a => a.Sequence ?? 0)
                .Select(a => a.RowHash)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false)
                ?? SecretAccessAuditHashChain.GenesisHash;

            var accessedAt = DateTime.UtcNow;
            var entity = new SecretAccessAuditEntity
            {
                Id = Guid.NewGuid(),
                Sequence = nextSequence,
                SecretName = secretName,
                CallerType = ctx.CallerType.ToString(),
                CallerId = ctx.CallerId,
                SessionId = ctx.SessionId,
                AccessedAt = accessedAt,
                Success = success,
                PreviousRowHash = previous,
                RowHash = SecretAccessAuditHashChain.ComputeRowHash(
                    previous,
                    accessedAt,
                    ctx.CallerType.ToString(),
                    ctx.CallerId,
                    ctx.SessionId,
                    secretName,
                    success)
            };

            db.Set<SecretAccessAuditEntity>().Add(entity);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            ChainLock.Release();
        }

        _logger.LogInformation(
            "Vault secret access audited: callerType={CallerType}, success={Success}",
            ctx.CallerType,
            success);
    }
}

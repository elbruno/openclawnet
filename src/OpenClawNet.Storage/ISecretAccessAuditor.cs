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
    private readonly IDbContextFactory<OpenClawDbContext> _dbFactory;
    private readonly ILogger<SecretAccessAuditor> _logger;

    public SecretAccessAuditor(IDbContextFactory<OpenClawDbContext> dbFactory, ILogger<SecretAccessAuditor> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task RecordAsync(string secretName, VaultCallerContext ctx, bool success, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.Set<SecretAccessAuditEntity>().Add(new SecretAccessAuditEntity
        {
            Id = Guid.NewGuid(),
            SecretName = secretName,
            CallerType = ctx.CallerType.ToString(),
            CallerId = ctx.CallerId,
            SessionId = ctx.SessionId,
            AccessedAt = DateTime.UtcNow,
            Success = success
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Vault secret access audited: secretName={SecretName}, callerType={CallerType}, success={Success}",
            secretName,
            ctx.CallerType,
            success);
    }
}

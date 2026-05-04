using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace OpenClawNet.Storage.Entities;

/// <summary>
/// Best-effort writer for <see cref="AgentInvocationLog"/> rows. Concept-review §4c
/// (Option B: Sibling Model) — adopted as the single chokepoint so chat and job
/// invocations land in the same table with the same shape.
/// </summary>
/// <remarks>
/// All exceptions are caught and logged at Warning. A failure here must NEVER fail
/// the parent agent invocation.
/// </remarks>
public sealed class AgentInvocationLogger
{
    private readonly IDbContextFactory<OpenClawDbContext> _dbFactory;
    private readonly ILogger<AgentInvocationLogger> _logger;

    public AgentInvocationLogger(
        IDbContextFactory<OpenClawDbContext> dbFactory,
        ILogger<AgentInvocationLogger> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task RecordAsync(AgentInvocationLog entry, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            db.Set<AgentInvocationLog>().Add(entry);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "AgentInvocationLogger: failed to persist invocation log (kind={Kind}, source={SourceId})",
                entry.Kind, entry.SourceId);
        }
    }
}

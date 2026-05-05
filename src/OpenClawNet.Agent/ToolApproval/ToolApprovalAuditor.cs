using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Agent.ToolApproval;

/// <summary>
/// SQLite-backed <see cref="IToolApprovalAuditor"/>. Writes are best-effort:
/// any persistence failure is logged at Warning and swallowed so the parent
/// tool call never fails for audit reasons. Concept-review §4a.
/// </summary>
/// <remarks>
/// Lives in the Agent project (not Storage) because Storage cannot reference Agent
/// without a project-reference cycle. Agent already references Storage for DbContext.
/// </remarks>
public sealed class ToolApprovalAuditor : IToolApprovalAuditor
{
    private readonly IDbContextFactory<OpenClawDbContext> _dbFactory;
    private readonly ILogger<ToolApprovalAuditor> _logger;

    public ToolApprovalAuditor(
        IDbContextFactory<OpenClawDbContext> dbFactory,
        ILogger<ToolApprovalAuditor> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task RecordAsync(ToolApprovalAuditEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            
            var decidedAt = DateTime.UtcNow;
            
            // Write to ToolApprovalLogs table (existing audit trail)
            db.ToolApprovalLogs.Add(new ToolApprovalLog
            {
                RequestId = entry.RequestId,
                SessionId = entry.SessionId,
                ToolName = entry.ToolName,
                AgentProfileName = entry.AgentProfileName,
                Approved = entry.Approved,
                RememberForSession = entry.RememberForSession,
                Source = (ApprovalDecisionSource)entry.Source,
                DecidedAt = decidedAt,
            });
            
            // Phase A: Also persist as a ChatMessage for bubble display
            var session = await db.Sessions
                .Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.Id == entry.SessionId, cancellationToken)
                .ConfigureAwait(false);
            
            if (session is not null)
            {
                var decision = entry.Approved ? "Approved" : 
                    (entry.Source == ToolApprovalAuditSource.Timeout ? "TimedOut" : "Denied");
                
                // Truncate args to 2KB to prevent excessive storage
                var argsJson = entry.ToolArgsJson;
                if (argsJson is not null && argsJson.Length > 2048)
                {
                    argsJson = argsJson.Substring(0, 2045) + "...";
                }
                
                var maxIndex = session.Messages.Count > 0 
                    ? session.Messages.Max(m => m.OrderIndex) 
                    : -1;
                
                var sourceDisplay = entry.Source switch
                {
                    ToolApprovalAuditSource.User => "User",
                    ToolApprovalAuditSource.SessionMemory => "Session Memory",
                    ToolApprovalAuditSource.Timeout => "System (Timeout)",
                    _ => "System"
                };
                
                db.Messages.Add(new ChatMessageEntity
                {
                    SessionId = entry.SessionId,
                    Role = "system",
                    Content = $"Tool approval: {entry.ToolName}",
                    MessageType = "ToolApproval",
                    ToolName = entry.ToolName,
                    ToolArgsJson = argsJson,
                    ToolDecision = decision,
                    ToolDecidedBy = sourceDisplay,
                    ToolDecidedAt = decidedAt,
                    CreatedAt = decidedAt,
                    OrderIndex = maxIndex + 1
                });
            }
            
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ToolApprovalAuditor: failed to persist approval log for {ToolName} (request {RequestId})",
                entry.ToolName, entry.RequestId);
        }
    }
}

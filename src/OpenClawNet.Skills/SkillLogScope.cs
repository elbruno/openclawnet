using Microsoft.Extensions.Logging;

namespace OpenClawNet.Skills;

/// <summary>
/// K-2 — Helper that opens an <see cref="ILogger.BeginScope{TState}"/> with
/// the canonical (<c>SnapshotId</c>, <c>AgentId</c>, optional <c>ChatId</c>)
/// triplet so every child event in the scope inherits correlation context.
/// </summary>
/// <remarks>
/// Scope keys use PascalCase to match the structured property names in
/// <see cref="SkillsLog"/>. Pass into a <c>using</c> block:
/// <code>
/// using (SkillLogScope.Begin(_logger, snapshotId, agentId, chatId))
/// {
///     _logger.SkillFunctionInvoked(...);
/// }
/// </code>
/// Returns <see cref="EmptyScope"/> if the logger cannot open a scope (e.g.
/// <c>NullLogger</c>) so call sites never need a null check.
/// </remarks>
public static class SkillLogScope
{
    public static IDisposable Begin(
        ILogger logger,
        string snapshotId,
        string agentId,
        string? chatId = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        var state = chatId is null
            ? (IReadOnlyDictionary<string, object>)new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["SnapshotId"] = snapshotId,
                ["AgentId"] = agentId,
            }
            : new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["SnapshotId"] = snapshotId,
                ["AgentId"] = agentId,
                ["ChatId"] = chatId,
            };
        return logger.BeginScope(state) ?? EmptyScope.Instance;
    }

    private sealed class EmptyScope : IDisposable
    {
        public static readonly EmptyScope Instance = new();
        public void Dispose() { }
    }
}

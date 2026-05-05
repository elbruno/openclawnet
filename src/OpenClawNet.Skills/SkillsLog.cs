using Microsoft.Extensions.Logging;

namespace OpenClawNet.Skills;

/// <summary>
/// K-2 — Structured logger surface for the skills audit taxonomy. Uses
/// <see cref="LoggerMessageAttribute"/> source generation for the hot-path
/// events (snapshot pin, function invoked/completed) and well-typed
/// <see cref="LoggerMessage.Define"/>-equivalent helpers for the rest.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q5 contract.</b> Not a single method here accepts an argument value
/// or return value as a parameter. Function bodies, tool inputs, tool
/// outputs, and skill markdown bodies must never be passed in. Reviewers
/// must reject any new method on this class that accepts a parameter named
/// <c>arguments</c>, <c>args</c>, <c>result</c>, <c>response</c>, or
/// <c>body</c>.
/// </para>
/// <para>
/// Numeric ids come from <see cref="SkillsLogEvents"/>. Templates use
/// PascalCase property names so they survive serialization to OTLP +
/// Application Insights without renaming.
/// </para>
/// </remarks>
public static partial class SkillsLog
{
    // ====================================================================
    // Registry lifecycle
    // ====================================================================

    [LoggerMessage(
        EventId = 7000,
        EventName = nameof(SkillsLogEvents.SkillRegistryRefresh),
        Level = LogLevel.Information,
        Message = "Skills registry refresh: cause={Cause}, count={Count}, snapshotId={SnapshotId}, prevSnapshotId={PrevSnapshotId}.")]
    public static partial void SkillRegistryRefresh(
        this ILogger logger,
        SkillRegistryRefreshCause cause,
        int count,
        string snapshotId,
        string prevSnapshotId);

    [LoggerMessage(
        EventId = 7001,
        EventName = nameof(SkillsLogEvents.SkillImported),
        Level = LogLevel.Information,
        Message = "Skill imported: name={SkillName}, source={Source}, layer={Layer}.")]
    public static partial void SkillImported(
        this ILogger logger,
        string skillName,
        SkillImportSource source,
        SkillLayer layer);

    [LoggerMessage(
        EventId = 7002,
        EventName = nameof(SkillsLogEvents.SkillRetired),
        Level = LogLevel.Information,
        Message = "Skill retired: name={SkillName}, layer={Layer}.")]
    public static partial void SkillRetired(
        this ILogger logger,
        string skillName,
        SkillLayer layer);

    [LoggerMessage(
        EventId = 7003,
        EventName = nameof(SkillsLogEvents.SkillValidationFailed),
        Level = LogLevel.Warning,
        Message = "Skill validation failed: skillPath={SkillPath}, layer={Layer}, reason={Reason}.")]
    public static partial void SkillValidationFailed(
        this ILogger logger,
        string skillPath,
        SkillLayer layer,
        string reason);

    // ====================================================================
    // Per-agent enable state
    // ====================================================================

    [LoggerMessage(
        EventId = 7020,
        EventName = nameof(SkillsLogEvents.SkillEnabled),
        Level = LogLevel.Information,
        Message = "Skill enabled: skill={SkillName}, agent={AgentId}, requestedBy={RequestedBy}.")]
    public static partial void SkillEnabled(
        this ILogger logger,
        string skillName,
        string agentId,
        string requestedBy);

    [LoggerMessage(
        EventId = 7021,
        EventName = nameof(SkillsLogEvents.SkillDisabled),
        Level = LogLevel.Information,
        Message = "Skill disabled: skill={SkillName}, agent={AgentId}, requestedBy={RequestedBy}.")]
    public static partial void SkillDisabled(
        this ILogger logger,
        string skillName,
        string agentId,
        string requestedBy);

    [LoggerMessage(
        EventId = 7022,
        EventName = nameof(SkillsLogEvents.SkillEnabledStateChanged),
        Level = LogLevel.Information,
        Message = "Skill enable-state bulk override: agent={AgentId}, count={Count}, requestedBy={RequestedBy}.")]
    public static partial void SkillEnabledStateChanged(
        this ILogger logger,
        string agentId,
        int count,
        string requestedBy);

    // ====================================================================
    // Turn / function invocation (HOT PATH — source-generated)
    // ====================================================================

    [LoggerMessage(
        EventId = 7040,
        EventName = nameof(SkillsLogEvents.SkillSnapshotPinned),
        Level = LogLevel.Debug,
        Message = "Skills snapshot pinned for turn: snapshotId={SnapshotId}, agent={AgentId}, chat={ChatId}, count={Count}.")]
    public static partial void SkillSnapshotPinned(
        this ILogger logger,
        string snapshotId,
        string agentId,
        string chatId,
        int count);

    /// <summary>
    /// Q5 hard contract: <b>no</b> arguments, no return values, no payload.
    /// Only identifiers. If this signature ever needs to change, route the
    /// PR through Drummond.
    /// </summary>
    [LoggerMessage(
        EventId = 7041,
        EventName = nameof(SkillsLogEvents.SkillFunctionInvoked),
        Level = LogLevel.Debug,
        Message = "Skill function invoked: skill={SkillName}, function={FunctionName}, agent={AgentId}.")]
    public static partial void SkillFunctionInvoked(
        this ILogger logger,
        string skillName,
        string functionName,
        string agentId);

    /// <summary>
    /// Q5 hard contract: outcome and timing only — never a return body.
    /// </summary>
    [LoggerMessage(
        EventId = 7042,
        EventName = nameof(SkillsLogEvents.SkillFunctionCompleted),
        Level = LogLevel.Debug,
        Message = "Skill function completed: skill={SkillName}, function={FunctionName}, durationMs={DurationMs}, status={Status}.")]
    public static partial void SkillFunctionCompleted(
        this ILogger logger,
        string skillName,
        string functionName,
        long durationMs,
        SkillFunctionStatus status);

    // ====================================================================
    // External import flow (K-4 will fire these — schema fixed now)
    // ====================================================================

    [LoggerMessage(
        EventId = 7060,
        EventName = nameof(SkillsLogEvents.SkillImportRequested),
        Level = LogLevel.Information,
        Message = "Skill import requested: skill={SkillName}, source={Source}, requestedBy={RequestedBy}.")]
    public static partial void SkillImportRequested(
        this ILogger logger,
        string skillName,
        string source,
        string requestedBy);

    [LoggerMessage(
        EventId = 7061,
        EventName = nameof(SkillsLogEvents.SkillImportApproved),
        Level = LogLevel.Information,
        Message = "Skill import approved: skill={SkillName}, source={Source}, approvedBy={ApprovedBy}.")]
    public static partial void SkillImportApproved(
        this ILogger logger,
        string skillName,
        string source,
        string approvedBy);
}

using Microsoft.Extensions.Logging;

namespace OpenClawNet.Skills;

/// <summary>
/// K-2 — Stable, structured event-id taxonomy for skill activity logs.
/// </summary>
/// <remarks>
/// <para>
/// Event ids live in the <c>7000-7099</c> range (skills audit). The numeric
/// id is part of the public log contract: dashboards, alert rules, and the
/// K-3 admin UI key off these ids. <b>Never reuse a retired id.</b> When an
/// event becomes obsolete, leave its id assigned and add a new one for the
/// replacement.
/// </para>
/// <para>
/// All events here are emitted via <see cref="SkillsLog"/>'s
/// <see cref="LoggerMessageAttribute"/>-generated extension methods — never
/// via raw <c>logger.LogInformation</c>. This guarantees structured
/// properties and Q5 hardening (no body / argument / return content ever
/// reaches the log pipeline).
/// </para>
/// </remarks>
public static class SkillsLogEvents
{
    // Registry / lifecycle (7000-7019)
    public static readonly EventId SkillRegistryRefresh = new(7000, nameof(SkillRegistryRefresh));
    public static readonly EventId SkillImported = new(7001, nameof(SkillImported));
    public static readonly EventId SkillRetired = new(7002, nameof(SkillRetired));
    public static readonly EventId SkillValidationFailed = new(7003, nameof(SkillValidationFailed));

    // Per-agent enable state (7020-7039)
    public static readonly EventId SkillEnabled = new(7020, nameof(SkillEnabled));
    public static readonly EventId SkillDisabled = new(7021, nameof(SkillDisabled));
    public static readonly EventId SkillEnabledStateChanged = new(7022, nameof(SkillEnabledStateChanged));

    // Turn / function-invocation (7040-7059) — hot-path
    public static readonly EventId SkillSnapshotPinned = new(7040, nameof(SkillSnapshotPinned));
    public static readonly EventId SkillFunctionInvoked = new(7041, nameof(SkillFunctionInvoked));
    public static readonly EventId SkillFunctionCompleted = new(7042, nameof(SkillFunctionCompleted));

    // External import flow (7060-7079) — schema for K-4 to plug into.
    public static readonly EventId SkillImportRequested = new(7060, nameof(SkillImportRequested));
    public static readonly EventId SkillImportApproved = new(7061, nameof(SkillImportApproved));
}

/// <summary>
/// K-2 — Cause attribution for <see cref="SkillsLogEvents.SkillRegistryRefresh"/>.
/// </summary>
public enum SkillRegistryRefreshCause
{
    /// <summary>Initial build during DI construction.</summary>
    Startup = 0,
    /// <summary>FileSystemWatcher debounce fired and triggered a rebuild.</summary>
    Watcher = 1,
    /// <summary>An explicit caller (endpoint, test, admin tool) triggered a rebuild.</summary>
    Manual = 2,
}

/// <summary>
/// K-2 — Source attribution for <see cref="SkillsLogEvents.SkillImported"/>.
/// </summary>
public enum SkillImportSource
{
    /// <summary>Hand-installed (drag-drop or admin endpoint).</summary>
    Manual = 0,
    /// <summary>Pulled from an external registry (K-4 import flow).</summary>
    External = 1,
}

/// <summary>
/// K-2 — Outcome attribution for <see cref="SkillsLogEvents.SkillFunctionCompleted"/>.
/// </summary>
public enum SkillFunctionStatus
{
    Success = 0,
    Error = 1,
}

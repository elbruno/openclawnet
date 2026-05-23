namespace OpenClawNet.Models.Abstractions;

/// <summary>
/// Describes a named agent configuration — the provider, instructions, and
/// default parameter overrides to use when creating an <see cref="IAgentProvider"/> session.
/// The model itself is owned by the referenced <c>ModelProviderDefinition</c>; the agent
/// is bound to a provider, and the provider supplies the <c>IChatClient</c> with the model
/// it was configured with (Bruno's directive, PR-F).
/// </summary>
public sealed class AgentProfile
{
    public required string Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string? DeploymentName { get; set; }
    public string? AuthMode { get; set; }
    public string? Instructions { get; set; }
    public string? EnabledTools { get; set; }
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    public bool IsDefault { get; set; }

    /// <summary>
    /// Retrieval mode for this agent. Off disables prompt-time retrieval entirely.
    /// </summary>
    public RetrievalLevel RetrievalLevel { get; set; } = RetrievalLevel.Off;

    /// <summary>
    /// Discriminator that controls where this profile can be selected.
    /// <see cref="ProfileKind.Standard"/> profiles appear in chat / job pickers.
    /// <see cref="ProfileKind.System"/> profiles are used for internal platform tasks.
    /// <see cref="ProfileKind.ToolTester"/> profiles are only used by the Tool Test surface.
    /// Defaults to Standard for backward compatibility.
    /// </summary>
    public ProfileKind Kind { get; set; } = ProfileKind.Standard;

    /// <summary>
    /// When true, the agent runtime must pause and request explicit user approval
    /// before executing any tool whose <c>ToolMetadata.RequiresApproval</c> is true.
    /// Defaults to <c>true</c> (safe-by-default per Wave 4 directive 2026-04-19).
    /// Disable for unattended/cron jobs that need to run without a human in the loop.
    /// </summary>
    public bool RequireToolApproval { get; set; } = true;

    /// <summary>Whether this profile is enabled. Disabled profiles are excluded from default/active selection.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>When this profile was last tested.</summary>
    public DateTime? LastTestedAt { get; set; }

    /// <summary>Result of the last test (null = never tested, true = success, false = failure).</summary>
    public bool? LastTestSucceeded { get; set; }

    /// <summary>Error message from the last test (populated only when LastTestSucceeded is false).</summary>
    public string? LastTestError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

namespace OpenClawNet.Storage.Entities;

/// <summary>
/// EF Core entity for persisting <see cref="Models.Abstractions.AgentProfile"/> configurations.
/// </summary>
public class AgentProfileEntity
{
    public string Name { get; set; } = null!;
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

    /// <summary>Retrieval mode for prompt-time memory lookup.</summary>
    public OpenClawNet.Models.Abstractions.RetrievalLevel RetrievalLevel { get; set; } = OpenClawNet.Models.Abstractions.RetrievalLevel.Off;

    /// <summary>
    /// Discriminator (Standard | System | ToolTester) persisted as a string column.
    /// See <see cref="OpenClawNet.Models.Abstractions.ProfileKind"/>.
    /// </summary>
    public OpenClawNet.Models.Abstractions.ProfileKind Kind { get; set; } = OpenClawNet.Models.Abstractions.ProfileKind.Standard;

    /// <summary>
    /// Persisted flag — when true, tool calls that declare RequiresApproval must
    /// receive explicit user approval before execution. Defaults to true.
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

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

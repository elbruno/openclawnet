namespace OpenClawNet.Web.Models;

public sealed record AgentProfileDto(
    string Name,
    string? DisplayName,
    string? Provider,
    string? Instructions,
    string[]? EnabledTools,
    double? Temperature,
    int? MaxTokens,
    bool IsDefault,
    bool RequireToolApproval,
    bool IsEnabled,
    DateTime? LastTestedAt,
    bool? LastTestSucceeded,
    string? LastTestError,
    string Kind = "Standard");

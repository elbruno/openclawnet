namespace OpenClawNet.Agent.ToolApproval;

/// <summary>
/// Configuration options for <see cref="DefaultToolResultSanitizer"/>.
/// Feature 2 Story 2 — enables runtime tuning of sanitizer defense thresholds.
/// </summary>
public sealed class ToolResultSanitizerOptions
{
    public const string SectionName = "ToolResultSanitizer";

    /// <summary>Maximum characters retained from raw content. Excess is truncated.</summary>
    public int MaxLength { get; set; } = 64 * 1024;

    /// <summary>
    /// Maximum characters per line before rejection (prevents pathological line-length attacks).
    /// Default 10,000 chars/line.
    /// </summary>
    public int MaxLineLength { get; set; } = 10_000;
}
